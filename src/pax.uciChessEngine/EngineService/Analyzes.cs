using pax.chess;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.uciChessEngine;
public sealed class Analyzes : IDisposable
{
    public List<Engine> Engines { get; private set; } = new List<Engine>();
    public Game Game { get; private set; } = new Game();
    private string? Fen;
    public TimeSpan Refreshtime { get; set; }
    private CancellationTokenSource? TokenSource;
    public event EventHandler<List<EngineInfo>>? EngineInfoAvailable;
    private object lockobject = new object();
    private bool InfoUpdateRunning = false;
    public int CpuCoresUsed => Engines.Sum(s => s.Threads());
    public bool isPaused { get; private set; } = false;

    private void OnEngineInfoAvailable(List<EngineInfo> infos)
    {
        EngineInfoAvailable?.Invoke(this, infos);
    }

    public Analyzes(Game game, string? fen = null)
    {
        Game = game;
        Fen = fen;
        Refreshtime = TimeSpan.FromMilliseconds(250);
    }

    public async Task AddEngine(Engine engine)
    {
        await InitEngine(engine);
        Engines.Add(engine);
    }

    public void RemoveEngine(Engine engine)
    {
        Engines.Remove(engine);
        engine.Dispose();
    }

    private async Task InitEngine(Engine engine)
    {
        engine.Start();
        await engine.IsReady();
        await engine.GetOptions();
        await SetEngineOption(engine, "Threads", 2);
        await SetEngineOption(engine, "MultiPV", 2);
    }

    public async Task SetEngineOption(Engine engine, string name, object value)
    {
        engine.SetOption(name, value);
        await engine.IsReady();
    }

    public async Task ChangePvLine(Engine engine, bool upOrDown)
    {
        var pvOption = engine.Status.Options.FirstOrDefault(f => f.Name == "MultiPV");
        if (pvOption != null)
        {
            int PvLines = (int)pvOption.Value;
            if (upOrDown)
            {
                PvLines++;
            }
            else
            {
                PvLines = Math.Max(1, PvLines - 1);
            }
            if (PvLines != (int)pvOption.Value)
            {
                engine.Send("stop");
                await engine.IsReady();
                engine.Status.Pvs.Clear();
                await SetEngineOption(engine, "Threads", PvLines);
                await SetEngineOption(engine, "MultiPV", PvLines);
                engine.Send("go");
            }
        }
    }

    public void Pause()
    {
        TokenSource?.Cancel();
        Engines.ForEach(f => f.Send("stop"));
        isPaused = true;
    }

    public void Resume()
    {
        isPaused = false;
        _ = UpdateEngineGame();
    }

    public async Task UpdateEngineGame()
    {
        if (isPaused)
        {
            return;
        }

        var moves = Game.ObserverState.Moves.Select(s => s.EngineMove.ToString());
        foreach (var engine in Engines)
        {
            engine.Send("stop");
            await engine.IsReady();
            engine.Send($"position {(Fen == null ? "startpos" : $"fen {Fen}")} moves {String.Join(" ", moves)}");
            await engine.IsReady();
            engine.Send("go");
        }
        _ = UpdateEngineEval();
    }

    private async Task UpdateEngineEval()
    {
        lock (lockobject)
        {
            if (InfoUpdateRunning)
            {
                return;
            }
            else
            {
                TokenSource = new CancellationTokenSource();
                InfoUpdateRunning = true;
            }
        }

        ParallelOptions po = new ParallelOptions()
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = TokenSource.Token
        };

        try
        {
            while (!TokenSource.Token.IsCancellationRequested)
            {
                ConcurrentBag<EngineInfo> infos = new ConcurrentBag<EngineInfo>();
                await Parallel.ForEachAsync(Engines, po, async (engine, token) =>
                {
                    await Task.Run(() =>
                    {
                        var engineInfo = engine.GetInfo();
                        if (engineInfo != null)
                        {
                            infos.Add(engineInfo);
                        }
                    }, token);
                });
                if (infos.Any())
                {
                    OnEngineInfoAvailable(infos.ToList());
                }
                await Task.Delay(Refreshtime, TokenSource.Token);
            }
        } catch (OperationCanceledException) { }
        finally
        {
            InfoUpdateRunning = false;
            TokenSource.Dispose();
            TokenSource = null;
        }
    }

    public void Dispose()
    {
        TokenSource?.Cancel();
        Engines.ForEach(f => f.Dispose());
        Engines.Clear();

    }
}
