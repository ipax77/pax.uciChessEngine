namespace pax.uciChessEngine;

public record Pv
{
    public int Multipv { get; init; }
    private Dictionary<string, int> Values { get; set; } = new Dictionary<string, int>();
    private List<string> Moves { get; set; } = new List<string>();
    private readonly object lockobject = new();

    public Pv(int multiPv)
    {
        Multipv = multiPv;
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
            return new List<string>(Moves);
        }
    }
}
