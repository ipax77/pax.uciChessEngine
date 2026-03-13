
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using pax.chess;
using pax.chess.Extensions;
using pax.uciChessEngine.EngineServices;

namespace pax.uciChessEngine;

public interface IGameAnalysis
{
    Task<IReadOnlyList<AnalysisEval>> AnalyseGame();
    IAsyncEnumerable<AnalysisEval> AnalyseGameStream(CancellationToken cancellationToken = default);
    ValueTask DisposeAsync();
}

public sealed partial class GameAnalysis : IAsyncDisposable, IGameAnalysis
{
    private readonly string? _engineBinary;
    private readonly IEngineSessionProvider? _sessionProvider;
    private readonly ChessGame _game;
    private readonly int _threads;
    private readonly CancellationTokenSource cts = new();

    private readonly int _thinkTimePerMoveMs;
    private readonly int pvs = 2;

    public GameAnalysis(string engineBinary, ChessGame game, int threads = 8, int thinkTimePerMoveMs = 1000)
    {
        _engineBinary = engineBinary ?? throw new ArgumentNullException(nameof(engineBinary));
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _threads = Math.Max(1, threads);
        _thinkTimePerMoveMs = thinkTimePerMoveMs;
    }

    public GameAnalysis(IEngineSessionProvider sessionProvider, ChessGame game, int threads = 8, int thinkTimePerMoveMs = 1000)
    {
        _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _threads = Math.Max(1, threads);
        _thinkTimePerMoveMs = thinkTimePerMoveMs;
    }

    public async IAsyncEnumerable<AnalysisEval> AnalyseGameStream(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
        var token = linkedCts.Token;

        var engineMoves = _game.Moves.Select(m => Uci.GetUci(m.Move)).ToList();

        var workChannel = Channel.CreateBounded<AnalysisTask>(_threads * 4);
        var resultChannel = Channel.CreateUnbounded<AnalysisEval>();

        var workers = Enumerable.Range(0, _threads)
            .Select(_ => WorkerCore(workChannel.Reader, resultChannel.Writer, token))
            .ToList();

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < engineMoves.Count; i++)
            {
                var position = string.Join(' ', engineMoves.Take(i + 1));

                var side = i % 2 == 0
                    ? PieceColor.Black
                    : PieceColor.White;

                await workChannel.Writer.WriteAsync(
                    new AnalysisTask(i + 1, position, side),
                    token);
            }

            workChannel.Writer.Complete();
        }, token);

        _ = Task.Run(async () =>
        {
            await Task.WhenAll(workers);
            resultChannel.Writer.Complete();
        }, token);

        await foreach (var result in resultChannel.Reader.ReadAllAsync(token))
            yield return result;
    }

    public async Task<IReadOnlyList<AnalysisEval>> AnalyseGame()
    {
        var engineMoves = _game.Moves.Select(m => Uci.GetUci(m.Move)).ToList();

        var workChannel = Channel.CreateBounded<AnalysisTask>(_threads * 4);
        var resultChannel = Channel.CreateUnbounded<AnalysisEval>();

        var workers = Enumerable.Range(0, _threads)
            .Select(_ => WorkerCore(workChannel.Reader, resultChannel.Writer, cts.Token))
            .ToList();

        // enqueue work
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < engineMoves.Count; i++)
            {
                var position = string.Join(' ', engineMoves.Take(i + 1));

                var side = i % 2 == 0
                    ? PieceColor.Black
                    : PieceColor.White;

                await workChannel.Writer.WriteAsync(
                    new AnalysisTask(i + 1, position, side),
                    cts.Token);
            }

            workChannel.Writer.Complete();
        });

        var results = new List<AnalysisEval>();

        var collector = Task.Run(async () =>
        {
            await foreach (var eval in resultChannel.Reader.ReadAllAsync(cts.Token))
                results.Add(eval);
        });

        await Task.WhenAll(workers);

        resultChannel.Writer.Complete();

        await collector;

        return results.OrderBy(x => x.MoveNumber).ToList();
    }

    private async Task WorkerCore(
        ChannelReader<AnalysisTask> reader,
        ChannelWriter<AnalysisEval> writer,
        CancellationToken token)
    {
        if (_sessionProvider != null)
        {
            await foreach (var task in reader.ReadAllAsync(token))
            {
                await using var lease = await _sessionProvider.AcquireAsync(token);
                var eval = await lease.Session.UseAsync(async engine =>
                {
                    await engine.SendAsync(
                        $"position startpos moves {task.Position}",
                        token);

                    await engine.SendAsync(
                        $"go movetime {_thinkTimePerMoveMs}",
                        token);

                    await Task.Delay(_thinkTimePerMoveMs + 25, token);
                    return GetEval(engine, task.SideToMove);
                }, token);

                await writer.WriteAsync(new AnalysisEval
                {
                    MoveNumber = task.MoveNumber,
                    Eval = eval
                }, token);
            }
        }
        else
        {
            await using var engine = new UciEngine(_engineBinary!);

            await engine.StartAsync(token);
            await engine.SendAsync("ucinewgame", token);

            await engine.SetOption("Threads", 1, token);
            await engine.SetOption("MultiPV", pvs, token);

            await foreach (var task in reader.ReadAllAsync(token))
            {
                await engine.SendAsync(
                    $"position startpos moves {task.Position}",
                    token);

                await engine.SendAsync(
                    $"go movetime {_thinkTimePerMoveMs}",
                    token);

                await Task.Delay(_thinkTimePerMoveMs + 25, token);
                var eval = GetEval(engine, task.SideToMove);

                await writer.WriteAsync(new AnalysisEval
                {
                    MoveNumber = task.MoveNumber,
                    Eval = eval
                }, token);
            }
        }
    }

    private static Eval? GetEval(UciEngine engine, PieceColor sideToMove)
    {
        return engine.Status.GetEval(sideToMove);
    }

    public async ValueTask DisposeAsync()
    {
        await cts.CancelAsync();
        cts.Dispose();
    }
}

public sealed record AnalysisEval
{
    public int MoveNumber { get; init; }
    public Eval? Eval { get; init; }
}

internal sealed record AnalysisTask(
    int MoveNumber,
    string Position,
    PieceColor SideToMove
);