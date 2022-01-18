using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

    private static ILogger<Engine> Logger => StatusService.logger;

    public Status Status { get; private set; } = new Status();


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

    public Engine(string engineName, string engineBinary)
    {
        EngineGuid = Guid.NewGuid();
        Name = engineName;
        Binary = engineBinary;
        Status.EngineName = Name;
        StatusService.AddEngine(this);
    }

    public void Start()
    {
        if (!File.Exists(Binary))
        {
            throw new FileNotFoundException($"binary for {Name} not found: {Binary}");
        }
        var processStartInfo = new ProcessStartInfo()
        {
            FileName = Binary,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
        };

        engineProcess = new();
        engineProcess.StartInfo = processStartInfo;

        engineProcess.OutputDataReceived += new DataReceivedEventHandler(HandleOutputData);
        engineProcess.ErrorDataReceived += new DataReceivedEventHandler(HandleErrorData);

        engineProcess.Start();
        engineProcess.BeginOutputReadLine();
        engineProcess.BeginErrorReadLine();

        Logger.EngineStarted($"{EngineGuid} {Name}");
        // SetDefaultConfig();
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
            Logger.EngineError($"engine {Name} error: {e.Data}");
        }
    }

    public void Stop()
    {
        if (engineProcess != null && !engineProcess.HasExited)
        {
            Send("quit");
            engineProcess?.WaitForExit(3000);
        }
        engineProcess?.Close();
        engineProcess?.Dispose();
        engineProcess = null;
    }

    public void Send(string cmd)
    {
        if (engineProcess != null)
        {
            engineProcess.StandardInput.WriteLine(cmd);
            engineProcess.StandardInput.Flush();
            Logger.EnginePing($"{EngineGuid} {Name} ping {cmd}");
        }
    }

    public async Task<List<EngineOption>> GetOptions()
    {
        Send("uci");
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
                Send($"setoption name {myoption.Name} value {svalue.ToString().ToLower(CultureInfo.InvariantCulture)}");
            }
            else
            {
                Send($"setoption name {myoption.Name} value {myoption.Value}");
            }
        }
        else
        {
            Logger.EngineWarning($"{EngineGuid} {Name} option {name} not found.");
        }
        await IsReady().ConfigureAwait(false);
    }

    public async Task<bool> IsReady(int fs = 40)
    {
        CancellationTokenSource cts = new();
        Status.StatusChanged += (o, e) =>
        {
            cts.Cancel();
        };

        Send("isready");
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(100, cts.Token).ConfigureAwait(false);
                fs--;
                if (fs < 0)
                {
                    Logger.EngineError($"{EngineGuid} {Name} failed waiting for isready.");
                    return false;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
        return true;
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

        CancellationTokenSource cts = new();
        Status.MoveReady += (o, e) =>
        {
            cts.Cancel();
        };
        Send("stop");
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(100, cts.Token).ConfigureAwait(false);
                fs--;
                if (fs < 0)
                {
                    Logger.EngineError($"{EngineGuid} {Name} failed waiting for bestmove.");
                    return null;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            cts.Dispose();
        }
        var info2 = GetInfo();
        Status.Pvs.Clear();
        return info2;
    }

    private void HandleOutput(string info)
    {
        StatusService.HandleOutput(EngineGuid, info);
    }

    public void Dispose()
    {
        if (engineProcess != null)
        {
            Stop();
        }
        StatusService.RemoveEngine(EngineGuid);
    }
}
