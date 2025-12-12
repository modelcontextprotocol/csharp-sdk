using ModelContextProtocol.Protocol;

namespace ModelContextProtocol;

/// <summary>
/// Provides an <see cref="IProgress{ProgressNotificationValue}"/> tied to a specific progress token and that will issue
/// progress notifications on the supplied session.
/// </summary>
internal sealed class TokenProgress(McpSession session, ProgressToken progressToken) : IProgress<ProgressNotificationValue>
{
    /// <inheritdoc />
    public void Report(ProgressNotificationValue value)
    {
        _ = session.NotifyProgressAsync(progressToken, value, cancellationToken: CancellationToken.None);
    }
}
