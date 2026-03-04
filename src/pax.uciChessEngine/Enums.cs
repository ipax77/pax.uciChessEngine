
namespace pax.uciChessEngine;

public enum EngineState
{
    Initializing,
    Started,
    Ready,
    Calculating,
    Evaluating,
    BestMove,
    Error,
}