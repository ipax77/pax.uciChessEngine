namespace pax.uciChessEngine.tests;

[TestClass]
public class EngineVsEngineTests
{
    private readonly string engineBinary1 = @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe";
    private readonly string engineBinary2 = @"C:\data\lc0-v0.28.0-windows-gpu-nvidia-cuda-nodll\lc0.exe";

    [TestMethod]
    public async Task BasicEvETest()
    {
        EngineVsEngine eve = new(engineBinary1);

        List<MoveReadyEventArgs> moves = [];

        eve.MoveReady += (o, e) =>
        {
            moves.Add(e);
        };

        eve.Start();

        await Task.Delay(30000);

        Assert.IsTrue(moves.Count > 0);

        eve.Dispose();
    }

    [TestMethod]
    public async Task BasicEvETest2()
    {
        EngineVsEngine eve = new(engineBinary1, engineBinary2);

        List<MoveReadyEventArgs> moves = [];

        eve.MoveReady += (o, e) =>
        {
            moves.Add(e);
        };

        eve.Start();

        await Task.Delay(30000);

        Assert.IsTrue(moves.Count > 0);

        eve.Dispose();
    }
}
