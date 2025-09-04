using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EverythingServer;

public class LoggingUpdateMessageSender(IServiceProvider serviceProvider) : BackgroundService
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
        // Wait for the application to fully start before trying to access the MCP server
        await Task.Delay(2000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Try to get the server from the service provider
                var server = serviceProvider.GetService<IMcpServer>();
                if (server != null)
                {
                    var newLevel = (LoggingLevel)Random.Shared.Next(_loggingLevelMap.Count);

                    var message = new
                        {
                            Level = newLevel.ToString().ToLower(),
                            Data = _loggingLevelMap[newLevel],
                        };

                    if (newLevel > server.LoggingLevel)
                    {
                        await server.SendNotificationAsync("notifications/message", message, cancellationToken: stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception but don't crash the service
                Console.WriteLine($"Error in LoggingUpdateMessageSender: {ex.Message}");
            }

            await Task.Delay(15000, stoppingToken);
        }
    }
}