namespace pax.uciChessEngine;

public record Pv
{
    public int multipv { get; init; }
    private Dictionary<string, int> Values { get; set; } = new Dictionary<string, int>();
    private List<string> Moves { get; set; } = new List<string>();
    private object lockobject = new object();

    public Pv(int multiPv)
    {
        multipv = multiPv;
    }

    public void SetValues(Dictionary<string, int> values)
    {
        lock (lockobject)
        {
            Values = values;
        }
    }

    public void SetMoves(List<string> moves)
    {
        lock (lockobject)
        {
            Moves = moves;
        }
    }

    public Dictionary<string, int> GetValues()
    {
        lock (lockobject)
        {
            return new Dictionary<string, int>(Values);
        }
    }

    public List<string> GetMoves()
    {
        lock (lockobject)
        {
            return new List<string>(Moves);
        }
    }
}
