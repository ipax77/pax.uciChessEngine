using pax.chess;
using pax.chess.Extensions;
using pax.uciChessEngine.EngineServices;

namespace pax.uciChessEngine.tests;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class UciEngineIntegrationTests
{
    [TestMethod]
    public async Task CanStartEngine()
    {
        var enginePath = TestEngine.RequirePath();

        await using var engine = new UciEngine(enginePath);
        await engine.StartAsync();
    }

    [TestMethod]
    public async Task CanCreateEval3()
    {
        var enginePath = TestEngine.RequirePath();
        ChessGame chessGame = PgnSerializer.Parse("1. e4 d6 2. Ne2 Nf6 3. Nbc3 g6 4. g3 Bg7 5. Bg2 O-O 6. d3 c5 7. h3 Nc6 8. O-O Ne8 9. f4 Nc7 10. Be3 Rb8 11. Qd2 b5 12. e5 Bb7 13. exd6 exd6");
        await using var engine = new UciEngine(enginePath);
        await engine.StartAsync();
        await engine.SetOption("Threads", 2);
        await engine.SetOption("MultiPV", 2);

        var fen = FenSerializer.Serialize(chessGame.CurrentPosition);
        var results = await EngineService
            .GetEvaluation(fen, chessGame.CurrentPosition.SideToMove, TimeSpan.FromMilliseconds(200), engine);
        Assert.HasCount(2, results);
        var eval1 = results[0];
        Assert.IsGreaterThan(0, eval1.Depth);
    }

    [TestMethod]
    public void ReturnsSameProviderForSameId()
    {
        var enginePath = TestEngine.RequirePath();
        var options = new EngineRunOptions { BinaryPath = enginePath, PoolSize = 1 };

        var provider1 = EngineService.GetEngineSessionProvider(options);
        var provider2 = EngineService.GetEngineSessionProvider(options);

        Assert.AreSame(provider1, provider2);
    }

    [TestMethod]
    public async Task ReusesSessionAfterLeaseDisposal()
    {
        var enginePath = TestEngine.RequirePath();
        var options = new EngineRunOptions { BinaryPath = enginePath, PoolSize = 1, IdelTimeoutMs = 10_000 };
        var provider = EngineService.GetEngineSessionProvider(options);

        EngineSession firstSession;
        await using (var lease = await provider.AcquireAsync(CancellationToken.None))
        {
            firstSession = lease.Session;
        }

        await using var lease2 = await provider.AcquireAsync(CancellationToken.None);
        Assert.AreSame(firstSession, lease2.Session);
    }

    [TestMethod]
    public async Task SetsLastUsedOnLeaseDispose()
    {
        var enginePath = TestEngine.RequirePath();
        var options = new EngineRunOptions { BinaryPath = enginePath, PoolSize = 1, IdelTimeoutMs = 10_000 };
        var provider = EngineService.GetEngineSessionProvider(options);

        await using var lease = await provider.AcquireAsync(CancellationToken.None);
        var session = lease.Session;
        var initial = session.LastUsedUtc;

        await lease.DisposeAsync();

        Assert.IsTrue(session.LastUsedUtc > initial);
    }

    [TestMethod]
    public async Task CleanupIdleRemovesExpiredSessions()
    {
        var enginePath = TestEngine.RequirePath();
        var options = new EngineRunOptions { BinaryPath = enginePath, PoolSize = 1, IdelTimeoutMs = 10 };
        var provider = EngineService.GetEngineSessionProvider(options);

        EngineSession firstSession;
        await using (var lease = await provider.AcquireAsync(CancellationToken.None))
        {
            firstSession = lease.Session;
        }

        await Task.Delay(30);
        await provider.CleanupIdleAsync();

        await using var lease2 = await provider.AcquireAsync(CancellationToken.None);

        Assert.AreNotSame(firstSession, lease2.Session);
    }

    [TestMethod]
    public async Task GameAnalysisWorksWithSessionProvider()
    {
        var enginePath = TestEngine.RequirePath();
        var game = PgnSerializer.Parse("1. e4 e5 2. Bc4 Bc5");
        var options = new EngineRunOptions { BinaryPath = enginePath, PoolSize = 2, Threads = 1, Pvs = 2 };
        var provider = EngineService.GetEngineSessionProvider(options);

        var gameAnalysis = new GameAnalysis(provider, game, threads: 2);
        var results = await gameAnalysis.AnalyseGame();

        Assert.HasCount(game.Moves.Count, results);
        foreach (var result in results)
        {
            Assert.IsNotNull(result.Eval);
        }
    }

