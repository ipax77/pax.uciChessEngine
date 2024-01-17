namespace pax.uciChessEngine;

public class EngineVsEngine : IDisposable
{
    private readonly Engine whiteEngine;
    private readonly Engine blackEngine;
    private readonly List<string> moves = [];

    public int WhiteTimeMilliseconds { get; private set; } = 180_000;
    public int BlackTimeMilliseconds { get; private set; } = 180_000;
    public int WhiteIncrement { get; private set; } = 2000;
    public int BlackIncrement { get; private set; } = 2000;
    public bool BlackToMove { get; private set; }
    public DateTime StartTime { get; private set; }
    public EngineScore WhiteScore { get; private set; } = new();
    public EngineScore BlackScore { get; private set; } = new();
    

    public int whiteTime;
    public int blackTime;

    public EventHandler<MoveReadyEventArgs>? MoveReady { get; set; }

    protected virtual void OnMoveReady(MoveReadyEventArgs e)
    {
        MoveReady?.Invoke(this, e);
    }

    public EngineVsEngine(string whiteBinary, string? blackBinary = null)
    {
        if (string.IsNullOrEmpty(blackBinary)
            || string.Equals(whiteBinary, blackBinary, StringComparison.Ordinal))
        {
            whiteEngine = blackEngine = new();
            whiteEngine.Start(whiteBinary);
            whiteEngine.BestMoveReady += HandleBestMove;
            whiteEngine.PvInfoReady += HandleInfo;
        }
        else
        {
            whiteEngine = new();
            blackEngine = new();
            whiteEngine.Start(whiteBinary);
            blackEngine.Start(blackBinary);
            whiteEngine.BestMoveReady += HandleBestMove;
            blackEngine.BestMoveReady += HandleBestMove;
            whiteEngine.PvInfoReady += HandleInfo;
            blackEngine.PvInfoReady += HandleInfo;
        }
    }

    public async void Start()
    {
        StartTime = DateTime.UtcNow;
        if (whiteEngine == blackEngine)
        {
            await whiteEngine.IsReady();
            await whiteEngine.Send("ucinewgame");
        }
        else
        {
            await whiteEngine.IsReady();
            await whiteEngine.Send("ucinewgame");
            await blackEngine.IsReady();
            await blackEngine.Send("ucinewgame");
        }
        _ = whiteEngine.Send($"go wtime {WhiteTimeMilliseconds} btime {BlackTimeMilliseconds} winc {WhiteIncrement} binc {BlackIncrement}");
    }

    public void Stop()
    {

    }

    private async void HandleBestMove(object? sender, BestMoveEventArgs e)
    {
        if (e.Result is null || string.IsNullOrEmpty(e.Result.BestMove))
        {
            return;
        }

        moves.Add(e.Result.BestMove);

        if (BlackToMove)
        {
            BlackTimeMilliseconds -= blackTime + BlackIncrement;
            blackTime = 0;
            await whiteEngine.Send($"position startpos moves {string.Join(' ', moves)}");
            await whiteEngine.Send($"go wtime {WhiteTimeMilliseconds} btime {BlackTimeMilliseconds} winc {WhiteIncrement} binc {BlackIncrement}");
        }
        else
        {
            WhiteTimeMilliseconds -= whiteTime + WhiteIncrement;
            whiteTime = 0;
            await blackEngine.Send($"position startpos moves {string.Join(' ', moves)}");
            await blackEngine.Send($"go wtime {WhiteTimeMilliseconds} btime {BlackTimeMilliseconds} winc {WhiteIncrement} binc {BlackIncrement}");
        }
        OnMoveReady(new() 
            {
                BlackToMove = BlackToMove,
                Move = e.Result.BestMove,
                Score = BlackToMove ? BlackScore : WhiteScore
            }
        );
        BlackToMove = !BlackToMove;
    }

    private void HandleInfo(object? sender, PvInfoEventArgs e)
    {
        if (e.Result is null)
        {
            return;
        }

        if (BlackToMove)
        {
            blackTime = e.Result.Time;
            BlackScore = new(e.Result);
        }
        else
        {
            whiteTime = e.Result.Time;
            WhiteScore = new(e.Result);
        }
    }

    public void Dispose()
    {
        whiteEngine.BestMoveReady -= HandleBestMove;
        blackEngine.BestMoveReady -= HandleBestMove;
        whiteEngine.PvInfoReady -= HandleInfo;
        blackEngine.PvInfoReady -= HandleInfo;

        if (blackEngine != whiteEngine)
        {
            whiteEngine.Dispose();
            blackEngine.Dispose();
        }
        else
        {
            whiteEngine.Dispose();
        }
    }
}

public class MoveReadyEventArgs : EventArgs
{
    public bool BlackToMove { get; set; }
    public string Move { get; init; } = string.Empty;
    public EngineScore Score { get; set; } = new();
}

public enum Result
{
    None = 1,
    Draw = 2,
    WhiteWin = 3,
    BlackWin = 4
}