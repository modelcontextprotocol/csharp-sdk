using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Logging;

internal class McpLogger(string categoryName, IMcpServer mcpServer) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => default;

    public bool IsEnabled(LogLevel logLevel)
        => logLevel.ToLoggingLevel() <= mcpServer.LoggingLevel;

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
    public async void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message))
            return;

        // Use JsonSerializer to properly escape the string for JSON and turn it into a JsonElement
        var json = JsonSerializer.Serialize(message);
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        await mcpServer.SendLogNotificationAsync(new LoggingMessageNotificationParams
        {
            Data = element,
            Level = logLevel.ToLoggingLevel(),
            Logger = categoryName
        });
    }
}
