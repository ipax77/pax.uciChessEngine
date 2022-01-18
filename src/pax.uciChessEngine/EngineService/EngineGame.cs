using Microsoft.Extensions.Logging;
using pax.chess;

namespace pax.uciChessEngine;

public sealed class EngineGame : IDisposable
{
    public static ILogger<Engine> Logger => StatusService.logger;

    public Guid EngineGameGuid { get; private set; } = Guid.NewGuid();
    public Engine? WhiteEngine { get; private set; }
    public Engine? BlackEngine { get; private set; }
    public Game Game { get; private set; }
    public event EventHandler<EngineMoveEventArgs>? EngineMoved;
    public ICollection<int> WhiteEvaluations { get; private set; } = new List<int>();
    public ICollection<int> BlackEvaluations { get; private set; } = new List<int>();
    public int CpuCoresUsed => (WhiteEngine == null ? 0 : WhiteEngine.Threads()) + (BlackEngine == null ? 0 : BlackEngine.Threads());
    public EngineGameOptions Options { get; private set; }
    public bool Playing { get; private set; }

    private void OnEngineMoved(EngineMoveEventArgs e)
    {
        EngineMoved?.Invoke(this, e);
    }

    public EngineGame(Game game, EngineGameOptions options)
    {
        Game = game;
        Options = options;
    }

    internal void SetOptions(EngineGameOptions options)
    {
        Game.Time = new Time(TimeSpan.FromSeconds(options.TimeInSeconds), TimeSpan.FromSeconds(options.IncrementInSeconds));
        Options = options;
    }

    public async Task Start()
    {
        Game.Time = new Time(TimeSpan.FromSeconds(Options.TimeInSeconds), TimeSpan.FromSeconds(Options.IncrementInSeconds));
        WhiteEngine = new Engine(Options.WhiteEngine.Key, Options.WhiteEngine.Value);
        BlackEngine = new Engine(Options.BlackEngine.Key, Options.BlackEngine.Value);
        await Task.Delay(1000).ConfigureAwait(false);
        WhiteEngine.Start();
        BlackEngine.Start();
        await Task.Delay(1000).ConfigureAwait(false);
        await WhiteEngine.IsReady().ConfigureAwait(false);
        await BlackEngine.IsReady().ConfigureAwait(false);

        WhiteEngine.Send("ucinewgame");
        BlackEngine.Send("ucinewgame");
        await WhiteEngine.IsReady(200).ConfigureAwait(false);
        WhiteEngine.Send($"position startpos moves {String.Join(" ", Game.State.Moves.Select(s => Map.GetEngineMoveString(s)))}");
        await BlackEngine.IsReady(200).ConfigureAwait(false);
        BlackEngine.Send($"position startpos moves {String.Join(" ", Game.State.Moves.Select(s => Map.GetEngineMoveString(s)))}");
        await WhiteEngine.IsReady().ConfigureAwait(false);
        await BlackEngine.IsReady().ConfigureAwait(false);
        // Game.Time = new Time(whitetime, whiteincrement, blacktime, blackincrement);

        await WhiteEngine.GetOptions().ConfigureAwait(false);
        await BlackEngine.GetOptions().ConfigureAwait(false);
        await WhiteEngine.IsReady().ConfigureAwait(false);
        await BlackEngine.IsReady().ConfigureAwait(false);
        await WhiteEngine.SetOption("Threads", Options.Threads / 2).ConfigureAwait(false);
        await BlackEngine.SetOption("Threads", Options.Threads / 2).ConfigureAwait(false);

        WhiteEngine.Status.MoveReady += WhiteMoveReady;
        BlackEngine.Status.MoveReady += BlackMoveReady;

        Playing = true;
        Go(Game.State.Info.BlackToMove ? BlackEngine : WhiteEngine);
    }

    private async void Go(Engine engine)
    {
        if (!IsGameOver())
        {
            engine.Send($"position startpos moves {String.Join(" ", Game.State.Moves.Select(s => Map.GetEngineMoveString(s)))}");
            await engine.IsReady().ConfigureAwait(false);
            engine.Send($"go wtime {Convert.ToInt32(Game.Time.CurrentWhiteTime.TotalMilliseconds)} btime {Convert.ToInt32(Game.Time.CurrentBlackTime.TotalMilliseconds)} winc {Game.Time.WhiteIncrement.TotalMilliseconds} binc {Game.Time.BlackIncrement.TotalMilliseconds}");
        }
        else if (WhiteEngine != null && BlackEngine != null)
        {
            OnEngineMoved(new EngineMoveEventArgs(WhiteEngine.Name, Map.GetEngineMove(Map.GetEngineMoveString(Game.State.Moves.Last())), Game.Time.LastMoveDuration, new EngineInfo(WhiteEngine.Name, new List<PvInfo>()), true));
            WhiteEngine.Stop();
            BlackEngine.Stop();
        }
    }

