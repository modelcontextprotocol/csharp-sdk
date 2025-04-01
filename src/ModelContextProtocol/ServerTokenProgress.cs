using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;

namespace ModelContextProtocol;

/// <summary>
/// Provides an <see cref="IProgress{ProgressNotificationValue}"/> tied to a specific progress token and that will issue
/// progress notifications to the supplied endpoint.
/// </summary>
internal sealed class ServerTokenProgress(IMcpServer server, ProgressToken progressToken)
    : IProgress<ProgressNotificationValue>
{
    /// <inheritdoc />
    public void Report(ProgressNotificationValue value)
    {
        _ = server.NotifyProgressAsync(new()
        {
            ProgressToken = progressToken,
            Progress = new()
            {
                Progress = value.Progress,
                Total = value.Total,
                Message = value.Message,
            },
        }, CancellationToken.None);
    }
}
