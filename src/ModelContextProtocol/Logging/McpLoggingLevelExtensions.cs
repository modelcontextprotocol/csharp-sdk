using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Logging;

    internal static class McpLoggingLevelExtensions
{
    public static LogLevel ToLogLevel(this LoggingLevel level)
        => level switch
        {
            LoggingLevel.Emergency or LoggingLevel.Alert or LoggingLevel.Critical => LogLevel.Critical,
            LoggingLevel.Error => LogLevel.Error,
            LoggingLevel.Warning => LogLevel.Warning,
            LoggingLevel.Notice or LoggingLevel.Info => LogLevel.Information,
            LoggingLevel.Debug => LogLevel.Debug,
            _ => LogLevel.None,
        };

    public static LoggingLevel ToLoggingLevel(this LogLevel level)
        => level switch
        {
            LogLevel.Critical => LoggingLevel.Critical,
            LogLevel.Error => LoggingLevel.Error,
            LogLevel.Warning => LoggingLevel.Warning,
            LogLevel.Information => LoggingLevel.Info,
            LogLevel.Debug or LogLevel.Trace => LoggingLevel.Debug,
            _ => LoggingLevel.Info,
        };
}
