using Microsoft.Extensions.Logging;
using pax.chess;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace pax.uciChessEngine;

internal static class StatusService
{
    internal static ConcurrentDictionary<Guid, Engine> Engines = new ConcurrentDictionary<Guid, Engine>();
    private static Channel<KeyValuePair<Guid, string>> OutputChannel = Channel.CreateUnbounded<KeyValuePair<Guid, string>>();
    private static object lockobject = new object();
    private static bool IsConsuming = false;
    private static CancellationTokenSource tokenSource = new CancellationTokenSource();

    private static Regex bestmoveRx = new Regex(@"^bestmove\s(.*)\sponder\s(.*)$");
    private static Regex valueRx = new Regex(@"\s(\w+)\s(\-?\d+)");
    private static Regex optionNameRx = new Regex(@"option\s+name\s+([\d\w\s]+)\s+type");
    private static Regex optionTypeRx = new Regex(@"\s+type\s+([^\s]+)");
    private static Regex optionMinRx = new Regex(@"\s+min\s+([^\s]+)");
    private static Regex optionMaxRx = new Regex(@"\s+max\s+([^\s]+)");
    private static Regex optionDefaultRx = new Regex(@"\s+default\s+([^\s]+)");
    private static Regex optionComboRx = new Regex(@"\s+var\s+([\d\w_-]+)");

    public static ILogger<Engine> logger = ApplicationDebugLogging.CreateLogger<Engine>();

    internal static void AddEngine(Engine engine)
    {
        Engines.TryAdd(engine.Guid, engine);
        _ = Consume();
    }

    internal static void RemoveEngine(Guid guid)
    {
        Engines.TryRemove(guid, out _);
        if (!Engines.Any())
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
            while (await OutputChannel.Reader.WaitToReadAsync(tokenSource.Token))
            {
                KeyValuePair<Guid, string> output;
                if (OutputChannel.Reader.TryRead(out output))
                {
                    Engine? engine;
                    if (Engines.TryGetValue(output.Key, out engine))
                    {
                        ParseOutput(engine, output.Value);
                    }
                    else
                    {
                        logger.LogWarning($"engine {output.Key} not found: {output.Value}");
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

    private static void ParseOutput(Engine engine, string output)
    {
        Status status = engine.Status;
        logger.LogDebug($"{status.EngineName} {output}");

        if (output.StartsWith("info "))
        {
            if (output.Contains("evaluation using"))
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
                    Dictionary<string, int> ents = new Dictionary<string, int>();
                    while (m.Success)
                    {
                        ents[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
                        m = m.NextMatch();
                    }
                    if (ents.ContainsKey("multipv"))
                    {
                        var info = status.GetPv(ents["multipv"]);
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
        else if (output.StartsWith("bestmove "))
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
        else if (output.StartsWith("option"))
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
                logger.LogWarning($"{status.EngineName} could not identify option: {output}");
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
                    min = int.Parse(minMatch.Groups[1].Value);
                }
                Match maxMatch = optionMaxRx.Match(output);
                if (maxMatch.Success)
                {
                    max = int.Parse(maxMatch.Groups[1].Value);
                }
                Match defaultMatch = optionDefaultRx.Match(output);
                {
                    defaultValue = int.Parse(defaultMatch.Groups[1].Value);
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
            status.Options.Add(new Option(name, type, defaultValue, vars, min, max));
        }
    }
}
