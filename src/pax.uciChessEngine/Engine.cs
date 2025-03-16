using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace pax.uciChessEngine;

[SuppressMessage(
    "Usage", "CA1308:Normalize strings to uppercase",
    Justification = "the engine option requires lower case")]
public sealed class Engine : IDisposable
{
    public Guid EngineGuid { get; private set; }
    private Process? engineProcess;

    public string Name { get; private set; }
    public string Binary { get; private set; }

    public Status Status { get; private set; } = new Status();

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<Engine> _logger;
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly SemaphoreSlim sendSemaphore = new(1, 1);
    private EventWaitHandle startEwh = new(false, EventResetMode.ManualReset);
    private EventWaitHandle readyEwh = new(false, EventResetMode.ManualReset);
    private EventWaitHandle infoEwh = new(false, EventResetMode.ManualReset);

    public int Threads()
    {
        var option = Status.Options.FirstOrDefault(f => f.Name == "Threads");
        if (option == null)
        {
            return 1;
        }
        else
        {
            return (int)option.Value;
        }
    }

    public Engine(string engineName, string engineBinary, LogLevel logLevel = LogLevel.None)
    {
        EngineGuid = Guid.NewGuid();
        Name = engineName;
        Binary = engineBinary;
        Status.EngineName = Name;

        _loggerFactory = CreateLoggerFactory(logLevel);
        _logger = _loggerFactory.CreateLogger<Engine>();
        StatusService.AddEngine(this);
    }

    public void LogWarning(string msg)
    {
        _logger.EngineWarning(msg);
    }

    public async Task<bool> Start()
    {
        if (!File.Exists(Binary))
        {
            throw new FileNotFoundException($"binary for {Name} not found: {Binary}");
        }
        var processStartInfo = new ProcessStartInfo()
        {
            FileName = Binary,
            Arguments = String.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };

        engineProcess = new();
        engineProcess.StartInfo = processStartInfo;

        engineProcess.OutputDataReceived += new DataReceivedEventHandler(HandleOutputData);
        engineProcess.ErrorDataReceived += new DataReceivedEventHandler(HandleErrorData);

        engineProcess.Start();
        engineProcess.BeginOutputReadLine();
        engineProcess.BeginErrorReadLine();

        _logger.EngineStarted($"{EngineGuid} {Name}");

        Status.ErrorRaised += ErrorRaised;
        // SetDefaultConfig();

        startEwh = new(false, EventResetMode.ManualReset);
        EventHandler<StatusEventArgs> startEvent = (s, e) => { startEwh.Set(); };
        Status.StatusChanged += startEvent;

        await Send("isready").ConfigureAwait(false);
        try
        {
            return startEwh.WaitOne(40 * 200);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Status.StatusChanged -= startEvent;
        }
        return false;
    }

    private void ErrorRaised(object? sender, ErrorEventArgs e)
    {
        _logger.EngineError($"{EngineGuid} error: {e.Error}");
    }

    private void HandleOutputData(object sender, DataReceivedEventArgs e)
    {
        if (!String.IsNullOrEmpty(e.Data))
        {
            HandleOutput(e.Data);
        }
    }

    private void HandleErrorData(object sender, DataReceivedEventArgs e)
    {
        if (!String.IsNullOrEmpty(e.Data))
        {
            _logger.EngineError($"engine {Name} error: {e.Data}");
        }
    }

    public void Stop()
    {
#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            if (engineProcess != null && !engineProcess.HasExited)
            {
                _ = Send("quit");
                engineProcess?.WaitForExit(3000);
            }
            engineProcess?.Close();
            engineProcess?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.EngineError($"{EngineGuid} {Name} failed stopping: {ex.Message}");
            engineProcess?.Dispose();
        }
        finally
        {
            engineProcess = null;
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }

