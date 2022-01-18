using Xunit;
using System.Linq;
using System.Threading.Tasks;

namespace pax.uciChessEngine.tests
{
    public class EngineTests
    {
        [Fact]
        public void Option()
        {
            Engine engine = new Engine("Stockfish", @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe");
            engine.Start();
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
            var options = engine.GetOptions().GetAwaiter().GetResult();

            var testOption = options.FirstOrDefault(f => f.Name == "Threads");
            Assert.NotNull(testOption);
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
            engine.SetOption("Threads", 2);
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
            engine.Dispose();
        }

        [Fact]
        public void Evaluation1()
        {
            Engine engine = new Engine("Stockfish", @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe");
            engine.Start();
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
            var options = engine.GetOptions().GetAwaiter().GetResult();

            var testOption = options.FirstOrDefault(f => f.Name == "Threads");
            Assert.NotNull(testOption);
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
            engine.SetOption("Threads", 2);
            Assert.True(engine.IsReady().GetAwaiter().GetResult());

            engine.Send("ucinewgame");
            engine.Send("go");
            Task.Delay(1000).GetAwaiter().GetResult();
            engine.Send("stop");
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
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
        public void Evaluation2()
        {
            Engine engine = new Engine("Stockfish", @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe");
            // Engine engine = new Engine("Houdini", @"C:\Program Files\Houdini 3 Chess\Houdini_3_Pro_x64.exe");
            engine.Start();
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
            var options = engine.GetOptions().GetAwaiter().GetResult();

            var testOption = options.FirstOrDefault(f => f.Name == "Threads");
            Assert.NotNull(testOption);
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
            engine.SetOption("Threads", 4);
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
            engine.SetOption("MultiPV", 4);
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
            engine.Send("ucinewgame");
            engine.Send("go");
            Task.Delay(2000).GetAwaiter().GetResult();
            engine.Send("stop");
            Assert.True(engine.IsReady().GetAwaiter().GetResult());
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