using pax.chess;
using System.Collections.Concurrent;

namespace pax.uciChessEngine;
public sealed class Analyzes : IDisposable
{
    public ICollection<Engine> Engines { get; private set; } = new List<Engine>();
    public Game Game { get; private set; } = new Game();
    private readonly string? Fen;
    public TimeSpan Refreshtime { get; set; }
    private CancellationTokenSource? TokenSource;
    public event EventHandler<EngineInfoEventArgs>? EngineInfoAvailable;
    private readonly object lockobject = new();
    private bool InfoUpdateRunning;
    public int CpuCoresUsed => Engines.Sum(s => s.Threads());
    public bool IsPaused { get; private set; }

    private void OnEngineInfoAvailable(EngineInfoEventArgs infosEventArgs)
    {
        EngineInfoAvailable?.Invoke(this, infosEventArgs);
    }

    public Analyzes(Game game, string? fen = null)
    {
        Game = game;
        Fen = fen;
        Refreshtime = TimeSpan.FromMilliseconds(250);
    }

    public async Task AddEngine(Engine engine)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }
        Engines.Add(engine);
        await InitEngine(engine).ConfigureAwait(false);
    }

    public void RemoveEngine(Engine engine)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }
        Engines.Remove(engine);
        engine.Dispose();
    }

    private async Task InitEngine(Engine engine)
    {
        if (Engines.Contains(engine))
        {
            await engine.Start().ConfigureAwait(false);
            await engine.IsReady().ConfigureAwait(false);
            await engine.GetOptions().ConfigureAwait(false);
            await engine.SetOption("Threads", 2).ConfigureAwait(false);
            await engine.IsReady().ConfigureAwait(false);
            await engine.SetOption("MultiPV", 2).ConfigureAwait(false);
            await engine.IsReady().ConfigureAwait(false);
        }
    }

    public async Task ChangePvLine(Engine engine, bool upOrDown)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }
        if (Engines.Contains(engine))
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
                    await engine.Send("stop").ConfigureAwait(false);
                    await engine.IsReady().ConfigureAwait(false);
                    engine.Status.Pvs.Clear();
                    await engine.SetOption("Threads", PvLines).ConfigureAwait(false);
                    await engine.SetOption("MultiPV", PvLines).ConfigureAwait(false);
                    await engine.Send("go").ConfigureAwait(false);
                }
            }
        }
    }

    public void Pause()
    {
        TokenSource?.Cancel();
        Engines.ToList().ForEach(f => _ = f.Send("stop"));
        IsPaused = true;
    }

    public void Resume()
    {
        IsPaused = false;
        _ = UpdateEngineGame();
    }

    public async Task UpdateEngineGame()
    {
        if (IsPaused)
        {
            return;
        }

        var moves = Game.ObserverState.Moves.Select(s => s.EngineMove.ToString());
        foreach (var engine in Engines)
        {
            await engine.Send("stop").ConfigureAwait(false);
            await engine.IsReady().ConfigureAwait(false);
            await engine.Send($"position {(Fen == null ? "startpos" : $"fen {Fen}")} moves {String.Join(" ", moves)}").ConfigureAwait(false);
            await engine.IsReady().ConfigureAwait(false);
            await engine.Send("go").ConfigureAwait(false);
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
                ConcurrentBag<EngineInfo> infos = new();
                await Parallel.ForEachAsync(Engines, po, async (engine, token) =>
                {
                    await Task.Run(() =>
                    {
                        var engineInfo = engine.GetInfo();
                        if (engineInfo != null)
                        {
                            infos.Add(engineInfo);
                        }
                    }, token).ConfigureAwait(false);
                }).ConfigureAwait(false);
                if (infos.Any())
                {
                    OnEngineInfoAvailable(new EngineInfoEventArgs(infos.ToList()));
                }
                await Task.Delay(Refreshtime, TokenSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
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
        Engines.ToList().ForEach(f => f.Dispose());
        Engines.Clear();
        TokenSource?.Dispose();
    }
}

public class EngineInfoEventArgs : EventArgs
{
    public IReadOnlyCollection<EngineInfo> Infos { get; init; }

    public EngineInfoEventArgs(IReadOnlyCollection<EngineInfo> infos)
    {
        Infos = infos;
    }
}
