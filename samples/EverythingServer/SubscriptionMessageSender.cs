using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Server;

internal class SubscriptionMessageSender(McpServer server, HashSet<string> subscriptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var uri in subscriptions)
            {
                await server.SendNotificationAsync("notifications/resource/updated",
                    new
                    {
                        Uri = uri,
                    }, cancellationToken: stoppingToken);
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
}
