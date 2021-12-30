using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.uciChessEngine;

public static class ApplicationLogging
{
    public static ILoggerFactory LogFactory { get; } = LoggerFactory.Create(builder =>
    {
        builder.ClearProviders();
        // Clear Microsoft's default providers (like eventlogs and others)
        builder.AddSimpleConsole(options =>
        {
            options.IncludeScopes = true;
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        }).SetMinimumLevel(LogLevel.Warning);
    });

    public static ILogger<T> CreateLogger<T>() => LogFactory.CreateLogger<T>();
}

public static class ApplicationDebugLogging
{
    public static ILoggerFactory LogFactory { get; } = LoggerFactory.Create(builder =>
    {
        builder.ClearProviders();
        // Clear Microsoft's default providers (like eventlogs and others)
        builder.AddSimpleConsole(options =>
        {
            options.IncludeScopes = true;
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        }).SetMinimumLevel(LogLevel.Debug);
    });

    public static ILogger<T> CreateLogger<T>() => LogFactory.CreateLogger<T>();
}