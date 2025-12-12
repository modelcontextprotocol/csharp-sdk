using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Logging.Tools;

[McpServerToolType]
public class LoggingTools
{
    [McpServerTool, Description("Demonstrates a tool that produces log messages")]
    public static async Task<string> LoggingTool(
        RequestContext<CallToolRequestParams> context,
        int duration = 10,
        int steps = 10)
    {
        var progressToken = context.Params?.ProgressToken;
        var stepDuration = duration / steps;

        // <snippet_LoggingConfiguration >
        ILoggerProvider loggerProvider = context.Server.AsClientLoggerProvider();
        ILogger logger = loggerProvider.CreateLogger("LoggingTools");
        // </snippet_LoggingConfiguration>

        for (int i = 1; i <= steps; i++)
        {
            await Task.Delay(stepDuration * 1000);

            try
            {
                logger.LogCritical("A critical log message");
                logger.LogError("An error log message");
                logger.LogWarning("A warning log message");
                logger.LogInformation("An informational log message");
                logger.LogDebug("A debug log message");
                logger.LogTrace("A trace log message");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while logging messages");
            }
        }

        return $"Long running tool completed. Duration: {duration} seconds. Steps: {steps}.";
    }
}
