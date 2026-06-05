using BenchmarkDotNet.Attributes;
using pax.chess;

namespace pax.uciChessEngine.benchmark;

[MemoryDiagnoser]
public class UciParserBenchmarks
{
    private const string BinaryPath = "benchmark-only";
    private readonly UciEngine _engine = new(BinaryPath);
    private readonly UciEngine _evalEngine = new(BinaryPath);

    private readonly string _centipawnInfo =
        "info depth 24 seldepth 49 multipv 1 score cp 35 nodes 2055087 nps 546420 hashfull 663 tbhits 0 time 3761 pv e2e4 e7e5 g1f3 b8c6 f1b5 g8f6 e1g1 f6e4 f1e1 e4d6 f3e5 f8e7 b5f1 c6e5 e1e5 e8g8 d2d4 e7f6 e5e1 d6f5 c2c3 d7d5 c1f4 a7a5 b1d2 c7c6 d2f3 g7g6 f1d3 f5g7 d1c2";

    private readonly string _mateInfo =
        "info depth 245 seldepth 2 multipv 1 score mate 1 nodes 10540 nps 103333 hashfull 0 tbhits 0 time 102 pv h5f7";

    private readonly string[] _searchLines =
    [
        "info depth 10 seldepth 15 multipv 1 score cp 21 nodes 104050 nps 415000 hashfull 12 tbhits 0 time 251 pv e2e4 e7e5 g1f3",
        "info depth 11 seldepth 17 multipv 1 score cp 24 nodes 164050 nps 456000 hashfull 24 tbhits 0 time 360 pv e2e4 e7e5 g1f3 b8c6",
        "info depth 12 seldepth 19 multipv 1 score cp 28 nodes 254050 nps 501000 hashfull 37 tbhits 0 time 507 pv e2e4 e7e5 g1f3 b8c6 f1b5",
        "info depth 13 seldepth 21 multipv 2 score cp 11 nodes 354050 nps 535000 hashfull 51 tbhits 0 time 662 pv d2d4 d7d5 c2c4",
        "info depth 14 seldepth 24 multipv 1 score cp 35 nodes 505087 nps 546420 hashfull 66 tbhits 0 time 925 pv e2e4 e7e5 g1f3 b8c6 f1b5 g8f6"
    ];

    private readonly string[] _optionLines =
    [
        "option name Threads type spin default 1 min 1 max 1024",
        "option name Hash type spin default 16 min 1 max 33554432",
        "option name Use NNUE type check default true",
        "option name EvalFile type string default <empty>",
        "option name Skill Level type combo default Normal var Easy var Normal var Hard"
    ];

    [GlobalSetup]
    public void Setup()
        => _evalEngine.TestParseUciString(_centipawnInfo);

    [Benchmark(Baseline = true)]
    public void ParseCentipawnInfo()
        => _engine.TestParseUciString(_centipawnInfo);

    [Benchmark]
    public void ParseMateInfo()
        => _engine.TestParseUciString(_mateInfo);

    [Benchmark]
    public void ParseRepeatedSearchInfo()
    {
        foreach (var line in _searchLines)
            _engine.TestParseUciString(line);
    }

    [Benchmark]
    public void ParseBestMove()
        => _engine.TestParseUciString("bestmove e2e4 ponder e7e5");

    [Benchmark]
    public void ParseStartupOptions()
    {
        foreach (var line in _optionLines)
            _engine.TestParseUciString(line);
    }

    [Benchmark]
    public Eval? GetEval()
        => _evalEngine.Status.GetEval(PieceColor.White);

    [Benchmark]
    public Eval? ParseAndGetEval()
    {
        _engine.TestParseUciString(_centipawnInfo);
        return _engine.Status.GetEval(PieceColor.White);
    }
}
