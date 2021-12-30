using Microsoft.Extensions.Logging;
using pax.chess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.uciChessEngine;

public partial class EngineService : IDisposable
{
    public ILogger<Engine> logger => StatusService.logger;

    public Guid Guid = Guid.NewGuid();
    public string Description { get; init; }
    public Engine WhiteEngine { get; init; }
    public Engine BlackEngine { get; init; }
    public Game Game { get; private set; }
    public event EventHandler<EngineMoveEventArgs>? EngineMoved;
    public List<int> WhiteEvaluations { get; private set; } = new List<int>();
    public List<int> BlackEvaluations { get; private set; } = new List<int>();
    public int CpuCoresUsed => WhiteEngine.Threads() + BlackEngine.Threads();

    protected virtual void OnEngineMoved(EngineMoveEventArgs e)
    {
        EngineMoved?.Invoke(this, e);
    }

    public EngineService(string whiteEngineName, string whiteEngineBinary, string blackEngineName, string blackEngineBinary, Game game, string desc)
    {
        Description = desc;
        WhiteEngine = new Engine(whiteEngineName, whiteEngineBinary);
        BlackEngine = new Engine(blackEngineName, blackEngineBinary);
        WhiteEngine.Start();
        BlackEngine.Start();
        Game = game;
        WhiteEngine.Status.MoveReady += WhiteMoveReady;
        BlackEngine.Status.MoveReady += BlackMoveReady;
    }

    public async Task Start(TimeSpan whitetime, TimeSpan whiteincrement, TimeSpan blacktime = new TimeSpan(), TimeSpan blackincrement = new TimeSpan())
    {
        await WhiteEngine.IsReady();
        await BlackEngine.IsReady();

        WhiteEngine.Send("ucinewgame");
        BlackEngine.Send("ucinewgame");
        await WhiteEngine.IsReady();
        WhiteEngine.Send($"position startpos moves {String.Join(" ", Game.State.Moves.Select(s => Map.GetEngineMoveString(s)))}");
        await BlackEngine.IsReady();
        BlackEngine.Send($"position startpos moves {String.Join(" ", Game.State.Moves.Select(s => Map.GetEngineMoveString(s)))}");
        await WhiteEngine.IsReady();
        await BlackEngine.IsReady();
        Game.Time = new Time(whitetime, whiteincrement, blacktime, blackincrement);

        await WhiteEngine.GetOptions();
        await BlackEngine.GetOptions();
        await WhiteEngine.IsReady();
        await BlackEngine.IsReady();
        WhiteEngine.SetOption("Threads", 2);
        BlackEngine.SetOption("Threads", 2);

        Go(Game.State.Info.BlackToMove ? BlackEngine : WhiteEngine);
    }

    private async void Go(Engine engine)
    {
        if (!IsGameOver())
        {
            engine.Send($"position startpos moves {String.Join(" ", Game.State.Moves.Select(s => Map.GetEngineMoveString(s)))}");
            await engine.IsReady();
            engine.Send($"go wtime {Convert.ToInt32(Game.Time.CurrentWhiteTime.TotalMilliseconds)} btime {Convert.ToInt32(Game.Time.CurrentBlackTime.TotalMilliseconds)} winc {Game.Time.WhiteIncrement.TotalMilliseconds} binc {Game.Time.BlackIncrement.TotalMilliseconds}");
        }
        else
        {
            OnEngineMoved(new EngineMoveEventArgs(WhiteEngine.Name, Map.GetEngineMove(Map.GetEngineMoveString(Game.State.Moves.Last())), Game.Time.LastMoveDuration, new EngineInfo(WhiteEngine.Name, new List<PvInfo>()), true));
            WhiteEngine.Stop();
            BlackEngine.Stop();
        }
    }

    private void WhiteMoveReady(object? sender, MoveEventArgs e)
    {
        if (e.Move != null)
        {
            var info = WhiteEngine.GetInfo();
            if (Game.Time.WhiteMoved())
            {
                Game.Move(e.Move);
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
        if (e.Move != null)
        {
            var info = BlackEngine.GetInfo();
            if (Game.Time.BlackMoved())
            {
                Game.Move(e.Move);
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

    public void Dispose()
    {
        WhiteEngine.Status.MoveReady -= WhiteMoveReady;
        BlackEngine.Status.MoveReady -= BlackMoveReady;
        WhiteEngine.Dispose();
        BlackEngine.Dispose();
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