using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents the lifecycle state for an MRTR handler invocation across retries.
/// Created when the handler starts and stored in <c>_mrtrContinuations</c> when
/// the handler suspends waiting for client input.
/// </summary>
internal sealed class MrtrContinuation
{
    private readonly CancellationTokenSource _handlerCts;

    public MrtrContinuation(CancellationTokenSource handlerCts, Task<JsonNode?> handlerTask, MrtrContext mrtrContext)
    {
        _handlerCts = handlerCts;
        HandlerTask = handlerTask;
        MrtrContext = mrtrContext;
    }

    /// <summary>
    /// Gets a token that cancels when the handler should be aborted.
    /// Passed to the handler at creation and remains valid across retries.
    /// </summary>
    public CancellationToken HandlerToken => _handlerCts.Token;

    /// <summary>
    /// The handler task that is suspended awaiting input.
    /// </summary>
    public Task<JsonNode?> HandlerTask { get; }

    /// <summary>
    /// The MRTR context for the handler's async flow.
    /// </summary>
    public MrtrContext MrtrContext { get; }

    /// <summary>
    /// The exchange that is awaiting a response from the client.
    /// Set each time the handler suspends on a new exchange.
    /// </summary>
    public MrtrExchange? PendingExchange { get; set; }

    /// <summary>
    /// Cancels the handler. Safe to call multiple times and concurrently —
    /// <see cref="CancellationTokenSource.Cancel()"/> is thread-safe with itself.
    /// The CTS is intentionally never disposed to avoid deadlock risks from
    /// calling Cancel/Dispose inside synchronization primitives.
    /// </summary>
    public void CancelHandler() => _handlerCts.Cancel();
}
