
using System.Globalization;
using System.Text.RegularExpressions;

namespace pax.uciChessEngine;

public sealed partial class UciEngine
{
    public void TestParseUciString(string msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        ParseUciString(msg);    
    }

    private void ParseUciString(string msg)
    {
        if (msg.Equals("readyok", StringComparison.Ordinal))
        {
            _readyOkTcs?.TrySetResult(true);
            _status.EngineState = EngineState.Ready;

        }

        if (msg.Equals("uciok", StringComparison.Ordinal))
        {
            _status.EngineState = EngineState.Ready;
            _uciOkTcs?.TrySetResult(true);
        }

        else if (msg.StartsWith("id name", StringComparison.Ordinal))
            _status.Name = msg[8..];

        else if (msg.StartsWith("id author", StringComparison.Ordinal))
            _status.Author = msg[10..];

        else if (msg.StartsWith("option name", StringComparison.Ordinal))
            UpdateOption(msg, _status);

        else if (msg.StartsWith("info ", StringComparison.Ordinal))
            ProcessInfoOutput(msg, _status);

        else if (msg.StartsWith("bestmove ", StringComparison.Ordinal))
        {
            ProcessBestMove(msg, _status);
            _bestMoveTcs?.TrySetResult(_status.BestMove ?? msg);
            OnMoveReady(new() { Move = _status.BestMove });
        }

        else if (msg.StartsWith("error ", StringComparison.Ordinal))
        {
            _status.Error = msg;
            OnErrorRaised(new() { Error = msg });
        }
    }

    private static void ProcessBestMove(string msg, Status status)
    {
        var match = BestMoveGrx().Match(msg);
        status.BestMove = match.Success ? match.Groups[1].Value : null;

        status.Ponder = match.Success ? match.Groups[2].Value : null;

        if (!string.IsNullOrEmpty(status.BestMove))
        {
            status.EngineState = EngineState.BestMove;
        }
    }

    private static void ProcessInfoOutput(string msg, Status status)
    {
        if (msg.Contains("evaluation using", StringComparison.Ordinal))
        {
            status.EngineState = EngineState.Evaluating;
        }
        else
        {
            var infos = msg.Split(" pv ", 2);
            if (infos.Length == 2)
            {
                var ents = ExtractKeyValuePairs(infos[0]);
                int multipv = ents.GetValueOrDefault("multipv", 1);
                var info = status.GetPv(multipv);
                info.SetValues(ents);
                info.SetMoves(infos[1].Split(' ').ToList());
            }
        }
        status.EngineState = EngineState.Calculating;
    }

    private static Dictionary<string, int> ExtractKeyValuePairs(string input)
    {
        var ents = new Dictionary<string, int>();
        Match match = ValueGrx().Match(input);
        while (match.Success)
        {
            ents[match.Groups[1].Value] = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            match = match.NextMatch();
        }
        return ents;
    }

    private static void UpdateOption(string msg, Status status)
    {
        Match nameMatch = OptionTypeGrx().Match(msg);
        if (!nameMatch.Success)
        {
            return;
        }

        string name = nameMatch.Groups[1].Value;

        string type = OptionNameGrx().Match(msg).Groups[1].Value;
        object defaultValue = ExtractDefaultValue(msg, type, out int min, out int max, out List<string>? vars);

        var option = new EngineOption(name, type, defaultValue, vars, min, max);

        var existing = status.EngineOptions.FirstOrDefault(f => f.Name == option.Name);
        if (existing is null)
            status.EngineOptions = [.. status.EngineOptions, option];
        else
            existing.Udpate(option);
    }

    private static object ExtractDefaultValue(string output, string type, out int min, out int max, out List<string>? vars)
    {
        min = max = 0;
        vars = null;
        return type switch
        {
            "spin" => new
            {
                Min = int.Parse(OptionMinGrx().Match(output).Groups[1].Value, CultureInfo.InvariantCulture),
                Max = int.Parse(OptionMaxGrx().Match(output).Groups[1].Value, CultureInfo.InvariantCulture),
                DefaultValue = int.Parse(OptionDefaultGrx().Match(output).Groups[1].Value, CultureInfo.InvariantCulture)
            },
            "string" => OptionDefaultGrx().Match(output).Groups[1].Value switch
            {
                "<empty>" => string.Empty,
                var value => value
            },
            "check" => bool.Parse(OptionDefaultGrx().Match(output).Groups[1].Value),
            "combo" => new
            {
                DefaultValue = OptionDefaultGrx().Match(output).Groups[1].Value,
                Vars = OptionComboGrx().Matches(output).Select(m => m.Groups[1].Value).ToList()
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