    [TestMethod]
    public async Task CanDetectCheckMate()
    {
        var enginePath = TestEngine.RequirePath();
        ChessGame chessGame = PgnSerializer.Parse("1. e4 d6 2. Ne2 Nf6 3. Nbc3 g6 4. g3 Bg7 5. Bg2 O-O 6. d3 c5 7. h3 Nc6 8. O-O Ne8 9. f4 Nc7 10. Be3 Rb8 11. Qd2 b5 12. e5 Bb7 13. exd6 exd6 14. f5 Re8 15. Rae1 b4 16. Ne4 Bxb2 17. c3 bxc3 18. N2xc3 Ba6 19. fxg6 hxg6 20. Nf6+ Kh8 21. Bd4 Nxd4 22. Qh6# 1-0");
        await using var engine = new UciEngine(enginePath);
        await engine.StartAsync();
        await engine.SetOption("Threads", 2);
        await engine.SetOption("MultiPV", 2);

        var fen = FenSerializer.Serialize(chessGame.CurrentPosition);
        var results = await EngineService
            .GetEvaluation(fen, chessGame.CurrentPosition.SideToMove, TimeSpan.FromMilliseconds(200), engine);
        Assert.HasCount(0, results);
    }

    [TestMethod]
    public async Task CanDetectCheckMate2()
    {
        var enginePath = TestEngine.RequirePath();
        ChessGame chessGame = PgnSerializer.Parse("1. f4 e6 2. g4 Qh4#");
        await using var engine = new UciEngine(enginePath);
        await engine.StartAsync();
        await engine.SetOption("Threads", 2);
        await engine.SetOption("MultiPV", 2);

        var gameAnalysis = new GameAnalysis(enginePath, chessGame, 2, 100);
        var results = await gameAnalysis.AnalyseGame();

        Assert.HasCount(4, results);
        var nonNullEvalsCount = results.Count(c => c.Eval != null);
        Assert.AreEqual(3, nonNullEvalsCount);
    }

    [TestMethod]
    public async Task ReusedEngineDoesNotReturnStalePvForTerminalPosition()
    {
        var enginePath = TestEngine.RequirePath();
        var normalGame = PgnSerializer.Parse("1. e4 e5");
        var terminalGame = PgnSerializer.Parse("1. f4 e6 2. g4 Qh4#");

        await using var engine = new UciEngine(enginePath);
        await engine.StartAsync();
        await engine.SetOption("Threads", 1);
        await engine.SetOption("MultiPV", 2);

        var normalFen = FenSerializer.Serialize(normalGame.CurrentPosition);
        var normal = await EngineService.GetEvaluation(
            normalFen,
            normalGame.CurrentPosition.SideToMove,
            TimeSpan.FromMilliseconds(200),
            engine);
        Assert.IsGreaterThan(0, normal.Count);

        var terminalFen = FenSerializer.Serialize(terminalGame.CurrentPosition);
        var terminal = await EngineService.GetEvaluation(
            terminalFen,
            terminalGame.CurrentPosition.SideToMove,
            TimeSpan.FromMilliseconds(200),
            engine);

        Assert.HasCount(0, terminal);
    }

    [TestMethod]
    public async Task ContinualEvaluationDoesNotReturnStalePvForTerminalPosition()
    {
        var enginePath = TestEngine.RequirePath();
        var normalGame = PgnSerializer.Parse("1. e4 e5");
        var terminalGame = PgnSerializer.Parse("1. f4 e6 2. g4 Qh4#");
        var normalMoves = string.Join(' ', normalGame.Moves.Select(m => Uci.GetUci(m.Move)));
        var terminalMoves = string.Join(' ', terminalGame.Moves.Select(m => Uci.GetUci(m.Move)));

        await using var engine = new UciEngine(enginePath);
        await engine.StartAsync();
        await engine.SetOption("Threads", 1);
        await engine.SetOption("MultiPV", 2);

        await foreach (var evals in EngineService.GetContinualEvaluation(
            normalMoves,
            normalGame.CurrentPosition.SideToMove,
            engine,
            CancellationToken.None))
        {
            if (evals.Count > 0)
                break;
        }

        var terminalSnapshots = new List<List<Eval>>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evals in EngineService.GetContinualEvaluation(
            terminalMoves,
            terminalGame.CurrentPosition.SideToMove,
            engine,
            cts.Token))
        {
            terminalSnapshots.Add(evals);
        }

