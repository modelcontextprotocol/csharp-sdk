using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol;

/// <summary>
/// Provides an <see cref="IProgress{ProgressNotificationValue}"/> tied to a specific progress token and that will issue
/// progress notifications on the supplied endpoint.
/// </summary>
internal sealed class TokenProgress(IMcpEndpoint endpoint, ProgressToken progressToken) : IProgress<ProgressNotificationValue>
{
    /// <inheritdoc />
    public void Report(ProgressNotificationValue value)
    {
        _ = endpoint.NotifyProgressAsync(progressToken, value, CancellationToken.None);
    }
}
