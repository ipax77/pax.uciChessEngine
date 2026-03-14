using pax.chess;
using pax.chess.Analyze;
using pax.uciChessEngine.EngineServices;

namespace pax.uciChessEngine.tests;

[TestClass]
public sealed class BasicTests
{
    private readonly string binaryPath = @"C:\data\chess\engines\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";

    [TestMethod]
    public async Task CanStartEngine()
    {
        await using var engine = new UciEngine(binaryPath);
        await engine.StartAsync();
    }

    [TestMethod]
    public void CanCreateEval()
    {
        var msg = "info depth 24 seldepth 49 multipv 1 score cp 35 nodes 2055087 nps 546420 hashfull 663 tbhits 0 time 3761 pv e2e4 e7e5 g1f3 b8c6 f1b5 g8f6 e1g1 f6e4 f1e1 e4d6 f3e5 f8e7 b5f1 c6e5 e1e5 e8g8 d2d4 e7f6 e5e1 d6f5 c2c3 d7d5 c1f4 a7a5 b1d2 c7c6 d2f3 g7g6 f1d3 f5g7 d1c2";
        var engine = new UciEngine(binaryPath);
        engine.TestParseUciString(msg);
        var status = engine.Status;
        var eval = status.GetEval(chess.PieceColor.White);
        Assert.IsNotNull(eval);
        Assert.AreEqual(35, eval.Score);
    }

    [TestMethod]
    public async Task CanCreateEval2()
    {
        var msg = "info depth 245 seldepth 2 multipv 1 score mate 1 nodes 10540 nps 103333 hashfull 0 tbhits 0 time 102 pv h5f7";
        await using var engine = new UciEngine(binaryPath);
        engine.TestParseUciString(msg);
        var status = engine.Status;
        var eval = status.GetEval(PieceColor.White);
        Assert.IsNotNull(eval);
        Assert.AreEqual(0, eval.Score);
        Assert.AreEqual(1, eval.Mate);
    }

    [TestMethod]
    public async Task CanCreateEval3()
    {
        ChessGame chessGame = PgnSerializer.Parse("1. e4 d6 2. Ne2 Nf6 3. Nbc3 g6 4. g3 Bg7 5. Bg2 O-O 6. d3 c5 7. h3 Nc6 8. O-O Ne8 9. f4 Nc7 10. Be3 Rb8 11. Qd2 b5 12. e5 Bb7 13. exd6 exd6");
        AnalysisBoard analysisBoard = new(chessGame);
        await using var engine = new UciEngine(binaryPath);
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
        var options = new EngineRunOptions { BinaryPath = binaryPath, PoolSize = 1 };

        var provider1 = EngineService.GetEngineSessionProvider(options);
        var provider2 = EngineService.GetEngineSessionProvider(options);

        Assert.AreSame(provider1, provider2);
    }

    [TestMethod]
    public async Task ReusesSessionAfterLeaseDisposal()
    {
        var options = new EngineRunOptions { BinaryPath = binaryPath, PoolSize = 1, IdelTimeoutMs = 10_000 };
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
        var options = new EngineRunOptions { BinaryPath = binaryPath, PoolSize = 1, IdelTimeoutMs = 10_000 };
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
        var options = new EngineRunOptions { BinaryPath = binaryPath, PoolSize = 1, IdelTimeoutMs = 10 };
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
        var game = PgnSerializer.Parse("1. e4 e5 2. Bc4 Bc5");
        var options = new EngineRunOptions { BinaryPath = binaryPath, PoolSize = 2, Threads = 1, Pvs = 2 };
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
        ChessGame chessGame = PgnSerializer.Parse("1. e4 d6 2. Ne2 Nf6 3. Nbc3 g6 4. g3 Bg7 5. Bg2 O-O 6. d3 c5 7. h3 Nc6 8. O-O Ne8 9. f4 Nc7 10. Be3 Rb8 11. Qd2 b5 12. e5 Bb7 13. exd6 exd6 14. f5 Re8 15. Rae1 b4 16. Ne4 Bxb2 17. c3 bxc3 18. N2xc3 Ba6 19. fxg6 hxg6 20. Nf6+ Kh8 21. Bd4 Nxd4 22. Qh6# 1-0");
        AnalysisBoard analysisBoard = new(chessGame);
        await using var engine = new UciEngine(binaryPath);
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
        ChessGame chessGame = PgnSerializer.Parse("1. f4 e6 2. g4 Qh4#");
        AnalysisBoard analysisBoard = new(chessGame);
        await using var engine = new UciEngine(binaryPath);
        await engine.StartAsync();
        await engine.SetOption("Threads", 2);
        await engine.SetOption("MultiPV", 2);

        var gameAnalysis = new GameAnalysis(binaryPath, chessGame, 2, 100);
        var results = await gameAnalysis.AnalyseGame();

        Assert.HasCount(4, results);
        var nonNullEvalsCount = results.Count(c => c.Eval != null);
        Assert.AreEqual(3, nonNullEvalsCount);
    }

    [TestMethod]
    public async Task CanCancelGracefully()
    {
        using CancellationTokenSource cts = new();

        var engine1 = new UciEngine(binaryPath);
        var engine2 = new UciEngine(binaryPath);
        var chessGame = new ChessGame();
        var engineGame = new EngineGame(engine1, engine2, chessGame);
        var chessClock = new ChessClock(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        chessGame.SetClock(chessClock);
        
        var gameTask = engineGame.Start(chessClock);
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
        using CancellationTokenSource cts = new();

        var engine1 = new UciEngine(binaryPath);
        var engine2 = new UciEngine(binaryPath);
        var chessGame = new ChessGame();
        var engineGame = new EngineGame(engine1, engine2, chessGame);
        var chessClock = new ChessClock(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        chessGame.SetClock(chessClock);
        
        var gameTask = engineGame.Start(chessClock);
        await Task.Delay(2_000, cts.Token);
        await engineGame.StopGame();

        var completed = await Task.WhenAny(gameTask, Task.Delay(3_000, cts.Token));

        Assert.AreSame(gameTask, completed, "Game task should finish after StopGame is requested.");
        Assert.IsFalse(gameTask.IsCanceled, "Game task should not be canceled.");
        Assert.IsFalse(gameTask.IsFaulted, gameTask.Exception?.ToString());
    }

}