    public async Task Send(string cmd)
    {
        if (engineProcess != null)
        {
            await sendSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await engineProcess.StandardInput.WriteLineAsync(cmd).ConfigureAwait(false);
                if (cmd != "quit")
                {
                    await engineProcess.StandardInput.FlushAsync().ConfigureAwait(false);
                }
                _logger.EnginePing($"{EngineGuid} {Name} ping {cmd}");
            }
            finally
            {
                sendSemaphore.Release();
            }
        }
    }

    public async Task<List<EngineOption>> GetOptions()
    {
        await Send("uci").ConfigureAwait(false);
        await IsReady().ConfigureAwait(false);
        return new List<EngineOption>(Status.Options);
    }

    public async Task SetOption(string name, object value)
    {
        var myoption = Status.Options.FirstOrDefault(f => f.Name == name);
        if (myoption != null)
        {
            myoption.Value = value;
            string? svalue = myoption.Value.ToString();
            if (svalue != null)
            {
                await Send($"setoption name {myoption.Name} value {svalue.ToString().ToLower(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
            }
            else
            {
                await Send($"setoption name {myoption.Name} value {myoption.Value}").ConfigureAwait(false);
            }
        }
        else
        {
            _logger.EngineWarning($"{EngineGuid} {Name} option {name} not found.");
        }
        await IsReady().ConfigureAwait(false);
    }

    public async Task<bool> IsReady(int fs = 40)
    {
        await semaphore.WaitAsync().ConfigureAwait(false);
        readyEwh = new(false, EventResetMode.ManualReset);
        EventHandler<StatusEventArgs> readyEvent = (s, e) => { readyEwh.Set(); };
        Status.StatusChanged += readyEvent;

        await Send("isready").ConfigureAwait(false);
        try
        {
            return readyEwh.WaitOne(fs * 200);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            semaphore.Release();
            Status.StatusChanged -= readyEvent;
        }
        return false;
    }

    public EngineInfo GetInfo()
    {
        List<PvInfo> pvInfos = new();
        foreach (var pv in Status.Pvs.Values)
        {
            pvInfos.Add(new PvInfo(pv.Multipv, pv.GetValues(), pv.GetMoves()));
        }
        return new EngineInfo(Name, pvInfos);
    }

    public async Task<EngineInfo?> GetStopInfo(int fs = 40)
    {
        if (Status.State == EngineState.BestMove)
        {
            var info1 = GetInfo();
            Status.Pvs.Clear();
            return info1;
        }

        await semaphore.WaitAsync().ConfigureAwait(false);
        infoEwh = new(false, EventResetMode.ManualReset);
        EventHandler<MoveEventArgs> infoEvent = (s, e) => { infoEwh.Set(); };
        Status.MoveReady += infoEvent;
        bool success = false;
        await Send("stop").ConfigureAwait(false);
        try
        {
            success = infoEwh.WaitOne(fs * 200);
        }
        catch (OperationCanceledException) { }
        finally
        {
            semaphore.Release();
            Status.MoveReady -= infoEvent;
        }
        if (!success)
        {
            _logger.EngineError($"{EngineGuid} failed waiting for info");
        }
        var info2 = GetInfo();
        Status.Pvs.Clear();
        return info2;
    }

    private void HandleOutput(string info)
    {
        // StatusService.HandleOutput(EngineGuid, info);
        StatusService.ParseOutput(this, info);
    }

    private static ILoggerFactory CreateLoggerFactory(LogLevel logLevel)
    {
        if (logLevel == LogLevel.None)
            return NullLoggerFactory.Instance;

        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            }).SetMinimumLevel(logLevel);
        });
    }

    public void Dispose()
    {
        if (engineProcess != null)
        {
            Stop();
        }
        Status.ErrorRaised -= ErrorRaised;
        semaphore.Dispose();
        sendSemaphore.Dispose();
        startEwh.Dispose();
        readyEwh.Dispose();
        infoEwh.Dispose();

        StatusService.RemoveEngine(EngineGuid);
        _loggerFactory.Dispose();
    }
}
