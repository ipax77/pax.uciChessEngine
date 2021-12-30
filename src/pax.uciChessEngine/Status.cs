using Microsoft.Extensions.Logging;
using pax.chess;
using System.Text.RegularExpressions;

namespace pax.uciChessEngine;

public class Status
{
    private static Regex bestmoveRx = new Regex(@"^bestmove\s(.*)\sponder\s(.*)$");
    private static Regex valueRx = new Regex(@"\s(\w+)\s(\-?\d+)");
    private static Regex optionRx = new Regex(@"^option name ([^\s]+) type ([^\s]+) default ([^\s]+)");
    private static Regex optionmmRx = new Regex(@"min ([\d]+) max ([\d]+)$");

    public EngineState State { get; internal set; }
    internal int _lineCount = 0;

    public string EngineName { get; internal set; } = "unknown";
    internal List<Option> Options { get; private set; } = new List<Option>();
    public EngineMove? BestMove { get; internal set; }
    public EngineMove? Ponder { get; internal set; }
    public int Depth { get; internal set; }
    public EngineMove? CurrentMove { get; internal set; }
    public int CurrentMoveNumber { get; internal set; }
    public List<Pv> Pvs { get; internal set; } = new List<Pv>();
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
}

public enum EngineState
{
    None,
    Ready,
    Calculating,
    Evaluating,
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

