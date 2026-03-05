
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using pax.chess;
using pax.chess.Extensions;

namespace pax.uciChessEngine;

public sealed partial class GameAnalysis(string engineBinary, ChessGame game, int threads = 8) : IAsyncDisposable
{
    private readonly int _threads = Math.Max(1, threads);
    private readonly CancellationTokenSource cts = new();

    private readonly int thinkTimePerMoveMs = 1000;
    private readonly int pvs = 2;

    public async IAsyncEnumerable<AnalysisEval> AnalyseGameStream(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
        var token = linkedCts.Token;

        var engineMoves = game.Moves.Select(m => Uci.GetUci(m.Move)).ToList();

        var workChannel = Channel.CreateBounded<AnalysisTask>(_threads * 4);
        var resultChannel = Channel.CreateUnbounded<AnalysisEval>();

        var workers = Enumerable.Range(0, _threads)
            .Select(_ => StreamWorker(workChannel.Reader, resultChannel.Writer, token))
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
        var engineMoves = game.Moves.Select(m => Uci.GetUci(m.Move)).ToList();

        var workChannel = Channel.CreateBounded<AnalysisTask>(_threads * 4);
        var resultChannel = Channel.CreateUnbounded<AnalysisEval>();

        var workers = Enumerable.Range(0, _threads)
            .Select(_ => Worker(workChannel.Reader, resultChannel.Writer))
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

        _ = Task.Run(async () =>
        {
            await foreach (var eval in resultChannel.Reader.ReadAllAsync(cts.Token))
                results.Add(eval);
        });

        await Task.WhenAll(workers);

        resultChannel.Writer.Complete();

        await Task.Delay(50);

        return results.OrderBy(x => x.MoveNumber).ToList();
    }

    private async Task Worker(
        ChannelReader<AnalysisTask> reader,
        ChannelWriter<AnalysisEval> writer)
    {
        await using var engine = new UciEngine(engineBinary);

        await engine.StartAsync();
        await engine.SendAsync("ucinewgame", cts.Token);

        await engine.SetOption("Threads", 1, cts.Token);
        await engine.SetOption("MultiPV", pvs, cts.Token);

        await foreach (var task in reader.ReadAllAsync(cts.Token))
        {
            await engine.SendAsync(
                $"position startpos moves {task.Position}",
                cts.Token);

            await engine.SendAsync(
                $"go movetime {thinkTimePerMoveMs}",
                cts.Token);

            await Task.Delay(thinkTimePerMoveMs + 25, cts.Token);
            var eval = GetEval(engine, task.SideToMove);

            await writer.WriteAsync(new AnalysisEval
            {
                MoveNumber = task.MoveNumber,
                Eval = eval
            }, cts.Token);
        }
    }

    private async Task StreamWorker(
    ChannelReader<AnalysisTask> reader,
    ChannelWriter<AnalysisEval> writer,
    CancellationToken token)
    {
        await using var engine = new UciEngine(engineBinary);

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
                $"go movetime {thinkTimePerMoveMs}",
                token);

            await Task.Delay(thinkTimePerMoveMs + 25, cts.Token);
            
            var eval = GetEval(engine, task.SideToMove);

            await writer.WriteAsync(new AnalysisEval
            {
                MoveNumber = task.MoveNumber,
                Eval = eval
            }, token);
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