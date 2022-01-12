using pax.chess;

namespace pax.uciChessEngine;
public record PvInfo
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
    public List<EngineMove> Moves { get; init; } = new List<EngineMove>();

    public PvInfo() { }
    public PvInfo(int pvNum, Dictionary<string, int> pvValues, List<string> pvMoves)
    {
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
        for (int i = 0; i < pvMoves.Count; i++)
        {
            Moves.Add(Map.GetValidEngineMove(pvMoves[i]));
        }
    }

}
