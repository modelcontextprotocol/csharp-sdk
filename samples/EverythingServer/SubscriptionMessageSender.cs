using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol.Messages;

internal class SubscriptionMessageSender(IMcpServer server, HashSet<string> subscriptions) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var uri in subscriptions)
            {
                await server.SendMessageAsync(new JsonRpcNotification
                {
                    Method = "notifications/resource/updated",
                    Params = new ResourceUpdatedNotificationParams
                    {
                        Uri = uri,
                    }
                }, cancellationToken);
            }

            await Task.Delay(5000, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
