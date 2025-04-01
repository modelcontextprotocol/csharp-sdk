using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;

namespace EverythingServer;

public class LoggingUpdateMessageSender(IMcpServer server) : IHostedService
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var currentLevel = server.Services!.GetRequiredService<Func<LoggingLevel>>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var newLevel = (LoggingLevel)Enum.GetValues<LoggingLevel>().GetValue(new Random().Next(0, Enum.GetValues(typeof(LoggingLevel)).Length))!;

            var message = new JsonRpcNotification
            {
                Method = "notifications/message",
                Params = new
                {
                    Level = newLevel.ToString().ToLower(),
                    Data = _loggingLevelMap[newLevel],
                }
            };

            if (newLevel > currentLevel())
            {
                await server.SendMessageAsync(message, cancellationToken);
            }

            await Task.Delay(15000, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}