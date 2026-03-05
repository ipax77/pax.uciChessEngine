
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace pax.uciChessEngine;

public sealed partial class UciEngine : IAsyncDisposable
{
    private readonly string _binaryPath;
    private Process? _process;
    private Task? _readerTask;
    private CancellationTokenSource? _readerCts;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly CancellationTokenSource _engineCts = new();

    private TaskCompletionSource<bool>? _uciOkTcs;
    private TaskCompletionSource<bool>? _readyOkTcs;
    private TaskCompletionSource<string>? _bestMoveTcs;

    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);

    public bool IsRunning => _process is { HasExited: false };
    private Status _status = new();
    public Status Status => _status;

    public event EventHandler<MoveEventArgs>? MoveReady;
    public event EventHandler<StatusEventArgs>? StatusChanged;
    public event EventHandler<ErrorEventArgs>? ErrorRaised;

    private readonly Channel<string> _outputChannel =
    Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

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
            WorkingDirectory = Path.GetDirectoryName(_binaryPath),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.ASCII,
            StandardInputEncoding = Encoding.ASCII,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnOutput;
        _process.ErrorDataReceived += OnError;
        _process.Exited += OnExit;

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        _readerCts = new CancellationTokenSource();
        _readerTask = ProcessOutputAsync(_readerCts.Token);

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

    public async Task SetOption(string name, object value, CancellationToken token = default)
    {
        var myoption = _status.EngineOptions.FirstOrDefault(f => f.Name == name);
        if (myoption != null)
        {
            myoption.Value = value;
            string? svalue = myoption.Value.ToString();
            if (svalue != null)
            {
#pragma warning disable CA1308 // Normalize strings to uppercase
                await SendAsync($"setoption name {myoption.Name} value {svalue.ToString().ToLowerInvariant()}", token);
#pragma warning restore CA1308 // Normalize strings to uppercase
            }
            else
            {
                await SendAsync($"setoption name {myoption.Name} value {myoption.Value}", token);
            }
        }
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
    private async Task ProcessOutputAsync(CancellationToken ct)
    {
        await foreach (var line in _outputChannel.Reader.ReadAllAsync(ct))
        {
            ParseUciString(line);
        }
    }

    private void OnOutput(object? sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
            _outputChannel.Writer.TryWrite(e.Data);
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

    private void OnMoveReady(MoveEventArgs e)
    {
        MoveReady?.Invoke(this, e);
    }

    private void OnStatusChanged(StatusEventArgs e)
    {
        StatusChanged?.Invoke(this, e);
    }

    private void OnErrorRaised(ErrorEventArgs e)
    {
        ErrorRaised?.Invoke(this, e);
    }

    // ------------------------------------------------------------
    // SEND
    // ------------------------------------------------------------
    public async Task SendAsync(string command, CancellationToken ct)
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
            if (_readerCts is not null) await _readerCts.CancelAsync();
            if (_readerTask != null)
            {
                try { await _readerTask; }
                catch (OperationCanceledException) { }
            }
            Cleanup();
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }

    private void Cleanup()
    {
        if (_process is null) return;

        _process.OutputDataReceived -= OnOutput;
        _process.ErrorDataReceived -= OnError;
        _process.Exited -= OnExit;

        _process.Dispose();
        _process = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _commandLock.Dispose();
        _engineCts.Dispose();
        _readerCts?.Dispose();
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