namespace pax.uciChessEngine.tests;

[TestClass]
public sealed class BasicTests
{
    [TestMethod]
    public async Task CanStartEngine()
    {
        var binaryPath = @"C:\data\chess\engines\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
        var engine = new UciEngine(binaryPath);
        await engine.StartAsync();
        // string bestMove = await engine.GetBestMoveAsync(
        //     "rn1qkbnr/pppb1ppp/3p4/4p3/4P3/3P1N2/PPP2PPP/RNBQKB1R w KQkq - 0 5",
        //     TimeSpan.FromSeconds(1));
        // Assert.IsNotEmpty(bestMove);
        await engine.DisposeAsync();
    }

    [TestMethod]
    public void CanCreateEval()
    {
        var msg = "info depth 24 seldepth 49 multipv 1 score cp 35 nodes 2055087 nps 546420 hashfull 663 tbhits 0 time 3761 pv e2e4 e7e5 g1f3 b8c6 f1b5 g8f6 e1g1 f6e4 f1e1 e4d6 f3e5 f8e7 b5f1 c6e5 e1e5 e8g8 d2d4 e7f6 e5e1 d6f5 c2c3 d7d5 c1f4 a7a5 b1d2 c7c6 d2f3 g7g6 f1d3 f5g7 d1c2";
        var binaryPath = @"C:\data\chess\engines\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
        var engine = new UciEngine(binaryPath);
        engine.TestParseUciString(msg);
        var status = engine.Status;
        var eval = status.GetEval(chess.PieceColor.White);
        Assert.IsNotNull(eval);
        Assert.AreEqual(35, eval.Score);
    }

    [TestMethod]
    public void CanCreateEval2()
    {
        var msg = "info depth 245 seldepth 2 multipv 1 score mate 1 nodes 10540 nps 103333 hashfull 0 tbhits 0 time 102 pv h5f7";
        var binaryPath = @"C:\data\chess\engines\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
        var engine = new UciEngine(binaryPath);
        engine.TestParseUciString(msg);
        var status = engine.Status;
        var eval = status.GetEval(chess.PieceColor.White);
        Assert.IsNotNull(eval);
        Assert.AreEqual(0, eval.Score);
        Assert.AreEqual(1, eval.Mate);
        Assert.AreEqual(10000, eval.ChartScore);
    }
}
