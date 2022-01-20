using Xunit;
using System.Linq;
using System.Threading.Tasks;

namespace pax.uciChessEngine.tests
{
    public class EngineTests
    {
        [Fact]
        public async Task Option()
        {
            Engine engine = new("Stockfish", @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe");
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
            Engine engine = new("Stockfish", @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe");
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
            Engine engine = new("Stockfish", @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe");
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
    }
}