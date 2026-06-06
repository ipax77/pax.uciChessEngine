using pax.chess;

namespace pax.uciChessEngine.tests;

[TestClass]
public sealed class BasicTests
{
    private const string EnginePath = "unused-uci-engine-path";

    [TestMethod]
    public void CanCreateEval()
    {
        var msg = "info depth 24 seldepth 49 multipv 1 score cp 35 nodes 2055087 nps 546420 hashfull 663 tbhits 0 time 3761 pv e2e4 e7e5 g1f3 b8c6 f1b5 g8f6 e1g1 f6e4 f1e1 e4d6 f3e5 f8e7 b5f1 c6e5 e1e5 e8g8 d2d4 e7f6 e5e1 d6f5 c2c3 d7d5 c1f4 a7a5 b1d2 c7c6 d2f3 g7g6 f1d3 f5g7 d1c2";
        var engine = new UciEngine(EnginePath);
        engine.TestParseUciString(msg);
        var status = engine.Status;
        var eval = status.GetEval(PieceColor.White);
        Assert.IsNotNull(eval);
        Assert.AreEqual(35, eval.Score);
    }

    [TestMethod]
    public void CanCreateEval2()
    {
        var msg = "info depth 245 seldepth 2 multipv 1 score mate 1 nodes 10540 nps 103333 hashfull 0 tbhits 0 time 102 pv h5f7";
        var engine = new UciEngine(EnginePath);
        engine.TestParseUciString(msg);
        var status = engine.Status;
        var eval = status.GetEval(PieceColor.White);
        Assert.IsNotNull(eval);
        Assert.AreEqual(0, eval.Score);
        Assert.AreEqual(1, eval.Mate);
    }

    [TestMethod]
    public void CanParseCentipawnInfo()
    {
        var msg = "info depth 24 seldepth 49 multipv 1 score cp 35 nodes 2055087 nps 546420 hashfull 663 tbhits 0 time 3761 pv e2e4 e7e5 g1f3";
        var engine = new UciEngine(EnginePath);

        engine.TestParseUciString(msg);

        var eval = engine.Status.GetEval(PieceColor.White);
        Assert.IsNotNull(eval);
        Assert.AreEqual(35, eval.Score);
        Assert.AreEqual(24, eval.Depth);
        Assert.AreEqual(49, eval.PvInfo.SelDepth);
        Assert.AreEqual(2055087, eval.PvInfo.Nodes);
        Assert.HasCount(3, eval.PvInfo.Moves);
    }

    [TestMethod]
    public void CanParseMateInfo()
    {
        var msg = "info depth 245 seldepth 2 multipv 1 score mate 1 nodes 10540 nps 103333 hashfull 0 tbhits 0 time 102 pv h5f7";
        var engine = new UciEngine(EnginePath);

        engine.TestParseUciString(msg);

        var eval = engine.Status.GetEval(PieceColor.White);
        Assert.IsNotNull(eval);
        Assert.AreEqual(0, eval.Score);
        Assert.AreEqual(1, eval.Mate);
        Assert.AreEqual(1, eval.PvInfo.Mate);
    }

    [TestMethod]
    public void CanParseMultiPvInfo()
    {
        var engine = new UciEngine(EnginePath);

        engine.TestParseUciString("info depth 12 seldepth 20 multipv 2 score cp -14 nodes 123 nps 456 hashfull 7 tbhits 0 time 89 pv d2d4 d7d5");

        Assert.IsTrue(engine.Status.Pvs.ContainsKey(2));
        var pv = engine.Status.Pvs[2];
        Assert.AreEqual(2, pv.MultiPv);
        Assert.AreEqual(-14, pv.Centipawns);
    }

    [TestMethod]
    public void CanParseBestMoveNone()
    {
        var engine = new UciEngine(EnginePath);

        engine.TestParseUciString("bestmove (none)");

        Assert.IsNull(engine.Status.BestMove);
        Assert.IsNull(engine.Status.Ponder);
        Assert.AreEqual(EngineState.BestMove, engine.Status.EngineState);
    }

    [TestMethod]
    public void CanParseStartupOptions()
    {
        var engine = new UciEngine(EnginePath);

        engine.TestParseUciString("option name Threads type spin default 1 min 1 max 1024");
        engine.TestParseUciString("option name Use NNUE type check default true");
        engine.TestParseUciString("option name EvalFile type string default <empty>");
        engine.TestParseUciString("option name Skill Level type combo default Normal var Easy var Normal var Hard");

        Assert.HasCount(4, engine.Status.EngineOptions);

        var threads = engine.Status.EngineOptions.Single(o => o.Name == "Threads");
        Assert.AreEqual("spin", threads.Type);
        Assert.AreEqual(1, threads.Min);
        Assert.AreEqual(1024, threads.Max);

        var nnue = engine.Status.EngineOptions.Single(o => o.Name == "Use NNUE");
        Assert.AreEqual("check", nnue.Type);
        Assert.IsTrue((bool)nnue.Default);

        var evalFile = engine.Status.EngineOptions.Single(o => o.Name == "EvalFile");
        Assert.AreEqual("string", evalFile.Type);
        Assert.AreEqual(string.Empty, evalFile.Default);

        var skill = engine.Status.EngineOptions.Single(o => o.Name == "Skill Level");
        Assert.AreEqual("combo", skill.Type);
        Assert.IsNotNull(skill.Vars);
        CollectionAssert.AreEqual(new[] { "Easy", "Normal", "Hard" }, skill.Vars.ToArray());
    }
}
