using Microsoft.Extensions.Logging;

namespace pax.uciChessEngine;

public static class LoggerExtensions
{
    private static readonly Action<ILogger, string, Exception?> _engineStarted = LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(2, nameof(EngineStarted)),
            "Engine start (Engine = '{EngineStarted}')");

    public static void EngineStarted(this ILogger logger, string engineString)
    {
        _engineStarted(logger, engineString, null);
    }

    private static readonly Action<ILogger, string, Exception?> _engineError = LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(99, nameof(EngineError)),
            "Engine error (Error = '{EngineError}')");

    public static void EngineError(this ILogger logger, string engineError)
    {
        _engineError(logger, engineError, null);
    }

    private static readonly Action<ILogger, string, Exception?> _engineWarning = LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(98, nameof(EngineWarning)),
            "Engine warning (Warning = '{EngineWarning}')");

    public static void EngineWarning(this ILogger logger, string engineWarning)
    {
        _engineWarning(logger, engineWarning, null);
    }

    private static readonly Action<ILogger, string, Exception?> _enginePing = LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(97, nameof(EnginePing)),
            "Engine ping (Ping = '{EnginePing}')");

    public static void EnginePing(this ILogger logger, string enginePing)
    {
        _enginePing(logger, enginePing, null);
    }

    private static readonly Action<ILogger, string, Exception?> _enginePong = LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(96, nameof(EnginePong)),
            "Engine Pong (Pong = '{EnginePong}')");

    public static void EnginePong(this ILogger logger, string enginePong)
    {
        _enginePong(logger, enginePong, null);
    }
}
