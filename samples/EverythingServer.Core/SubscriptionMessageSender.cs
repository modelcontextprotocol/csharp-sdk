using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EverythingServer.Core;

public class SubscriptionMessageSender(McpServer server, ConcurrentDictionary<string, byte> subscriptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var uri in subscriptions.Keys)
            {
                await server.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification,
                    new
                    {
                        Uri = uri,
                    }, cancellationToken: stoppingToken);
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
}
