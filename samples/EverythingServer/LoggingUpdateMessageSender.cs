using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EverythingServer;

public class LoggingUpdateMessageSender(McpServer server) : BackgroundService
{
    readonly Dictionary<LoggingLevel, string> _loggingLevelMap = new()
    {
        { LoggingLevel.Debug, "Debug-level message" },
        { LoggingLevel.Info, "Info-level message" },
        { LoggingLevel.Notice, "Notice-level message" },
        { LoggingLevel.Warning, "Warning-level message" },
        { LoggingLevel.Error, "Error-level message" },
        { LoggingLevel.Critical, "Critical-level message" },
        { LoggingLevel.Alert, "Alert-level message" },
        { LoggingLevel.Emergency, "Emergency-level message" }
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var msgLevel = (LoggingLevel)Random.Shared.Next(_loggingLevelMap.Count);

            var message = new
                {
                    Level = msgLevel.ToString().ToLower(),
                    Data = _loggingLevelMap[msgLevel],
                };

            if (msgLevel > server.LoggingLevel)
            {
                await server.SendNotificationAsync("notifications/message", message, cancellationToken: stoppingToken);
            }

            await Task.Delay(15000, stoppingToken);
        }
    }
}