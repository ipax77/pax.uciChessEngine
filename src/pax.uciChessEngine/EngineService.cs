using pax.chess;
using System.ComponentModel.DataAnnotations;

namespace pax.uciChessEngine;

public sealed class EngineSessionProvider : IAsyncDisposable
{
    private readonly List<EngineSession> _sessions = [];
    private readonly SemaphoreSlim _poolLock = new(1, 1);

    private readonly EngineRunOptions _options;
    private readonly TimeSpan _idleTimeout;

    public EngineSessionProvider(
        EngineRunOptions runOptions,
        TimeSpan idleTimeout)
    {
        _options = runOptions;
        _idleTimeout = idleTimeout;
    }

    public async Task<EngineLease> AcquireAsync(CancellationToken ct)
    {
        await _poolLock.WaitAsync(ct);

        try
        {
            // find idle
            var session = _sessions.FirstOrDefault(s => !s.IsBusy);

            if (session != null)
                return new EngineLease(session, this);

            // create new
            if (_sessions.Count < _options.PoolSize)
            {
                session = new EngineSession(_options);
                _sessions.Add(session);
                return new EngineLease(session, this);
            }
        }
        finally
        {
            _poolLock.Release();
        }

        // wait for free session
        while (true)
        {
            await Task.Delay(20, ct);

            await _poolLock.WaitAsync(ct);
            try
            {
                var session = _sessions.FirstOrDefault(s => !s.IsBusy);
                if (session != null)
                    return new EngineLease(session, this);
            }
            finally
            {
                _poolLock.Release();
            }
        }
    }

    internal static void Release(EngineSession session)
    {
        session.LastUsedUtc = DateTime.UtcNow;
    }

    public async Task CleanupIdleAsync()
    {
        await _poolLock.WaitAsync();

        try
        {
            var now = DateTime.UtcNow;

            var remove = _sessions
                .Where(s => !s.IsBusy && now - s.LastUsedUtc > _idleTimeout)
                .ToList();

            foreach (var session in remove)
            {
                _sessions.Remove(session);
                await session.DisposeAsync();
            }
        }
        finally
        {
            _poolLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _sessions)
            await s.DisposeAsync();

        _poolLock.Dispose();
    }
}

public sealed class EngineLease : IAsyncDisposable
{
    private readonly EngineSessionProvider _provider;

    public EngineSession Session { get; }

    internal EngineLease(EngineSession session, EngineSessionProvider provider)
    {
        Session = session;
        _provider = provider;
    }

    public ValueTask DisposeAsync()
    {
        EngineSessionProvider.Release(Session);
        return ValueTask.CompletedTask;
    }
}

public sealed class EngineSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly UciEngine _engine;
    private readonly EngineRunOptions _options;
    public DateTime LastUsedUtc { get; internal set; }

    public EngineSession(EngineRunOptions engineRunOptions)
    {
        _options = engineRunOptions;
        _engine = new UciEngine(_options.BinaryPath);
    }

    public async Task EnsureStartedAsync(CancellationToken ct)
    {
        if (!_engine.IsRunning)
        {
            await _engine.StartAsync(ct);
            if (_options.Threads > 0)
            {
                await _engine.SetOption("Threads", _options.Threads, ct);
            }
            if (_options.Pvs > 0)
            {
                await _engine.SetOption("MultiPv", _options.Pvs, ct);
            }
            if (_options.HashMb > 0)
            {
                await _engine.SetOption("Hash", _options.HashMb, ct);
            }
        }
    }

    public async Task<T> UseAsync<T>(Func<UciEngine, Task<T>> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _lock.WaitAsync(ct);

        try
        {
            await EnsureStartedAsync(ct);
            LastUsedUtc = DateTime.UtcNow;
            return await action(_engine);
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool IsBusy => _lock.CurrentCount == 0;

    public async ValueTask DisposeAsync()
    {
        await _engine.DisposeAsync();
        _lock.Dispose();
    }
}

public static class EngineService
{
    public static async Task<string> GetEngineName(EngineRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await using var engine = new UciEngine(options.BinaryPath);
        return engine.Status.Name;
    }

    public static async Task<List<Eval>> GetEvaluation(string fen,
                                                       PieceColor sideToMove,
                                                       TimeSpan thinkTime,
                                                       UciEngine engine,
                                                       CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        try
        {
            await engine.SendAsync($"position fen {fen}", token);

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? _, MoveEventArgs __)
                => tcs.TrySetResult();

            engine.MoveReady += Handler;

            try
            {
                await engine.SendAsync($"go movetime {(int)thinkTime.TotalMilliseconds}", token);
                await tcs.Task.WaitAsync(token);
            }
            finally
            {
                engine.MoveReady -= Handler;
            }

            var status = engine.Status;

            List<Eval> evals = [];
            foreach (var pv in status.Pvs.Values.OrderBy(o => o.MultiPv))
            {
                var vals = pv.GetValues();
                int score = vals.GetValueOrDefault("cp", 0);
                int mate = vals.GetValueOrDefault("mate", 0);

                if (sideToMove == PieceColor.Black)
                {
                    score = -score;
                    mate = -mate;
                }

                evals.Add(new Eval()
                {
                    Score = score,
                    Mate = mate,
                    PvInfo = new PvInfo(pv.MultiPv, vals, pv.GetMoves())
                });
            }
            return evals;
        }
        catch (OperationCanceledException)
        {
            await engine.SendAsync("stop", CancellationToken.None);
            throw;
        }
    }
}


public sealed class EngineRunOptions
{
    public Guid Id { get; } = Guid.NewGuid();
    [Required]
    public string BinaryPath { get; set; } = string.Empty;
    public string? Name { get; set; }
    [Range(0, 256)]
    public int Threads { get; set; }
    [Range(0, 50)]
    public int Pvs { get; set; }
    [Range(0, 65536)]
    public int HashMb { get; set; }
    [Range(1, 32)]
    public int PoolSize { get; set; } = 2;
}
