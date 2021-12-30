namespace pax.uciChessEngine;
public record Option
{
    public string Name { get; init; }
    public string Type { get; init; }
    public object Value { get; set; }
    public int Min { get; init; }
    public int Max { get; init; }
    public object Default { get; init; }
    public List<string>? Vars { get; init; }

    public Option(string name, string type, object value, List<string>? vars = null, int min = 0, int max = 0)
    {
        Name = name;
        Type = type;
        Value = value;
        Default = value;
        Vars = vars;
        Min = min;
        Max = max;
    }

    public Option(Option option)
    {
        Name = option.Name;
        Type = option.Type;
        Value = option.Value;
        Default = option.Default;
        Vars = option.Vars;
        Min = option.Min;
        Max = option.Max;
    }
}
