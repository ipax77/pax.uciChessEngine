
using System.Collections.Concurrent;
using System.Globalization;
using pax.chess;
using pax.chess.Analyze;

namespace pax.uciChessEngine;

public sealed class Status
{
    public string Name { get; internal set; } = string.Empty;
    public string Author { get; internal set; } = string.Empty;
    public EngineState EngineState { get; internal set; }
    public IReadOnlyList<EngineOption> EngineOptions { get; internal set; } = [];
    public string? BestMove { get; internal set; }
    public string? Ponder { get; internal set; }
    public int Depth { get; internal set; }
    public string? CurrentMove { get; internal set; }
    public string? Error { get; internal set; }
    public IReadOnlyDictionary<int, Pv> Pvs => _pvs.AsReadOnly();
    private ConcurrentDictionary<int, Pv> _pvs = [];

    internal Pv GetPv(int i)
    {
        if (_pvs.TryGetValue(i, out Pv? pv))
        {
            return pv;
        }
        else
        {
            pv = new Pv(i);
            _pvs.AddOrUpdate(i, pv, (key, value) => pv);
            return pv;
        }
    }

    public Eval? GetEval(PieceColor sideToMove)
    {
        if (!_pvs.TryGetValue(1, out var pv))
            return null;

        var vals = pv.GetValues();

        int score = vals.GetValueOrDefault("cp", 0);
        int mate = vals.GetValueOrDefault("mate", 0);

        if (sideToMove == PieceColor.Black)
        {
            score = -score;
            mate = -mate;
        }

        return new Eval
        {
            Score = score,
            Mate = mate != 0 ? mate : null,
            PvInfo = new PvInfo(pv.MultiPv, vals, pv.GetMoves())
        };
    }
}

public sealed record EngineOption
{
    public string Name { get; private set; }
    public string Type { get; private set; }
    public object Value { get; set; }
    public int Min { get; private set; }
    public int Max { get; private set; }
    public object Default { get; private set; }
    public ICollection<string>? Vars { get; private set; }

    public EngineOption(string name, string type, object value, ICollection<string>? vars = null, int min = 0, int max = 0)
    {
        Name = name;
        Type = type;
        Value = value;
        Default = value;
        Vars = vars;
        Min = min;
        Max = max;
    }

    public EngineOption(EngineOption option)
    {
        Name = option?.Name ?? throw new ArgumentNullException(nameof(option));
        Type = option.Type;
        Value = option.Value;
        Default = option.Default;
        Vars = option.Vars;
        Min = option.Min;
        Max = option.Max;
    }

    public void Udpate(EngineOption option)
    {
        Name = option?.Name ?? throw new ArgumentNullException(nameof(option));
        Type = option.Type;
        Value = option.Value;
        Default = option.Default;
        Vars = option.Vars;
        Min = option.Min;
        Max = option.Max;
    }
}

public sealed record Pv
{
    public int MultiPv { get; init; }
    public int? Centipawns { get; private set; }
    public int? Mate { get; private set; }
    private Dictionary<string, int> Values { get; set; } = [];
    private List<string> Moves { get; set; } = [];
    private readonly Lock lockobject = new();

    public Pv(int multiPv)
    {
        MultiPv = multiPv;
    }

    internal void SetValues(Dictionary<string, int> values)
    {
        lock (lockobject)
        {
            Values = values;
        }
    }

    internal void SetMoves(List<string> moves)
    {
        lock (lockobject)
        {
            Moves = moves;
        }
    }

    internal Dictionary<string, int> GetValues()
    {
        lock (lockobject)
        {
            return new Dictionary<string, int>(Values);
        }
    }

    internal List<string> GetMoves()
    {
        lock (lockobject)
        {
            return [.. Moves];
        }
    }

    internal void SetScore(int? cp, int? mate)
    {
        lock (lockobject)
        {
            Centipawns = cp;
            Mate = mate;
        }
    }
}

public sealed record PvInfo : IPvInfo
{
    public int MultiPv { get; init; }
    public int Depth { get; init; }
    public int SelDepth { get; init; }
    public int Score { get; init; }
    public int Mate { get; init; }
    public int Nodes { get; init; }
    public int Nps { get; init; }
    public int HashFull { get; init; }
    public int TbHits { get; init; }
    public int Time { get; init; }
    public IReadOnlyList<string> Moves { get; init; } = [];

    public PvInfo() { }
    public PvInfo(int pvNum, Dictionary<string, int> pvValues, ICollection<string> pvMoves)
    {
        ArgumentNullException.ThrowIfNull(pvValues);
        ArgumentNullException.ThrowIfNull(pvMoves);
        MultiPv = pvNum;
        foreach (var ent in pvValues)
        {
            _ = ent.Key switch
            {
                "depth" => Depth = ent.Value,
                "seldepth" => SelDepth = ent.Value,
                "cp" => Score = ent.Value,
                "mate" => Mate = ent.Value,
                "nodes" => Nodes = ent.Value,
                "nps" => Nps = ent.Value,
                "hashfull" => HashFull = ent.Value,
                "tbhits" => TbHits = ent.Value,
                "time" => Time = ent.Value,
                _ => 0
            };
        }
        Moves = [.. pvMoves];
    }

}

public sealed record Eval
{
    public int Score { get; init; }     // centipawns
    public int? Mate { get; init; }     // mate distance
    public int Depth { get; init; }
    public PvInfo PvInfo { get; init; } = new();
    public int ChartScore
    {
        get
        {
            const int MateScore = 1000;

            if (Mate.HasValue)
                return Mate > 0 ? MateScore : -MateScore;

            return Score;
        }
    }

    public override string ToString()
    {
        if (Mate.HasValue)
        {
            return $"mate in {Mate.Value}";
        }
        return (Score / 100.0).ToString("N2", CultureInfo.InvariantCulture);
    }
}