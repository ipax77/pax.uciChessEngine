
using System.Text.RegularExpressions;

namespace pax.uciChessEngine;

public static partial class UciParser
{
    public static ParseResult? ParseLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        if (line.Equals("readyok", StringComparison.Ordinal))
        {
            return new ParseReadyResult()
            {
                RawLine = line
            };
        }

        var inforesult = ParseInfoLine(line);
        if (inforesult is not null)
        {
            return inforesult;
        }

        var currmoveresult = ParseCurrMoveLine(line);
        if (currmoveresult is not null)
        {
            return currmoveresult;
        }

        var bestmoveresult = ParseBestMoveLine(line);
        if (bestmoveresult is not null)
        {
            return bestmoveresult;
        }

        var optionresult = ParseOption(line);
        if (optionresult is not null)
        {
            return optionresult;
        }

        return new()
        {
            RawLine = line,
            Error = "Line does not match any known format."
        };
    }

    private static ParseOptionResult? ParseOption(string optionLine)
    {

        Match match = OptionRx().Match(optionLine);

        if (match.Success)
        {
            return new()
            {
                OptionName = match.Groups["Name"].Value,
                OptionType = match.Groups["Type"].Value,
                OptionDefault = match.Groups["Default"].Value,
                OptionMin = match.Groups["Min"].Value,
                OptionMax = match.Groups["Max"].Value,
                RawLine = optionLine
            };
        }
        return null;
    }

    private static ParseInfoResult? ParseInfoLine(string infoLine)
    {
        Match match = InfoRx().Match(infoLine);

        if (match.Success)
        {
            var scoreType = match.Groups["ScoreType"].Value;
            return new()
            {
                Depth = int.Parse(match.Groups["Depth"].Value),
                SelDepth = int.Parse(match.Groups["SelDepth"].Value),
                MultiPv = int.Parse(match.Groups["MultiPv"].Value),
                ScoreCp = scoreType == "cp" ? int.Parse(match.Groups["Score"].Value) : 0,
                ScoreMate = scoreType == "mate" ? int.Parse(match.Groups["Score"].Value) : 0,
                Nodes = int.Parse(match.Groups["Nodes"].Value),
                Nps = int.Parse(match.Groups["Nps"].Value),
                TbHits = int.Parse(match.Groups["TbHits"].Value),
                Time = int.Parse(match.Groups["Time"].Value),
                Pv = match.Groups["Pv"].Value,
                RawLine = infoLine
            };
        }
        return null;
    }

    private static ParseCurrMoveResult? ParseCurrMoveLine(string currMoveLine)
    {
        Match match = CurrMoveRx().Match(currMoveLine);

        if (match.Success)
        {
            return new()
            {
                Depth = int.Parse(match.Groups["Depth"].Value),
                CurrMove = match.Groups["CurrMove"].Value,
                CurrMoveNumber = int.Parse(match.Groups["CurrMoveNumber"].Value),
                RawLine = currMoveLine
            };
        }
        return null;
    }

    private static ParseBestMoveResult? ParseBestMoveLine(string bestMoveLine)
    {
        Match match = BestMoveRx().Match(bestMoveLine);

        if (match.Success)
        {
            return new()
            {
                BestMove = match.Groups["BestMove"].Value,
                PonderMove = match.Groups["PonderMove"].Value,
                RawLine = bestMoveLine
            };
        }
        return null;
    }

    [GeneratedRegex(@"option name (?<Name>\w+) type (?<Type>\w+)( default (?<Default>[\w.-]+))?( min (?<Min>[\w.-]+))?( max (?<Max>[\w.-]+))?")]
    private static partial Regex OptionRx();
    // [GeneratedRegex(@"info depth (?<Depth>\d+) seldepth (?<SelDepth>\d+) multipv (?<MultiPv>\d+) score (?<ScoreType>[cp|mate]) (?<Score>[-\d]+) nodes (?<Nodes>\d+) nps (?<Nps>\d+) tbhits (?<TbHits>\d+) time (?<Time>\d+) pv (?<Pv>.+)")]
    [GeneratedRegex(@"info depth (?<Depth>\d+) seldepth (?<SelDepth>\d+) multipv (?<MultiPv>\d+) score (?<ScoreType>(?:cp|mate)) (?<Score>[-\d]+) nodes (?<Nodes>\d+) nps (?<Nps>\d+) tbhits (?<TbHits>\d+) time (?<Time>\d+) pv (?<Pv>.+)")]
    private static partial Regex InfoRx();
    [GeneratedRegex(@"info depth (?<Depth>\d+) currmove (?<CurrMove>\w+) currmovenumber (?<CurrMoveNumber>\d+)")]
    private static partial Regex CurrMoveRx();
    [GeneratedRegex(@"bestmove (?<BestMove>\w+)( ponder (?<PonderMove>\w+))?")]
    private static partial Regex BestMoveRx();
}

public record ParseResult
{
    public string? RawLine { get; init; }
    public string? Error { get; init; }
}

public record ParseReadyResult : ParseResult
{

}

public record ParseCurrMoveResult : ParseResult
{
    public int Depth { get; init; }
    public string? CurrMove { get; init; }
    public int CurrMoveNumber { get; init; }
}

public record ParseInfoResult : ParseResult
{
    public int Depth { get; init; }
    public int SelDepth { get; init; }
    public int MultiPv { get; init; }
    public int ScoreCp { get; init; }
    public int ScoreMate { get; init; }
    public int Nodes { get; init; }
    public int Nps { get; init; }
    public int TbHits { get; init; }
    public int Time { get; init; }
    public string? Pv { get; init; }
}

public record ParseBestMoveResult : ParseResult
{
    public string? BestMove { get; init; }
    public string? PonderMove { get; init; }
    public EngineScore? EngineScore { get; set; }
}

public record ParseOptionResult : ParseResult
{
    public string? OptionName { get; init; }
    public string? OptionType { get; init; }
    public string? OptionDefault { get; init; }
    public string? OptionMin { get; init; }
    public string? OptionMax { get; init; }
    public object? CustomValue { get; set; }
}