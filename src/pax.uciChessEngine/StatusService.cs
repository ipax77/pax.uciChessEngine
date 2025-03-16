using pax.chess;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace pax.uciChessEngine;

internal static partial class StatusService
{
    internal static ConcurrentDictionary<Guid, Engine> Engines = new();
    private static readonly Channel<KeyValuePair<Guid, string>> OutputChannel = Channel.CreateUnbounded<KeyValuePair<Guid, string>>();
    private static readonly object lockobject = new();
    private static bool IsConsuming;
    private static CancellationTokenSource tokenSource = new();

    private static readonly Regex bestmoveRx = BestMoveGrx();
    private static readonly Regex valueRx = ValueGrx();
    private static readonly Regex optionNameRx = OptionTypeGrx();
    private static readonly Regex optionTypeRx = OptionNameGrx();
    private static readonly Regex optionMinRx = OptionMinGrx();
    private static readonly Regex optionMaxRx = OptionMaxGrx();
    private static readonly Regex optionDefaultRx = OptionDefaultGrx();
    private static readonly Regex optionComboRx = OptionComboGrx();

    internal static void AddEngine(Engine engine)
    {
        Engines.TryAdd(engine.EngineGuid, engine);
        _ = Consume();
    }

    internal static void RemoveEngine(Guid guid)
    {
        Engines.TryRemove(guid, out _);
        if (Engines.IsEmpty)
        {
            IsConsuming = false;
            tokenSource.Cancel();
        }
    }

    public static void HandleOutput(Guid guid, string output)
    {
        OutputChannel.Writer.TryWrite(new KeyValuePair<Guid, string>(guid, output));
    }

    public static async Task Consume()
    {
        lock (lockobject)
        {
            if (IsConsuming)
            {
                return;
            }
            else
            {
                IsConsuming = true;
                tokenSource.Dispose();
                tokenSource = new CancellationTokenSource();
            }
        }

        try
        {
            while (await OutputChannel.Reader.WaitToReadAsync(tokenSource.Token).ConfigureAwait(false))
            {
                if (OutputChannel.Reader.TryRead(out KeyValuePair<Guid, string> output))
                {
                    if (Engines.TryGetValue(output.Key, out Engine? engine))
                    {
                        ParseOutput(engine, output.Value);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsConsuming = false;
        }
    }

    internal static void ParseOutput(Engine engine, string output)
    {
        var status = engine.Status;

        if (status.State == EngineState.None)
        {
            UpdateStatus(status, EngineState.Started);
        }

        if (output.StartsWith("info ", StringComparison.Ordinal))
        {
            ProcessInfoOutput(status, output);
        }
        else if (output.StartsWith("bestmove ", StringComparison.Ordinal))
        {
            ProcessBestMove(status, output);
        }
        else if (output == "readyok")
        {
            UpdateStatus(status, EngineState.Ready);
        }
        else if (output.StartsWith("error", StringComparison.OrdinalIgnoreCase))
        {
            status.OnErrorRaised(new ErrorEventArgs(output));
        }
        else if (output.StartsWith("option", StringComparison.Ordinal))
        {
            ProcessOptionOutput(engine, status, output);
        }
    }

    private static void UpdateStatus(Status status, EngineState newState)
    {
        status.State = newState;
        status.OnStatusChanged(new StatusEventArgs { State = newState });
    }

    private static void ProcessInfoOutput(Status status, string output)
    {
        if (output.Contains("evaluation using", StringComparison.Ordinal))
        {
            status.State = EngineState.Evaluating;
        }
        else
        {
            var infos = output.Split(" pv ", 2);
            if (infos.Length == 2)
            {
                var ents = ExtractKeyValuePairs(infos[0]);
                int multipv = ents.GetValueOrDefault("multipv", 1);
                var info = status.GetPv(multipv);
                info.SetValues(ents);
                info.SetMoves(infos[1].Split(' ').ToList());
            }
        }
        status.State = EngineState.Calculating;
    }

    private static Dictionary<string, int> ExtractKeyValuePairs(string input)
    {
        var ents = new Dictionary<string, int>();
        Match match = valueRx.Match(input);
        while (match.Success)
        {
            ents[match.Groups[1].Value] = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            match = match.NextMatch();
        }
        return ents;
    }

    private static void ProcessBestMove(Status status, string output)
    {
        var match = bestmoveRx.Match(output);
        status.BestMove = match.Success
            ? Map.GetEngineMove(match.Groups[1].Value)
            : Map.GetEngineMove(output[9..]);

        status.Ponder = match.Success ? Map.GetEngineMove(match.Groups[2].Value) : null;

        if (status.BestMove != null)
        {
            UpdateStatus(status, EngineState.BestMove);
            status.OnMoveReady(new MoveEventArgs(status.BestMove));
        }
    }

    private static void ProcessOptionOutput(Engine engine, Status status, string output)
    {
        Match nameMatch = optionNameRx.Match(output);
        if (!nameMatch.Success)
        {
            engine.LogWarning($"{status.EngineName} could not identify option: {output}");
            return;
        }

        string name = nameMatch.Groups[1].Value;
        if (status.Options.Any(opt => opt.Name == name)) return;

        string type = optionTypeRx.Match(output).Groups[1].Value;
        object defaultValue = ExtractDefaultValue(output, type, out int min, out int max, out List<string>? vars);

        status.Options.Add(new EngineOption(name, type, defaultValue, vars, min, max));
    }

    private static object ExtractDefaultValue(string output, string type, out int min, out int max, out List<string>? vars)
    {
        min = max = 0;
        vars = null;
        return type switch
        {
            "spin" => new
            {
                Min = int.Parse(optionMinRx.Match(output).Groups[1].Value, CultureInfo.InvariantCulture),
                Max = int.Parse(optionMaxRx.Match(output).Groups[1].Value, CultureInfo.InvariantCulture),
                DefaultValue = int.Parse(optionDefaultRx.Match(output).Groups[1].Value, CultureInfo.InvariantCulture)
            },
            "string" => optionDefaultRx.Match(output).Groups[1].Value switch
            {
                "<empty>" => string.Empty,
                var value => value
            },
            "check" => bool.Parse(optionDefaultRx.Match(output).Groups[1].Value),
            "combo" => new
            {
                DefaultValue = optionDefaultRx.Match(output).Groups[1].Value,
                Vars = optionComboRx.Matches(output).Select(m => m.Groups[1].Value).ToList()
            },
            _ => string.Empty
        };
    }

    [GeneratedRegex(@"^bestmove\s(.*)\sponder\s(.*)$")]
    private static partial Regex BestMoveGrx();
    [GeneratedRegex(@"\s(\w+)\s(\-?\d+)")]
    private static partial Regex ValueGrx();
    [GeneratedRegex(@"option\s+name\s+([\d\w\s]+)\s+type")]
    private static partial Regex OptionTypeGrx();
    [GeneratedRegex(@"\s+type\s+([^\s]+)")]
    private static partial Regex OptionNameGrx();
    [GeneratedRegex(@"\s+min\s+([^\s]+)")]
    private static partial Regex OptionMinGrx();
    [GeneratedRegex(@"\s+max\s+([^\s]+)")]
    private static partial Regex OptionMaxGrx();
    [GeneratedRegex(@"\s+default\s+([^\s]+)")]
    private static partial Regex OptionDefaultGrx();
    [GeneratedRegex(@"\s+var\s+([\d\w_-]+)")]
    private static partial Regex OptionComboGrx();
}
