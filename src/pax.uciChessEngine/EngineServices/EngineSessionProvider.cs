namespace pax.uciChessEngine.EngineServices;

public sealed class EngineSessionProvider : IAsyncDisposable, IEngineSessionProvider
{
    private readonly List<EngineSession> _sessions = [];
    private readonly SemaphoreSlim _poolLock = new(1, 1);

    private readonly EngineRunOptions _options;
    private readonly TimeSpan _idleTimeout;

    public Guid Id() => _options.Id;

    public EngineSessionProvider(EngineRunOptions runOptions)
    {
        ArgumentNullException.ThrowIfNull(runOptions);
        _options = runOptions;
        _idleTimeout = TimeSpan.FromMilliseconds(runOptions.IdelTimeoutMs);
    }

    public async Task<EngineLease> AcquireAsync(CancellationToken ct)
    {
        await _poolLock.WaitAsync(ct);

        try
        {
            var session = _sessions.FirstOrDefault(s => !s.IsBusy);

            if (session != null)
                return new EngineLease(session, this);

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
