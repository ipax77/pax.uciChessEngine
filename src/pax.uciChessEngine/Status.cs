
using System.Collections.Concurrent;

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
}