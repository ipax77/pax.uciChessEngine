using Xunit;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace pax.uciChessEngine.tests
{
    public class EngineTests
    {
        private readonly string enginePath = @"C:\data\chess\engines\stockfish\stockfish-windows-x86-64.exe";

        [Fact]
        public async Task Option()
        {
            Engine engine = new("Stockfish", enginePath);
            await engine.Start();
            Assert.True(await engine.IsReady());
            var options = await engine.GetOptions();

            var testOption = options.FirstOrDefault(f => f.Name == "Threads");
            Assert.NotNull(testOption);
            Assert.True(await engine.IsReady());
            await engine.SetOption("Threads", 2);
            Assert.True(await engine.IsReady());
            engine.Dispose();
        }

        [Fact]
        public async Task Evaluation1()
        {
            Engine engine = new("Stockfish", enginePath);
            await engine.Start();
            Assert.True(await engine.IsReady());
            var options = await engine.GetOptions();

            var testOption = options.FirstOrDefault(f => f.Name == "Threads");
            Assert.NotNull(testOption);
            Assert.True(await engine.IsReady());
            await engine.SetOption("Threads", 2);
            Assert.True(await engine.IsReady());

            await engine.Send("ucinewgame");
            await engine.Send("go");
            await Task.Delay(1000);
            await engine.Send("stop");
            Assert.True(await engine.IsReady());
            var info = engine.GetInfo();
            Assert.NotNull(info);
            var pv = info.PvInfos.FirstOrDefault();
            Assert.NotNull(pv);
            if (pv != null)
            {
                Assert.True(pv.Score > 0);
            }

            engine.Dispose();
        }

        [Fact]
        public async Task Evaluation2()
        {
            Engine engine = new("Stockfish", enginePath);
            // Engine engine = new Engine("Houdini", @"C:\Program Files\Houdini 3 Chess\Houdini_3_Pro_x64.exe");
            await engine.Start();
            Assert.True(await engine.IsReady());
            var options = await engine.GetOptions();

            var testOption = options.FirstOrDefault(f => f.Name == "Threads");
            Assert.NotNull(testOption);
            Assert.True(await engine.IsReady());
            await engine.SetOption("Threads", 4);
            Assert.True(await engine.IsReady());
            await engine.SetOption("MultiPV", 4);
            Assert.True(await engine.IsReady());
            await engine.Send("ucinewgame");
            await engine.Send("go");
            await Task.Delay(2000);
            await engine.Send("stop");
            Assert.True(await engine.IsReady());
            var info = engine.GetInfo();
            Assert.NotNull(info);
            Assert.True(info.PvInfos.Count == 4);

            for (int i = 0; i < info.PvInfos.Count; i++)
            {
                var pv = info.PvInfos.ElementAt(i);
                Assert.True(pv.Score > 0);
            }
            engine.Dispose();
        }

        [Fact]
        public async Task GetStopInfoTest()
        {
            Engine engine = new("Stockfish", @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe");
            // Engine engine = new Engine("Houdini", @"C:\Program Files\Houdini 3 Chess\Houdini_3_Pro_x64.exe");
            await engine.Start();
            await engine.IsReady();
            await engine.Send("ucinewgame");
            await engine.Send("go");

            ManualResetEvent mre = new(false);
            engine.Status.MoveReady += (o, e) =>
            {
                mre.Set();
            };

            await Task.Delay(1000);
            EngineInfo? info = await engine.GetStopInfo();

            Assert.NotNull(info);
            Assert.NotNull(info?.BestMove);

            var waitResult = mre.WaitOne(3000);
            Assert.True(waitResult);

            engine.Dispose();
        }
    }
}