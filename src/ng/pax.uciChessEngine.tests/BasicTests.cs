using System.Collections.Concurrent;

namespace pax.uciChessEngine.tests;

[TestClass]
public class UnitTest1
{
    private readonly string engineBinary = @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe";

    [TestMethod]
    public async Task ReadyTest()
    {
        Engine engine = new();
        engine.Start(engineBinary);


        ConcurrentBag<ParseResult> results = new();

        engine.DataReceived += (o, e) =>
        {
            if (e.Result is not null)
            {
                results.Add(e.Result);
            }
        };

        var readyResult = await engine.IsReady();
        Assert.IsTrue(readyResult);
        
        engine.Dispose();
    }

    [TestMethod]
    public async Task OptionsTest()
    {
        Engine engine = new();
        engine.Start(engineBinary);

        var options = await engine.GetOptions();
        Assert.IsTrue(options.Count > 0);
        engine.Dispose();
    }

    [TestMethod]
    public async Task SetOptionsTest()
    {
        Engine engine = new();
        engine.Start(engineBinary);
        await engine.IsReady();

        var optionResult = await engine.SetOption("MultiPV", 2);
        var options = await engine.GetOptions();
        var option = options.FirstOrDefault(f => f.OptionName == "MultiPV");

        Assert.IsTrue(optionResult);
        Assert.IsNotNull(option);
        Assert.AreEqual(2, option.CustomValue);

        engine.Dispose();
    }

    [TestMethod]
    public async Task SetFenPositionTest()
    {
        Engine engine = new();
        engine.Start(engineBinary);
        await engine.IsReady();

        await engine.SetFenPosition("r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4");

        var bestMoveResult = await engine.GetBestMove();
        Assert.AreEqual("h5f7", bestMoveResult?.BestMove);

        engine.Dispose();
    }

    [TestMethod]
    public async Task BestMoveTest()
    {
        Engine engine = new();
        engine.Start(engineBinary);
        await engine.IsReady();

        ConcurrentBag<ParseResult> results = new();

        engine.DataReceived += (o, e) =>
        {
            if (e.Result is not null)
            {
                results.Add(e.Result);
            }
        };

        await engine.SetFenPosition("r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4");

        var bestMoveResult = await engine.GetBestMove();
        Assert.AreEqual("h5f7", bestMoveResult?.BestMove);
        Assert.IsTrue(bestMoveResult?.EngineScore?.IsMateScore);
        Assert.AreEqual(1, bestMoveResult?.EngineScore?.Score);

        engine.Dispose();
    }
}