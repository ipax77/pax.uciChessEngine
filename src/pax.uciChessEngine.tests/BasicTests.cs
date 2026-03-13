using pax.chess;
using pax.chess.Analyze;

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
        var eval = status.GetEval(chess.PieceColor.White);
        Assert.IsNotNull(eval);
        Assert.AreEqual(0, eval.Score);
        Assert.AreEqual(1, eval.Mate);
        Assert.AreEqual(10000, eval.ChartScore);
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
}
