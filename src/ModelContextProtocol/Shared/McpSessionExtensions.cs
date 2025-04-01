using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Shared;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol;

/// <summary>Provides extension methods for interacting with an <see cref="IMcpSession"/>.</summary>
public static class McpEndpointExtensions
{
    /// <summary>
    /// Notifies the connected endpoint of an event.
    /// </summary>
    /// <param name="endpoint">The endpoint issuing the notification.</param>
    /// <param name="notification">The notification to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    /// <returns>A task representing the completion of the operation.</returns>
    public static Task NotifyAsync(
        this IMcpSession endpoint,
        JsonRpcNotification notification,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(endpoint);

        return endpoint.SendMessageAsync(notification, cancellationToken);
    }
    
    /// <summary>
    /// Notifies the connected endpoint of an event.
    /// </summary>
    /// <param name="endpoint">The endpoint issuing the notification.</param>
    /// <param name="method">The method to call.</param>
    /// <param name="parameters">The parameters to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    /// <returns>A task representing the completion of the operation.</returns>
    public static Task NotifyAsync(
        this IMcpSession endpoint,
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(endpoint);

        return endpoint.NotifyAsync(new()
        {
            Method = method,
            Params = parameters,
        }, cancellationToken);
    }

    /// <summary>Notifies the connected endpoint of progress.</summary>
    /// <param name="endpoint">The endpoint issuing the notification.</param>
    /// <param name="progressToken">The <see cref="ProgressToken"/> identifying the operation.</param>
    /// <param name="progress">The progress update to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the completion of the operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    public static Task NotifyProgressAsync(
        this IMcpSession endpoint,
        ProgressToken progressToken,
        ProgressNotificationValue progress, 
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(endpoint);

        return endpoint.NotifyAsync(
            NotificationMethods.ProgressNotification,
            new ProgressNotification()
            {
                ProgressToken = progressToken,
                Progress = progress,
            }, cancellationToken);
    }

    /// <summary>
    /// Notifies the connected endpoint that a request has been cancelled.
    /// </summary>
    /// <param name="endpoint">The endpoint issuing the notification.</param>
    /// <param name="requestId">The ID of the request to cancel.</param>
    /// <param name="reason">An optional reason for the cancellation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the completion of the operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    public static Task NotifyCancelAsync(
        this IMcpSession endpoint,
        RequestId requestId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(endpoint);

        return endpoint.NotifyAsync(
            NotificationMethods.CancelledNotification,
            new CancelledNotification()
            {
                RequestId = requestId,
                Reason = reason,
            }, cancellationToken);
    }
}
