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

public sealed class EngineMatchSummary
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
    private readonly List<EngineMatchSummary> _matches = new();
    private readonly Dictionary<Guid, EngineMatchRuntime> _activeRuntimes = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public event EventHandler<EngineMatchSummary>? MatchUpdated;
    public event EventHandler? BatchFinished;

    public IReadOnlyList<EngineMatchSummary> Matches => _matches;
    public bool IsRunning => _isRunning;
    public int MaxConcurrent { get; private set; } = 1;

    public static int ComputeMaxConcurrent(EngineRunOptions white, EngineRunOptions black)
    {
        var poolSize = Math.Min(white.PoolSize, black.PoolSize);
        return Math.Max(1, poolSize / 2);
    }

    public async Task RunAsync(EngineMatchBatchRequest request, CancellationToken cancellationToken = default)
    {
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

            var summary = new EngineMatchSummary
            {
                Index = index,
                WhiteEngine = !string.IsNullOrEmpty(matchWhite.Name) ? matchWhite.Name! : matchWhite.BinaryPath,
                BlackEngine = !string.IsNullOrEmpty(matchBlack.Name) ? matchBlack.Name! : matchBlack.BinaryPath,
                Status = EngineMatchStatus.Queued,
                ReverseEngines = reverse
            };

            _matches.Add(summary);
            NotifyMatchUpdated(summary);
            tasks.Add(RunMatchAsync(summary, matchWhite, matchBlack, request, semaphore, token));
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
        }
    }

    public async Task StopAsync()
    {
        if (!_isRunning || _cts is null)
            return;

        _cts.Cancel();
        List<EngineMatchRuntime> runtimes;
        lock (_lock)
        {
            runtimes = _activeRuntimes.Values.ToList();
        }

        foreach (var runtime in runtimes)
        {
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
            runtime.Completion.TrySetResult();
        }
    }

    private async Task RunMatchAsync(
        EngineMatchSummary summary,
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

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                summary.Status = EngineMatchStatus.Cancelled;
                return;
            }

            summary.Status = EngineMatchStatus.Running;
            NotifyMatchUpdated(summary);

            var clock = new ChessClock(request.BaseTime, request.Increment);
            var game = new ChessGame(request.StartPosition.Clone(), new GameMetadata(), clock);
            game.ActivatePositionHashing();

            var whiteEngine = new UciEngine(white.BinaryPath);
            var blackEngine = new UciEngine(black.BinaryPath);
            var threads = Math.Max(1, Math.Max(white.Threads, black.Threads));
            var engineGame = new EngineGame(whiteEngine, blackEngine, game, threads);

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

            await engineGame.DisposeAsync();
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

    private void NotifyMatchUpdated(EngineMatchSummary summary)
    {
        MatchUpdated?.Invoke(this, summary);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
            await StopAsync();
    }

    private sealed record EngineMatchRuntime(EngineGame EngineGame, TaskCompletionSource Completion);
}
