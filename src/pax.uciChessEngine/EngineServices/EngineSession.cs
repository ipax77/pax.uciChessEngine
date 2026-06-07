namespace pax.uciChessEngine.EngineServices;

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
                await _engine.SetOption("MultiPV", _options.Pvs, ct);
            }
            if (_options.HashMb > 0)
            {
                await _engine.SetOption("Hash", _options.HashMb, ct);
            }
            if (!string.IsNullOrWhiteSpace(_options.WeightsPath))
            {
                await _engine.SetOption("WeightsFile", _options.WeightsPath, ct);
            }

            foreach (var option in ParseExtraOptions(_options.ExtraOptions))
            {
                await _engine.SetOption(option.Name, option.Value, ct);
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

    private static IEnumerable<(string Name, string Value)> ParseExtraOptions(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
            yield break;

        foreach (var rawLine in options.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
                separator = line.IndexOf(':', StringComparison.Ordinal);

            if (separator <= 0 || separator == line.Length - 1)
                continue;

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0)
                continue;

            yield return (name, value);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _engine.DisposeAsync();
        _lock.Dispose();
    }
}
