using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a continuation for a suspended MRTR handler, stored between round trips.
/// </summary>
internal sealed class MrtrContinuation
{
    public MrtrContinuation(Task<JsonNode?> handlerTask, MrtrContext mrtrContext, MrtrExchange pendingExchange, CancellationTokenSource handlerCts)
    {
        HandlerTask = handlerTask;
        MrtrContext = mrtrContext;
        PendingExchange = pendingExchange;
        HandlerCts = handlerCts;
    }

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
    /// </summary>
    public MrtrExchange PendingExchange { get; }

    /// <summary>
    /// The long-lived CTS that controls the handler's cancellation across retries.
    /// Linked to the original request's token at creation. Each retry links its own
    /// cancellation to this CTS via a registration.
    /// </summary>
    public CancellationTokenSource HandlerCts { get; }
}