        Assert.IsGreaterThan(0, terminalSnapshots.Count);
        Assert.IsTrue(terminalSnapshots.All(evals => evals.Count == 0));
    }

    [TestMethod]
    public async Task SearchResetStillAllowsLaterNonTerminalEvaluation()
    {
        var enginePath = TestEngine.RequirePath();
        var terminalGame = PgnSerializer.Parse("1. f4 e6 2. g4 Qh4#");
        var normalGame = PgnSerializer.Parse("1. e4 e5");

        await using var engine = new UciEngine(enginePath);
        await engine.StartAsync();
        await engine.SetOption("Threads", 1);
        await engine.SetOption("MultiPV", 2);

        var terminalFen = FenSerializer.Serialize(terminalGame.CurrentPosition);
        var terminal = await EngineService.GetEvaluation(
            terminalFen,
            terminalGame.CurrentPosition.SideToMove,
            TimeSpan.FromMilliseconds(200),
            engine);
        Assert.HasCount(0, terminal);

        var normalFen = FenSerializer.Serialize(normalGame.CurrentPosition);
        var normal = await EngineService.GetEvaluation(
            normalFen,
            normalGame.CurrentPosition.SideToMove,
            TimeSpan.FromMilliseconds(200),
            engine);
        Assert.IsGreaterThan(0, normal.Count);
    }

    [TestMethod]
    public async Task CanCancelGracefully()
    {
        var enginePath = TestEngine.RequirePath();
        using CancellationTokenSource cts = new();

        using var chessClock = new ChessClock(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        var chessGame = new ChessGame(new ChessGameOptions { Clock = chessClock });
        var engine1 = new UciEngine(enginePath);
        var engine2 = new UciEngine(enginePath);
        await using var engineGame = new EngineGame(engine1, engine2, chessGame);

        var gameTask = engineGame.Start();
        await Task.Delay(100, cts.Token);
        await engineGame.StopGame();

        var completed = await Task.WhenAny(gameTask, Task.Delay(1_000, cts.Token));

        Assert.AreSame(gameTask, completed, "Game task should finish after StopGame is requested.");
        Assert.IsFalse(gameTask.IsCanceled, "Game task should not be canceled.");
        Assert.IsFalse(gameTask.IsFaulted, gameTask.Exception?.ToString());
    }

    [TestMethod]
    public async Task CanCancelGracefullyDuringCalculation()
    {
        var enginePath = TestEngine.RequirePath();
        using CancellationTokenSource cts = new();

        using var chessClock = new ChessClock(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        var chessGame = new ChessGame(new ChessGameOptions { Clock = chessClock });
        var engine1 = new UciEngine(enginePath);
        var engine2 = new UciEngine(enginePath);
        await using var engineGame = new EngineGame(engine1, engine2, chessGame);

        var gameTask = engineGame.Start();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline
            && engine1.Status.EngineState != EngineState.Calculating
            && engine2.Status.EngineState != EngineState.Calculating)
        {
            await Task.Delay(25, cts.Token);
        }

        Assert.IsTrue(
            engine1.Status.EngineState == EngineState.Calculating
            || engine2.Status.EngineState == EngineState.Calculating,
            "Expected one engine to start calculating before StopGame is requested.");

        await engineGame.StopGame();

        var completed = await Task.WhenAny(gameTask, Task.Delay(4_000, cts.Token));

        Assert.AreSame(gameTask, completed, "Game task should finish after StopGame is requested.");
        Assert.IsFalse(gameTask.IsCanceled, "Game task should not be canceled.");
        Assert.IsFalse(gameTask.IsFaulted, gameTask.Exception?.ToString());
    }

    [TestMethod]
    public async Task CanHandleBestMoveOnTerminalPosition()
    {
        var enginePath = TestEngine.RequirePath();
        ChessGame chessGame = PgnSerializer.Parse("1. f4 e6 2. g4 Qh4#");
        var fen = FenSerializer.Serialize(chessGame.CurrentPosition);
        await using var engine = new UciEngine(enginePath);
        await engine.StartAsync();
        var result = await engine.GetBestMoveAsync(fen, TimeSpan.FromSeconds(1));
        Assert.IsNull(result);
    }
}
