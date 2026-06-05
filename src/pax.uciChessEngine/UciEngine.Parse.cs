
using System.Globalization;

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
            return;
        }

        if (msg.Equals("uciok", StringComparison.Ordinal))
        {
            _status.EngineState = EngineState.Ready;
            _uciOkTcs?.TrySetResult(true);
            return;
        }

        if (msg.StartsWith("id name", StringComparison.Ordinal))
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
            _bestMoveTcs?.TrySetResult(_status.BestMove);
            if (MoveReady is not null)
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
        var remaining = msg.AsSpan("bestmove ".Length);
        if (!TryReadToken(ref remaining, out var best))
            return;

        status.BestMove = best.Equals("(none)", StringComparison.Ordinal)
            ? null
            : GetStatusString(status.BestMove, best);

        string? ponderMove = null;
        if (TryReadToken(ref remaining, out var ponderLabel)
            && ponderLabel.Equals("ponder", StringComparison.Ordinal)
            && TryReadToken(ref remaining, out var ponder))
        {
            ponderMove = GetStatusString(status.Ponder, ponder);
        }

        status.Ponder = ponderMove;

        status.EngineState = EngineState.BestMove;
    }

    private static void ProcessInfoOutput(string msg, Status status)
    {
        if (msg.Contains("evaluation using", StringComparison.Ordinal))
        {
            status.EngineState = EngineState.Evaluating;
        }
        else
        {
            var pvStart = msg.AsSpan().IndexOf(" pv ", StringComparison.Ordinal);
            if (pvStart >= 0)
            {
                var fields = ParseInfoFields(msg.AsSpan(0, pvStart));
                var info = status.GetPv(fields.MultiPv);
                var movesStart = pvStart + " pv ".Length;
                info.SetInfo(
                    fields.Depth,
                    fields.SelDepth,
                    fields.Centipawns,
                    fields.Mate,
                    fields.Nodes,
                    fields.Nps,
                    fields.HashFull,
                    fields.TbHits,
                    fields.Time,
                    msg,
                    movesStart,
                    msg.Length - movesStart);
            }
        }
        status.EngineState = EngineState.Calculating;
    }

    private static InfoFields ParseInfoFields(ReadOnlySpan<char> input)
    {
        var fields = new InfoFields();
        while (TryReadToken(ref input, out var key))
        {
            if (key.Equals("info", StringComparison.Ordinal))
                continue;

            if (key.Equals("score", StringComparison.Ordinal))
            {
                if (TryReadToken(ref input, out var scoreType)
                    && TryReadInt(ref input, out var scoreValue))
                {
                    if (scoreType.Equals("cp", StringComparison.Ordinal))
                        fields.Centipawns = scoreValue;
                    else if (scoreType.Equals("mate", StringComparison.Ordinal))
                        fields.Mate = scoreValue;
                }

                continue;
            }

            if (!TryReadInt(ref input, out var value))
                continue;

            if (key.Equals("depth", StringComparison.Ordinal))
                fields.Depth = value;
            else if (key.Equals("seldepth", StringComparison.Ordinal))
                fields.SelDepth = value;
            else if (key.Equals("multipv", StringComparison.Ordinal))
                fields.MultiPv = value;
            else if (key.Equals("nodes", StringComparison.Ordinal))
                fields.Nodes = value;
            else if (key.Equals("nps", StringComparison.Ordinal))
                fields.Nps = value;
            else if (key.Equals("hashfull", StringComparison.Ordinal))
                fields.HashFull = value;
            else if (key.Equals("tbhits", StringComparison.Ordinal))
                fields.TbHits = value;
            else if (key.Equals("time", StringComparison.Ordinal))
                fields.Time = value;
        }

        return fields;
    }

    private static void UpdateOption(string msg, Status status)
    {
        if (!TryParseOption(msg, out var option))
            return;

        var existing = status.EngineOptions.FirstOrDefault(f => f.Name == option.Name);
        if (existing is null)
            status.EngineOptions = [.. status.EngineOptions, option];
        else
            existing.Udpate(option);
    }

    private static bool TryParseOption(string msg, out EngineOption option)
    {
        option = default!;
        const string NamePrefix = "option name ";
        if (!msg.StartsWith(NamePrefix, StringComparison.Ordinal))
            return false;

        var rest = msg.AsSpan(NamePrefix.Length);
        var typeMarker = rest.IndexOf(" type ", StringComparison.Ordinal);
        if (typeMarker < 0)
            return false;

        var name = rest[..typeMarker].ToString();
        rest = rest[(typeMarker + " type ".Length)..];

        if (!TryReadToken(ref rest, out var typeToken))
            return false;

        var type = typeToken.ToString();
        var min = FindIntValue(msg, " min ");
        var max = FindIntValue(msg, " max ");
        var vars = typeToken.Equals("combo", StringComparison.Ordinal)
            ? FindValues(msg, " var ")
            : null;

        option = new EngineOption(
            name,
            type,
            ExtractDefaultValue(msg, typeToken, min, max, vars),
            vars,
            min ?? 0,
            max ?? 0);

        return true;
    }

    private static object ExtractDefaultValue(
        string output,
        ReadOnlySpan<char> type,
        int? min,
        int? max,
        List<string>? vars)
    {
        var defaultValue = FindValue(output, " default ");

        if (type.Equals("spin", StringComparison.Ordinal))
        {
            return new
            {
                Min = min ?? 0,
                Max = max ?? 0,
                DefaultValue = ParseInt(defaultValue)
            };
        }

        if (type.Equals("string", StringComparison.Ordinal))
            return defaultValue.Equals("<empty>", StringComparison.Ordinal)
                ? string.Empty
                : defaultValue.ToString();

        if (type.Equals("check", StringComparison.Ordinal))
            return bool.Parse(defaultValue);

        if (type.Equals("combo", StringComparison.Ordinal))
        {
            return new
            {
                DefaultValue = defaultValue.ToString(),
                Vars = vars ?? []
            };
        }

        return string.Empty;
    }

    private static ReadOnlySpan<char> FindValue(string input, string marker)
    {
        var span = input.AsSpan();
        var index = span.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return [];

        span = span[(index + marker.Length)..].TrimStart();
        if (span.IsEmpty)
            return [];

        var end = span.IndexOf(' ');
        return end < 0 ? span : span[..end];
    }

    private static int? FindIntValue(string input, string marker)
    {
        var value = FindValue(input, marker);
        return value.IsEmpty ? null : ParseInt(value);
    }

    private static List<string> FindValues(string input, string marker)
    {
        List<string> values = [];
        var span = input.AsSpan();
        var offset = 0;
        while (offset < span.Length)
        {
            var index = span[offset..].IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
                break;

            var rest = span[(offset + index + marker.Length)..];
            if (TryReadToken(ref rest, out var value))
                values.Add(value.ToString());

            offset += index + marker.Length + value.Length;
        }

        return values;
    }

    private static bool TryReadInt(ref ReadOnlySpan<char> input, out int value)
    {
        if (!TryReadToken(ref input, out var token))
        {
            value = 0;
            return false;
        }

        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static int ParseInt(ReadOnlySpan<char> value)
        => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static string? GetStatusString(string? current, ReadOnlySpan<char> value)
        => current.AsSpan().SequenceEqual(value)
            ? current
            : value.ToString();

    private static bool TryReadToken(ref ReadOnlySpan<char> input, out ReadOnlySpan<char> token)
    {
        input = input.TrimStart();
        if (input.IsEmpty)
        {
            token = [];
            return false;
        }

        var end = input.IndexOf(' ');
        if (end < 0)
        {
            token = input;
            input = [];
            return true;
        }

        token = input[..end];
        input = input[(end + 1)..];
        return true;
    }

    private struct InfoFields
    {
        public int MultiPv { get; set; } = 1;
        public int Depth { get; set; }
        public int SelDepth { get; set; }
        public int? Centipawns { get; set; }
        public int? Mate { get; set; }
        public int Nodes { get; set; }
        public int Nps { get; set; }
        public int HashFull { get; set; }
        public int TbHits { get; set; }
        public int Time { get; set; }

        public InfoFields()
        {
        }
    }
}
