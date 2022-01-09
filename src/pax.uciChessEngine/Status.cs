using Microsoft.Extensions.Logging;
using pax.chess;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace pax.uciChessEngine;

public class Status
{
    public EngineState State { get; internal set; }
    public string EngineName { get; internal set; } = "unknown";
    internal List<Option> Options { get; private set; } = new List<Option>();
    public EngineMove? BestMove { get; internal set; }
    public EngineMove? Ponder { get; internal set; }
    public int Depth { get; internal set; }
    public EngineMove? CurrentMove { get; internal set; }
    public int CurrentMoveNumber { get; internal set; }
    public ConcurrentDictionary<int, Pv> Pvs { get; internal set; } = new ConcurrentDictionary<int, Pv>();
    public event EventHandler<StatusEventArgs>? StatusChanged;
    public event EventHandler<ErrorEventArgs>? ErrorRaised;
    public event EventHandler<MoveEventArgs>? MoveReady;

    internal virtual void OnStatusChanged(StatusEventArgs e)
    {
        StatusChanged?.Invoke(this, e);
    }

    internal virtual void OnErrorRaised(ErrorEventArgs e)
    {
        ErrorRaised?.Invoke(this, e);
        
    }
    internal virtual void OnMoveReady(MoveEventArgs e)
    {
        MoveReady?.Invoke(this, e);
    }

    internal Pv GetPv(int i)
    {
        Pv? pv;
        if (Pvs.TryGetValue(i, out pv))
        {
            return pv;
        } else
        {
            pv = new Pv(i);
            Pvs.AddOrUpdate(i, pv, (key, value) => pv);
            return pv;
        }
    }
}

public enum EngineState
{
    None,
    Ready,
    Calculating,
    Evaluating,
    BestMove,
    Error,
}

public class StatusEventArgs : EventArgs
{
    public EngineState State { get; set; }
}

public class ErrorEventArgs : EventArgs
{
    public string Error { get; init; }

    public ErrorEventArgs(string error)
    {
        Error = error;
    }
}

public class MoveEventArgs : EventArgs
{
    public EngineMove? Move { get; init; }
    public MoveEventArgs(EngineMove move)
    {
        Move = move;
    }
}

