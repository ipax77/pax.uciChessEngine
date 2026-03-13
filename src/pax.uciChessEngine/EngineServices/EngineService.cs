using pax.chess;
using System.Runtime.CompilerServices;

namespace pax.uciChessEngine.EngineServices;

public static class EngineService
{
    private static readonly Dictionary<Guid, IEngineSessionProvider> engineProviders = [];
    private static readonly Lock engineProviderLock = new();

    public static IEngineSessionProvider GetEngineSessionProvider(EngineRunOptions engineRunOptions)
    {
        ArgumentNullException.ThrowIfNull(engineRunOptions);

        lock (engineProviderLock)
        {
            if (!engineProviders.TryGetValue(engineRunOptions.Id, out var provider))
            {
                provider = new EngineSessionProvider(engineRunOptions);
                engineProviders.Add(engineRunOptions.Id, provider);
            }

            return provider;
        }
    }

    public static async Task<string> GetEngineName(EngineRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await using var engine = new UciEngine(options.BinaryPath);
        return engine.Status.Name;
    }

    public static async Task<List<Eval>> GetEvaluation(
        string fen,
        PieceColor sideToMove,
        TimeSpan thinkTime,
        UciEngine engine,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        try
        {
            await engine.SendAsync($"position fen {fen}", token);

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? _, MoveEventArgs __)
                => tcs.TrySetResult();

            engine.MoveReady += Handler;

            try
            {
                await engine.SendAsync($"go movetime {(int)thinkTime.TotalMilliseconds}", token);
                await tcs.Task.WaitAsync(token);
            }
            finally
            {
                engine.MoveReady -= Handler;
            }

            var status = engine.Status;

            return GetEvaluations(status, sideToMove);
        }
        catch (OperationCanceledException)
        {
            await engine.SendAsync("stop", CancellationToken.None);
            throw;
        }
    }

    public static async IAsyncEnumerable<List<Eval>> GetContinualEvaluation(
        string moves,
        PieceColor sideToMove,
        UciEngine engine,
        [EnumeratorCancellation] CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(engine);

        try
        {
            await engine.SendAsync($"position startpos moves {moves}", token);

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? _, MoveEventArgs __)
                => tcs.TrySetResult();

            engine.MoveReady += Handler;

            try
            {
                await engine.SendAsync("go", token);

                while (!tcs.Task.IsCompleted && !token.IsCancellationRequested)
                {
                    await Task.Delay(100, token);
                    yield return GetEvaluations(engine.Status, sideToMove);
                }
            }
            finally
            {
                engine.MoveReady -= Handler;
            }

            yield return GetEvaluations(engine.Status, sideToMove);
        }
        finally
        {
            await engine.SendAsync("stop", CancellationToken.None);
        }
    }

    private static List<Eval> GetEvaluations(Status status, PieceColor sideToMove)
    {
        List<Eval> evals = [];
        foreach (var pv in status.Pvs.Values.OrderBy(o => o.MultiPv))
        {
            var vals = pv.GetValues();
            var moves = pv.GetMoves();
            var pvInfo = new PvInfo(pv.MultiPv, vals, moves);
            var score = pvInfo.Score;
            var mate = pvInfo.Mate;

            if (sideToMove == PieceColor.Black)
            {
                score = -score;
                mate = -mate;
            }

            evals.Add(new Eval()
            {
                Score = score,
                Mate = mate,
                Depth = pvInfo.Depth,
                PvInfo = pvInfo,
            });
        }

        return evals;
    }
}
