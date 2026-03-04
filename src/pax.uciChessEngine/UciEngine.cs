
using System.Diagnostics;
using System.Text;

namespace pax.uciChessEngine;

public sealed class UciEngine : IAsyncDisposable
{
    private readonly string _binaryPath;
    private Process? _process;

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly CancellationTokenSource _engineCts = new();

    private TaskCompletionSource<bool>? _uciOkTcs;
    private TaskCompletionSource<bool>? _readyOkTcs;
    private TaskCompletionSource<string>? _bestMoveTcs;

    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);

    public bool IsRunning => _process is { HasExited: false };

    public UciEngine(string binaryPath)
    {
        _binaryPath = binaryPath ?? throw new ArgumentNullException(nameof(binaryPath));
    }

    // ------------------------------------------------------------
    // START
    // ------------------------------------------------------------
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_binaryPath))
            throw new FileNotFoundException("UCI engine not found.", _binaryPath);

        if (IsRunning)
            return;

        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnOutput;
        _process.ErrorDataReceived += OnError;
        _process.Exited += OnExit;

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await InitializeUciAsync(ct).ConfigureAwait(false);
    }

    private async Task InitializeUciAsync(CancellationToken ct)
    {
        _uciOkTcs = NewTcs<bool>();
        await SendAsync("uci", ct);
        await WaitAsync(_uciOkTcs.Task, _defaultTimeout, ct);

        _readyOkTcs = NewTcs<bool>();
        await SendAsync("isready", ct);
        await WaitAsync(_readyOkTcs.Task, _defaultTimeout, ct);
    }

    // ------------------------------------------------------------
    // SEARCH
    // ------------------------------------------------------------
    public async Task<string> GetBestMoveAsync(
        string fen,
        TimeSpan thinkTime,
        CancellationToken ct = default)
    {
        if (!IsRunning)
            throw new InvalidOperationException("Engine not running.");

        await _commandLock.WaitAsync(ct);
        try
        {
            _bestMoveTcs = NewTcs<string>();

            await SendAsync("ucinewgame", ct);
            await SendAsync($"position fen {fen}", ct);
            await SendAsync($"go movetime {(int)thinkTime.TotalMilliseconds}", ct);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _engineCts.Token);

            var completed = await Task.WhenAny(
                _bestMoveTcs.Task,
                Task.Delay(Timeout.Infinite, linkedCts.Token));

            if (completed != _bestMoveTcs.Task)
            {
                await SendAsync("stop", CancellationToken.None);
                throw new OperationCanceledException(ct);
            }

            return await _bestMoveTcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    // ------------------------------------------------------------
    // OUTPUT HANDLING
    // ------------------------------------------------------------
    private void OnOutput(object? sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
            return;

        var line = e.Data.Trim();

        if (line == "uciok")
            _uciOkTcs?.TrySetResult(true);

        else if (line == "readyok")
            _readyOkTcs?.TrySetResult(true);

        else if (line.StartsWith("bestmove", StringComparison.Ordinal))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                _bestMoveTcs?.TrySetResult(parts[1]);
        }

        // Optional: parse "info depth ..." lines here
    }

    private void OnError(object? sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            // Log — never throw from event handler
            Debug.WriteLine("UCI ERR: " + e.Data);
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _engineCts.Cancel();
        FailPendingTasks(new InvalidOperationException("Engine process exited."));
    }

    // ------------------------------------------------------------
    // SEND
    // ------------------------------------------------------------
    private async Task SendAsync(string command, CancellationToken ct)
    {
        if (_process?.HasExited != false)
            throw new InvalidOperationException("Engine not running.");

        await _process.StandardInput.WriteLineAsync(command).WaitAsync(ct);
        await _process.StandardInput.FlushAsync(CancellationToken.None).WaitAsync(ct);
    }

    // ------------------------------------------------------------
    // STOP
    // ------------------------------------------------------------
    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            if (_process is not null)
            {
                _process.OutputDataReceived -= OnOutput;
                _process.ErrorDataReceived -= OnError;
                _process.Exited -= OnExit;
            }
            await SendAsync("quit", CancellationToken.None);
            await _process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch
        {
            if (!_process!.HasExited)
                _process.Kill(true);
        }
        finally
        {
            Cleanup();
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }

    private void Cleanup()
    {
        if (_process is null) return;

        _process.OutputDataReceived -= OnOutput;
        _process.ErrorDataReceived -= OnError;

        _process.Dispose();
        _process = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _commandLock.Dispose();
        _engineCts.Dispose();
    }

    // ------------------------------------------------------------
    // UTILITIES
    // ------------------------------------------------------------
    private static TaskCompletionSource<T> NewTcs<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitAsync(
        Task task,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, linked.Token));

        if (completed != task)
            throw new TimeoutException("UCI command timed out.");

        await task.ConfigureAwait(false);
    }

    private void FailPendingTasks(Exception ex)
    {
        _uciOkTcs?.TrySetException(ex);
        _readyOkTcs?.TrySetException(ex);
        _bestMoveTcs?.TrySetException(ex);
    }
}