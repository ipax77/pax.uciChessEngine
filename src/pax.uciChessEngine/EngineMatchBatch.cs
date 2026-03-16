using pax.chess;
using pax.uciChessEngine.EngineServices;

namespace pax.uciChessEngine;

public enum EngineMatchStatus
{
    Queued,
    Running,
    Finished,
    Cancelled,
    Error
}

public sealed class EngineMatchSummaryEventArgs : EventArgs
{
    public Guid Id { get; } = Guid.NewGuid();
    public int Index { get; init; }
    public string WhiteEngine { get; init; } = string.Empty;
    public string BlackEngine { get; init; } = string.Empty;
    public bool ReverseEngines { get; init; }
    public GameResult? Result { get; internal set; }
    public EngineMatchStatus Status { get; internal set; } = EngineMatchStatus.Queued;
    public string? ErrorMessage { get; internal set; }
    public ChessGame? Game { get; internal set; }
}

public sealed record EngineMatchBatchRequest(
    BoardPosition StartPosition,
    EngineRunOptions WhiteEngine,
    EngineRunOptions BlackEngine,
    int MatchCount,
    TimeSpan BaseTime,
    TimeSpan Increment,
    bool ReverseEngines);

public sealed class EngineMatchBatch : IAsyncDisposable
{
    private readonly List<EngineMatchSummaryEventArgs> _matches = new();
    private readonly Dictionary<Guid, EngineMatchRuntime> _activeRuntimes = new();
    private readonly object _lock = new();
#pragma warning disable CA2213 // Disposable fields should be disposed

    private CancellationTokenSource? _cts;
#pragma warning restore CA2213 // Disposable fields should be disposed

    private bool _isRunning;

    public event EventHandler<EngineMatchSummaryEventArgs>? MatchUpdated;
    public event EventHandler? BatchFinished;

    public IReadOnlyList<EngineMatchSummaryEventArgs> Matches => _matches;
    public bool IsRunning => _isRunning;
    public int MaxConcurrent { get; private set; } = 1;

    public static int ComputeMaxConcurrent(EngineRunOptions white, EngineRunOptions black)
    {
        ArgumentNullException.ThrowIfNull(white);
        ArgumentNullException.ThrowIfNull(black);
        var poolSize = Math.Min(white.PoolSize, black.PoolSize);
        return Math.Max(1, poolSize / 2);
    }

    public async Task RunAsync(EngineMatchBatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_isRunning)
            throw new InvalidOperationException("Match batch already running.");

        _matches.Clear();
        MaxConcurrent = ComputeMaxConcurrent(request.WhiteEngine, request.BlackEngine);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _isRunning = true;

        var count = Math.Max(1, request.MatchCount);
        var semaphore = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);
        var tasks = new List<Task>(count);

        for (var i = 0; i < count; i++)
        {
            var index = i + 1;
            var reverse = request.ReverseEngines && (i % 2 == 1);
            var matchWhite = reverse ? request.BlackEngine : request.WhiteEngine;
            var matchBlack = reverse ? request.WhiteEngine : request.BlackEngine;

            var summary = new EngineMatchSummaryEventArgs
            {
                Index = index,
                WhiteEngine = !string.IsNullOrEmpty(matchWhite.Name) ? matchWhite.Name! : matchWhite.BinaryPath,
                BlackEngine = !string.IsNullOrEmpty(matchBlack.Name) ? matchBlack.Name! : matchBlack.BinaryPath,
                Status = EngineMatchStatus.Queued,
                ReverseEngines = reverse
            };

            _matches.Add(summary);
            NotifyMatchUpdated(summary);
#pragma warning disable CA2025 // Do not pass 'IDisposable' instances into unawaited tasks

            tasks.Add(RunMatchAsync(summary, matchWhite, matchBlack, request, semaphore, token));
#pragma warning restore CA2025 // Do not pass 'IDisposable' instances into unawaited tasks

        }

        try
        {
            await Task.WhenAll(tasks);
        }
        finally
        {
            _isRunning = false;
            _cts.Dispose();
            _cts = null;
            BatchFinished?.Invoke(this, EventArgs.Empty);
            semaphore.Dispose();
        }
    }

    public async Task StopAsync()
    {
        if (!_isRunning || _cts is null)
            return;

        await _cts.CancelAsync();
        List<EngineMatchRuntime> runtimes;
        lock (_lock)
        {
            runtimes = _activeRuntimes.Values.ToList();
        }

        foreach (var runtime in runtimes)
        {
#pragma warning disable CA1031 // Do not catch general exception types

            try
            {
                await runtime.EngineGame.StopGame();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"failed stopping match: {ex.Message}");
            }
#pragma warning restore CA1031 // Do not catch general exception types

            runtime.Completion.TrySetResult();
        }
    }

    private async Task RunMatchAsync(
        EngineMatchSummaryEventArgs summary,
        EngineRunOptions white,
        EngineRunOptions black,
        EngineMatchBatchRequest request,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        try
        {
            await semaphore.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            summary.Status = EngineMatchStatus.Cancelled;
            NotifyMatchUpdated(summary);
            return;
        }

#pragma warning disable CA1031 // Do not catch general exception types

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                summary.Status = EngineMatchStatus.Cancelled;
                return;
            }

            summary.Status = EngineMatchStatus.Running;
            NotifyMatchUpdated(summary);

            using var clock = new ChessClock(request.BaseTime, request.Increment);
            var game = new ChessGame(request.StartPosition.Clone(), new GameMetadata(), clock);
            game.ActivatePositionHashing();

            await using var whiteEngine = new UciEngine(white.BinaryPath);
            await using var blackEngine = new UciEngine(black.BinaryPath);
            var threads = Math.Max(1, Math.Max(white.Threads, black.Threads));
            await using var engineGame = new EngineGame(whiteEngine, blackEngine, game, threads);

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engineGame.GameFinished += (_, _) => completion.TrySetResult();

            RegisterRuntime(summary.Id, engineGame, completion);
            await engineGame.Start(clock);
            await completion.Task;

            summary.Result = game.Result;
            summary.Status = cancellationToken.IsCancellationRequested
                ? EngineMatchStatus.Cancelled
                : EngineMatchStatus.Finished;
            summary.Game = game;
            NotifyMatchUpdated(summary);
        }
        catch (OperationCanceledException)
        {
            summary.Status = EngineMatchStatus.Cancelled;
            NotifyMatchUpdated(summary);
        }
        catch (Exception ex)
        {
            summary.Status = EngineMatchStatus.Error;
            summary.ErrorMessage = ex.Message;
            NotifyMatchUpdated(summary);
        }
        finally
        {
            UnregisterRuntime(summary.Id);
            semaphore.Release();
        }
#pragma warning restore CA1031 // Do not catch general exception types

    }

    private void RegisterRuntime(Guid id, EngineGame engineGame, TaskCompletionSource completion)
    {
        lock (_lock)
        {
            _activeRuntimes[id] = new EngineMatchRuntime(engineGame, completion);
        }
    }

    private void UnregisterRuntime(Guid id)
    {
        lock (_lock)
        {
            _activeRuntimes.Remove(id);
        }
    }

    private void NotifyMatchUpdated(EngineMatchSummaryEventArgs summary)
    {
        MatchUpdated?.Invoke(this, summary);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await StopAsync();
        }
    }

    private sealed record EngineMatchRuntime(EngineGame EngineGame, TaskCompletionSource Completion);
}
