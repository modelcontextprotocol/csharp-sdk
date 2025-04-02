using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Server;

internal class SubscriptionMessageSender(IMcpServer server, HashSet<string> subscriptions) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var uri in subscriptions)
            {
                await server.SendNotificationAsync("notifications/resource/updated",
                    new
                    {
                        Uri = uri,
                    }, cancellationToken: cancellationToken);
            }

            await Task.Delay(5000, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
