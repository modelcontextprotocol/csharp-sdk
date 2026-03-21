using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a continuation for a suspended MRTR handler, stored between round trips.
/// </summary>
internal sealed class MrtrContinuation
{
    public MrtrContinuation(Task<JsonNode?> handlerTask, MrtrContext mrtrContext, MrtrExchange pendingExchange)
    {
        HandlerTask = handlerTask;
        MrtrContext = mrtrContext;
        PendingExchange = pendingExchange;
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
}
