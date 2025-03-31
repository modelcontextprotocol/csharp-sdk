using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using ModelContextProtocol.Shared;

namespace ModelContextProtocol;

/// <summary>
/// Provides an <see cref="IProgress{ProgressNotificationValue}"/> tied to a specific progress token and that will issue
/// progress notifications to the supplied endpoint.
/// </summary>
internal sealed class TokenProgress(IMcpServer server, ProgressToken progressToken) : IProgress<ProgressNotificationValue>
{
    /// <inheritdoc />
    public void Report(ProgressNotificationValue value)
    {
        _ = server.SendMessageAsync(new JsonRpcNotification()
        {
            Method = NotificationMethods.ProgressNotification,
            Params = new ProgressNotification()
            {
                ProgressToken = progressToken,
                Progress = new()
                {
                    Progress = value.Progress,
                    Total = value.Total,
                    Message = value.Message,
                },
            },
        }, CancellationToken.None);
    }
}
