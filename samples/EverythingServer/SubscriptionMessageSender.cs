using ModelContextProtocol;
using ModelContextProtocol.Server;

using System.Collections.Concurrent;
internal class SubscriptionMessageSender() : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the application to fully start before trying to access the MCP server
        await Task.Delay(2000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var (uri, servers) in SubscriptionManager.GetSubscriptions())
                {
                    foreach (var server in servers)
                    {
                        await server.SendNotificationAsync("notifications/resource/updated",
                            new
                            {
                                Uri = uri,
                            }, cancellationToken: stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception but don't crash the service
                Console.WriteLine($"Error in SubscriptionMessageSender: {ex.Message}");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
}