    private void WhiteMoveReady(object? sender, MoveEventArgs e)
    {
        if (e.Move != null && WhiteEngine != null && BlackEngine != null)
        {
            var info = WhiteEngine.GetInfo();
            if (Game.Time.WhiteMoved())
            {
                Game.Move(e.Move);
                Game.State.Moves.Last().MoveTime = Game.Time.LastMoveDuration;
                OnEngineMoved(new EngineMoveEventArgs(WhiteEngine.Name, e.Move, Game.Time.LastMoveDuration, info));
                WhiteEvaluations.Add(info.Evaluation);
                Go(BlackEngine);
            }
            else
            {
                Game.Result = Result.BlackWin;
                Game.Termination = Termination.Time;
                OnEngineMoved(new EngineMoveEventArgs(WhiteEngine.Name, e.Move, Game.Time.LastMoveDuration, info, true));
            }
        }
    }

    private void BlackMoveReady(object? sender, MoveEventArgs e)
    {
        if (e.Move != null && WhiteEngine != null && BlackEngine != null)
        {
            var info = BlackEngine.GetInfo();
            if (Game.Time.BlackMoved())
            {
                Game.Move(e.Move);
                Game.State.Moves.Last().MoveTime = Game.Time.LastMoveDuration;
                OnEngineMoved(new EngineMoveEventArgs(BlackEngine.Name, e.Move, Game.Time.LastMoveDuration, info));
                BlackEvaluations.Add(info.Evaluation);
                Go(WhiteEngine);
            }
            else
            {
                Game.Result = Result.WhiteWin;
                Game.Termination = Termination.Time;
                OnEngineMoved(new EngineMoveEventArgs(BlackEngine.Name, e.Move, Game.Time.LastMoveDuration, info, true));
            }
        }
    }

    private bool IsGameOver()
    {
        if (Game.Result != Result.None)
        {
            return true;
        }

        if (Game.State.Info.PawnHalfMoveClock >= 50)
        {
            Game.Result = Result.Draw;
            Game.Termination = Termination.NoPawnMoves;
            return true;
        }

        if (Game.State.Moves.Count > 120 &&
            Game.State.Info.PawnHalfMoveClock > 9
            && Math.Abs(WhiteEvaluations.TakeLast(7).Average()) <= 40
            && Math.Abs(BlackEvaluations.TakeLast(7).Average()) <= 40)
        {
            Game.Result = Result.Draw;
            Game.Termination = Termination.Agreed;
            return true;
        }

        //if (Game.State.Moves.Count > 60
        //    && Math.Abs(WhiteEvaluations.TakeLast(6).Average()) > 6000
        //    && Math.Abs(BlackEvaluations.TakeLast(6).Average()) > 6000
        //)
        //{
        //    if (WhiteEvaluations.Last() > 0)
        //    {
        //        Game.Result = Result.WhiteWin;
        //    }
        //    else
        //    {
        //        Game.Result = Result.BlackWin;
        //    }
        //    Game.Termination = Termination.Agreed;
        //    return true;
        //}

        return false;
    }

    public void Stop()
    {
        if (WhiteEngine != null && BlackEngine != null)
        {
            WhiteEngine.Status.MoveReady -= WhiteMoveReady;
            BlackEngine.Status.MoveReady -= BlackMoveReady;
            OnEngineMoved(new EngineMoveEventArgs(WhiteEngine.Name, Map.GetEngineMove(Map.GetEngineMoveString(Game.State.Moves.Last())), Game.Time.LastMoveDuration, new EngineInfo(WhiteEngine.Name, new List<PvInfo>()), true));
            WhiteEngine.Dispose();
            BlackEngine.Dispose();
        }
        Playing = false;
    }

    public void Dispose()
    {
        if (WhiteEngine != null && BlackEngine != null)
        {
            WhiteEngine.Status.MoveReady -= WhiteMoveReady;
            BlackEngine.Status.MoveReady -= BlackMoveReady;
            WhiteEngine.Dispose();
            BlackEngine.Dispose();
        }
    }
}

public class EngineMoveEventArgs : EventArgs
{
    public string EngineName { get; init; }
    public EngineMove? EngineMove { get; init; }
    public TimeSpan Movetime { get; init; }
    public EngineInfo EngineInfo { get; init; }
    public bool GameOver { get; init; }

    public EngineMoveEventArgs(string engineName, EngineMove? move, TimeSpan moveTime, EngineInfo info, bool gameover = false)
    {
        EngineName = engineName;
        EngineMove = move;
        Movetime = moveTime;
        EngineInfo = info;
        GameOver = gameover;
    }
}

public class EngineGameOptions
{
    public string Description { get; set; } = Guid.NewGuid().ToString();
    public string WhiteEngineName { get; set; } = String.Empty;
    public string BlackEngineName { get; set; } = String.Empty;
    public int TimeInSeconds { get; set; } = 180;
    public int IncrementInSeconds { get; set; } = 2;
    public int Threads { get; set; } = 4;
    public KeyValuePair<string, string> WhiteEngine { get; set; }
    public KeyValuePair<string, string> BlackEngine { get; set; }
    // public int MovesToPlay { get; set; } = 1;
}