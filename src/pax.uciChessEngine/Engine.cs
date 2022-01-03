using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace pax.uciChessEngine;
public sealed class Engine : IDisposable
{
    public Guid Guid { get; private set; }
    private Process? engineProcess;

    public Status Status => StatusService.Stati[Guid];

    public string Name { get; private set; }
    public string Binary { get; private set; }

    ILogger<Engine> logger => StatusService.logger;

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
        Guid = Guid.NewGuid();
        StatusService.CreateStatus(Guid);
        Name = engineName;
        Binary = engineBinary;
    }

    public void Start()
    {
        if (!File.Exists(Binary))
        {
            throw new Exception($"binary for {Name} not found: {Binary}");
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

        engineProcess = new Process();
        engineProcess.StartInfo = processStartInfo;

        engineProcess.OutputDataReceived += new DataReceivedEventHandler(HandleOutputData);
        // engineProcess.ErrorDataReceived += new DataReceivedEventHandler(HandleErrorData);

        engineProcess.Start();
        engineProcess.BeginOutputReadLine();
        // engineProcess.BeginErrorReadLine();


        logger.LogDebug($"{Guid} {Name} started.");
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
            logger.LogError($"engine {Name} error: {e.Data}");
        }
    }

    public void Stop()
    {
        Send("quit");
        engineProcess?.WaitForExit(3000);

        logger.LogInformation($"engine {Name} quit.");
        engineProcess?.Close();
        engineProcess?.Dispose();
    }

    public void Send(string cmd)
    {
        if (engineProcess != null)
        {
            engineProcess.StandardInput.WriteLine(cmd);
            engineProcess.StandardInput.Flush();
            logger.LogDebug($"{Guid} {Name} ping {cmd}");
        }
    }

    public async Task<List<Option>> GetOptions()
    {
        Send("uci");
        await IsReady();
        return new List<Option>(Status.Options);
    }

    public void SetOption(string name, object value)
    {
        var myoption = Status.Options.FirstOrDefault(f => f.Name == name);
        if (myoption != null)
        {
            myoption.Value = value;
            string? svalue = myoption.Value.ToString();
            if (svalue != null)
            {
                Send($"setoption name {myoption.Name} value {svalue.ToString().ToLower()}");
            }
            else
            {
                Send($"setoption name {myoption.Name} value {myoption.Value}");
            }
            logger.LogDebug($"{Guid} {Name} setting option {myoption.Name} to {myoption.Value}");
        } else
        {
            logger.LogWarning($"{Guid} {Name} option {name} not found.");
        }
    }

    public async Task<bool> IsReady(int fs = 40)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        Status.StatusChanged += (o, e) =>
        {
            cts.Cancel();
        };

        Send("isready");
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(100, cts.Token);
                fs--;
                if (fs < 0)
                {
                    logger.LogError($"{Guid} {Name} failed waiting for isready.");
                    return false;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        return true;
    }

    public EngineInfo GetInfo()
    {
        List<PvInfo> pvInfos = new List<PvInfo>();
        foreach (var pv in Status.Pvs)
        {
            pvInfos.Add(new PvInfo(pv.multipv, pv.GetValues(), pv.GetMoves()));
        }
        return new EngineInfo(Name, pvInfos);
    }

    private void HandleOutput(string info, bool error = false)
    {
        StatusService.HandleOutput(Guid, info);
    }

    public void Dispose()
    {
        if (engineProcess != null)
        {
            Stop();
        }
        StatusService.DeleteStatus(Guid);

    }
}
