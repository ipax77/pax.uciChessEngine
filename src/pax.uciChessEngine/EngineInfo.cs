using pax.chess;

namespace pax.uciChessEngine;
public record EngineInfo
{
    public string EngineName { get; init; }
    public EngineMove? BestMove { get; init; }
    public EngineMove? Ponder { get; init; }
    public int Evaluation { get; init; }
    public int Mate { get; init; }
    public int Depth { get; init; }
    public ICollection<PvInfo> PvInfos { get; init; }

    public EngineInfo(string engineName, ICollection<PvInfo> pvInfos)
    {
        EngineName = engineName;
        PvInfos = pvInfos;
        var pv1 = PvInfos.FirstOrDefault(f => f.MultiPv == 1);
        if (pv1 != null)
        {
            Evaluation = pv1.Score;
            Mate = pv1.Mate;
            if (pv1.Moves.Count > 1)
            {
                BestMove = pv1.Moves.ElementAt(0);
                Ponder = pv1.Moves.ElementAt(1);
            }
            else if (pv1.Moves.Count > 0)
            {
                BestMove = pv1.Moves.ElementAt(0);
            }
            Depth = pv1.Depth;
        }
    }
}
