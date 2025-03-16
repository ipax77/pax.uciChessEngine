using Microsoft.Extensions.Logging;
using pax.chess;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace pax.uciChessEngine;

internal static class StatusService
{
    internal static ConcurrentDictionary<Guid, Engine> Engines = new();
    private static readonly Channel<KeyValuePair<Guid, string>> OutputChannel = Channel.CreateUnbounded<KeyValuePair<Guid, string>>();
    private static readonly object lockobject = new();
    private static bool IsConsuming;
    private static CancellationTokenSource tokenSource = new();

    private static readonly Regex bestmoveRx = new(@"^bestmove\s(.*)\sponder\s(.*)$");
    private static readonly Regex valueRx = new(@"\s(\w+)\s(\-?\d+)");
    private static readonly Regex optionNameRx = new(@"option\s+name\s+([\d\w\s]+)\s+type");
    private static readonly Regex optionTypeRx = new(@"\s+type\s+([^\s]+)");
    private static readonly Regex optionMinRx = new(@"\s+min\s+([^\s]+)");
    private static readonly Regex optionMaxRx = new(@"\s+max\s+([^\s]+)");
    private static readonly Regex optionDefaultRx = new(@"\s+default\s+([^\s]+)");
    private static readonly Regex optionComboRx = new(@"\s+var\s+([\d\w_-]+)");

    // public static ILogger<Engine> logger = ApplicationDebugLogging.CreateLogger<Engine>();
    public static ILogger<Engine> logger = ApplicationLogging.CreateLogger<Engine>();

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
                    else
                    {
                        logger.EngineWarning($"engine {output.Key} not found: {output.Value}");
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
        Status status = engine.Status;
        // logger.EnginePong($"{engine.EngineGuid} {output}");

        if (status.State == EngineState.None)
        {
            status.State = EngineState.Started;
            status.OnStatusChanged(new StatusEventArgs() { State = EngineState.Started });
        }

        if (output.StartsWith("info ", StringComparison.Ordinal))
        {
            if (output.Contains("evaluation using", StringComparison.Ordinal))
            {
                status.State = EngineState.Evaluating;
            }

            //if (output.Contains("currmove"))
            //{
            //    if (infos.Length == 6)
            //    {
            //        Depth = GetInt(infos[2]);
            //        CurrentMove = Map.GetEngineMove(infos[4]);
            //        CurrentMoveNumber = GetInt(infos[6]);
            //    }
            //}
            else
            {
                var infos = output.Split(" pv ");
                if (infos.Length == 2)
                {
                    Match m = valueRx.Match(infos[0]);
                    Dictionary<string, int> ents = new();
                    while (m.Success)
                    {
                        ents[m.Groups[1].Value] = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        m = m.NextMatch();
                    }
                    if (ents.TryGetValue("multipv", out int value))
                    {
                        var info = status.GetPv(value);
                        info.SetValues(ents);
                        info.SetMoves(infos[1].Split(" ").ToList());
                    }
                    else
                    {
                        var info = status.GetPv(1);
                        info.SetValues(ents);
                        info.SetMoves(infos[1].Split(" ").ToList());
                    }
                }
            }
            status.State = EngineState.Calculating;
        }
        else if (output.StartsWith("bestmove ", StringComparison.Ordinal))
        {
            var match = bestmoveRx.Match(output);
            if (match.Success)
            {
                status.BestMove = Map.GetEngineMove(match.Groups[1].Value);
                status.Ponder = Map.GetEngineMove(match.Groups[2].Value);
            }
            else
            {
                status.BestMove = Map.GetEngineMove(output.Substring(9));
            }
            if (status.BestMove != null)
            {
                status.State = EngineState.BestMove;
                status.OnMoveReady(new MoveEventArgs(status.BestMove));
            }
        }
        else if (output == "readyok")
        {
            status.State = EngineState.Ready;
            status.OnStatusChanged(new StatusEventArgs() { State = EngineState.Ready });
        }
        else if (output.StartsWith("error", StringComparison.InvariantCultureIgnoreCase))
        {
            status.OnErrorRaised(new ErrorEventArgs(output));
        }
        else if (output.StartsWith("option", StringComparison.Ordinal))
        {
            string name = String.Empty;
            int min = 0;
            int max = 0;
            object defaultValue = String.Empty;
            string type = String.Empty;
            object? Value = null;
            List<string>? vars = null;

            Match nameMatch = optionNameRx.Match(output);
            if (nameMatch.Success)
            {
                name = nameMatch.Groups[1].Value;
                if (status.Options.FirstOrDefault(f => f.Name == name) != null)
                {
                    return;
                }
            }
            else
            {
                logger.EngineWarning($"{status.EngineName} could not identify option: {output}");
                return;
            }

            Match typeMatch = optionTypeRx.Match(output);
            if (typeMatch.Success)
            {
                type = typeMatch.Groups[1].Value;
            }
            if (type == "spin")
            {
                Match minMatch = optionMinRx.Match(output);
                if (minMatch.Success)
                {
                    min = int.Parse(minMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                Match maxMatch = optionMaxRx.Match(output);
                if (maxMatch.Success)
                {
                    max = int.Parse(maxMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                Match defaultMatch = optionDefaultRx.Match(output);
                {
                    defaultValue = int.Parse(defaultMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    Value = defaultValue;
                }
            }
            else if (type == "string")
            {
                Match defaultMatch = optionDefaultRx.Match(output);
                {
                    defaultValue = defaultMatch.Groups[1].Value;
                    if ((string)defaultValue == "<empty>")
                    {
                        defaultValue = String.Empty;
                    }
                    Value = defaultValue;
                }
            }
            else if (type == "check")
            {
                Match defaultMatch = optionDefaultRx.Match(output);
                {
                    defaultValue = bool.Parse(defaultMatch.Groups[1].Value);
                    Value = defaultValue;
                }
            }
            else if (type == "combo")
            {
                Match defaultMatch = optionDefaultRx.Match(output);
                {
                    defaultValue = defaultMatch.Groups[1].Value;
                    Value = defaultValue;
                }

                vars = new List<string>();
                Match comboMatch = optionComboRx.Match(output);
                while (comboMatch.Success)
                {
                    vars.Add(comboMatch.Groups[1].Value);
                    comboMatch = comboMatch.NextMatch();
                }
            }
            status.Options.Add(new EngineOption(name, type, defaultValue, vars, min, max));
        }
    }
}
