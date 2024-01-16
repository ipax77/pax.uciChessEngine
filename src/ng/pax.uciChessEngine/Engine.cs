using System.Diagnostics;
using System.Text;

namespace pax.uciChessEngine;

public class Engine
{
    private Process? engineProcess;
    private readonly SemaphoreSlim enigneSS = new(1, 1);
    private readonly DataReceivedEventHandler outputDataHandler;
    private readonly DataReceivedEventHandler errorDataHandler;
    private readonly Dictionary<string, ParseOptionResult> options = new();
    private readonly int engineDelay;

    public EventHandler<RawEngineEventArgs>? DataReceived;
    public EventHandler? IsReadyReceived;
    public EventHandler<BestMoveEventArgs>? BestMoveReady;
    public EventHandler<PvInfoEventArgs>? PvInfoReady;


    protected virtual void OnDataReceived(RawEngineEventArgs e)
    {
        DataReceived?.Invoke(this, e);
    }

    protected virtual void OnIsReadyReceived()
    {
        IsReadyReceived?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnBestMoveReady(BestMoveEventArgs e)
    {
        BestMoveReady?.Invoke(this, e);
    }

    protected virtual void OnPvInfoReady(PvInfoEventArgs e)
    {
        PvInfoReady?.Invoke(this, e);
    }

    public Engine(int engineDelayInMilliseconds = 200)
    {
        outputDataHandler = new DataReceivedEventHandler(HandleOutputData);
        errorDataHandler = new DataReceivedEventHandler(HandleErrorData);
        engineDelay = engineDelayInMilliseconds;
    }

    public void Start(string binary, string arguments = "", bool processWindowStyleHidden = true)
    {
        if (!File.Exists(binary))
        {
            throw new FileNotFoundException($"binary not found: {binary}");
        }
        var processStartInfo = new ProcessStartInfo()
        {
            FileName = binary,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WindowStyle = processWindowStyleHidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            CreateNoWindow = processWindowStyleHidden,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };

        engineProcess = new Process
        {
            StartInfo = processStartInfo
        };

        engineProcess.OutputDataReceived += outputDataHandler;
        engineProcess.ErrorDataReceived += errorDataHandler;

        engineProcess.Start();
        engineProcess.BeginOutputReadLine();
        engineProcess.BeginErrorReadLine();
    }

    private void HandleOutputData(object sender, DataReceivedEventArgs e)
    {
        var result = UciParser.ParseLine(e.Data?.ToString());
        OnDataReceived(new() { Result = result });

        if (result is ParseReadyResult)
        {
            OnIsReadyReceived();
        }
        else if (result is ParseOptionResult optionResult)
        {
            if (optionResult.OptionName is not null 
                && !options.ContainsKey(optionResult.OptionName))
            {
                options.Add(optionResult.OptionName, optionResult);
            }
        }
        else if (result is ParseBestMoveResult bestMoveResult)
        {
            OnBestMoveReady(new BestMoveEventArgs() { Result = bestMoveResult });
        }
        else if (result is ParseInfoResult infoResult)
        {
            OnPvInfoReady(new PvInfoEventArgs() { Result = infoResult });
        }
    }

    private void HandleErrorData(object sender, DataReceivedEventArgs e)
    {

    }

    public async Task<bool> IsReady(int timeoutMilliseconds = 3000, int attempts = 40)
    {
        using var cts = new CancellationTokenSource(timeoutMilliseconds);
        bool isReadyReceived = false;
        void CheckIsReadyReceived(object? sender, EventArgs e)
        {
            isReadyReceived = true;
        }

        try
        {
            IsReadyReceived += CheckIsReadyReceived;

            while (!cts.IsCancellationRequested)
            {
                await Send("isready").ConfigureAwait(false);
                await Task.Delay(timeoutMilliseconds / attempts, cts.Token);
                if (isReadyReceived)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            IsReadyReceived -= CheckIsReadyReceived;
        }

        return false;
    }

    public async Task<List<ParseOptionResult>> GetOptions()
    {
        if (options.Count == 0)
        {
            if (await IsReady())
            {
                await Send("uci");
            }
            else
            {
                return [];
            }
            await IsReady();
        }
        return [.. options.Values];
    }

    public async Task<bool> SetOption(string name, object value)
    {
        if (options.Count == 0)
        {
            await GetOptions();
        }

        if (!options.TryGetValue(name, out var parseOption)
            || parseOption is null)
        {
            return false;
        }

        await Send($"setoption name {name} value {value}").ConfigureAwait(false);
        parseOption.CustomValue = value;

        return await IsReady();
    }

    public async Task SetFenPosition(string fen)
    {
        if (await IsReady())
        {
            await Send($"position fen {fen}");
        }
    }

    public async Task<ParseBestMoveResult?> GetBestMove(int calcTimeMilliseconds = 1000)
    {
        ParseBestMoveResult? result = null;
        ManualResetEvent mre = new(false);
        EngineScore? score = null;

        void CheckBestMoveReadyReceived(object? sender, BestMoveEventArgs e)
        {
            result = e.Result;
            mre.Set();
        }

        void CheckPvInfosReceived(object? sender, PvInfoEventArgs e)
        {
            if (e.Result is not null && e.Result.MultiPv == 1)
            {
                score = new(e.Result);
            }
        }

        if (await IsReady())
        {
            BestMoveReady += CheckBestMoveReadyReceived;
            PvInfoReady += CheckPvInfosReceived;
            await Send($"go movetime {calcTimeMilliseconds}");
            mre.WaitOne(calcTimeMilliseconds + engineDelay);
            BestMoveReady -= CheckBestMoveReadyReceived;
            PvInfoReady -= CheckPvInfosReceived;
            if (result is not null)
            {
                result.EngineScore = score;
            }
            return result;
        }
        return null;
    }

    public void Stop()
    {
        if (engineProcess != null && !engineProcess.HasExited)
        {
            _ = Send("quit");
            engineProcess?.WaitForExit(3000);
        }
        engineProcess?.Close();
        engineProcess?.Dispose();
        engineProcess = null;
    }

    public async Task Send(string cmd)
    {
        if (engineProcess != null)
        {
            await enigneSS.WaitAsync().ConfigureAwait(false);
            try
            {
                await engineProcess.StandardInput.WriteLineAsync(cmd).ConfigureAwait(false);
                if (cmd != "quit")
                {
                    await engineProcess.StandardInput.FlushAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                enigneSS.Release();
            }
        }
    }

    public void Dispose()
    {
        if (engineProcess != null)
        {
            engineProcess.OutputDataReceived -= outputDataHandler;
            engineProcess.ErrorDataReceived -= errorDataHandler;
            Stop();
        }

        enigneSS.Dispose();
    }
}

public class RawEngineEventArgs : EventArgs
{
    public ParseResult? Result { get; init; }
}

public class BestMoveEventArgs : EventArgs
{
    public ParseBestMoveResult? Result { get; init; }
}

public class PvInfoEventArgs : EventArgs
{
    public ParseInfoResult? Result { get; set; }
}

public record EngineScore
{
    public EngineScore() { }
    public EngineScore(ParseInfoResult result)
    {
        IsMateScore = result.ScoreMate != 0;
        Score = IsMateScore ? result.ScoreMate : result.ScoreCp;       
    }
    public bool IsMateScore { get; init; }
    public int Score { get; init; }
}