using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol;

/// <summary>Provides extension methods for interacting with an <see cref="IMcpEndpoint"/>.</summary>
public static class McpEndpointExtensions
{
    /// <summary>Notifies the connected endpoint of progress.</summary>
    /// <param name="endpoint">The endpoint issueing the notification.</param>
    /// <param name="progressToken">The <see cref="ProgressToken"/> identifying the operation.</param>
    /// <param name="progress">The progress update to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the completion of the operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    public static Task NotifyProgressAsync(
        this IMcpEndpoint endpoint,
        ProgressToken progressToken,
        ProgressNotificationValue progress, 
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(endpoint);

        return endpoint.SendMessageAsync(new JsonRpcNotification()
        {
            Method = NotificationMethods.ProgressNotification,
            Params = new ProgressNotification()
            {
                ProgressToken = progressToken,
                Progress = progress,
            },
        }, cancellationToken);
    }
}
