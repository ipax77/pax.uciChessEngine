namespace pax.uciChessEngine;

public class MoveEventArgs : EventArgs
{
    public string? Move { get; init; }
}

public class StatusEventArgs : EventArgs
{
    public Status Status { get; init; } = new();
}

public class ErrorEventArgs : EventArgs
{
    public string Error { get; init; } = string.Empty;
}