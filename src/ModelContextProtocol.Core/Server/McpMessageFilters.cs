using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides grouped message filter collections.
/// </summary>
public sealed class McpMessageFilters
{
    /// <summary>
    /// Gets or sets the filters for all incoming JSON-RPC messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These filters intercept all incoming JSON-RPC messages before they are processed by the server,
    /// including requests, notifications, responses, and errors. The filters can perform logging,
    /// authentication, rate limiting, or other cross-cutting concerns that apply to all message types.
    /// </para>
    /// <para>
    /// Message filters are applied before request-specific filters. If a message filter does not call
    /// the next handler in the pipeline, the default handlers will not be executed.
    /// </para>
    /// </remarks>
    public IList<McpMessageFilter> IncomingFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for all outgoing JSON-RPC messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These filters intercept all outgoing JSON-RPC messages before they are sent to the client,
    /// including responses, notifications, and errors. The filters can perform logging,
    /// redaction, auditing, or other cross-cutting concerns that apply to all message types.
    /// </para>
    /// <para>
    /// If a message filter does not call the next handler in the pipeline, the message will not be sent.
    /// Filters may also call the next handler multiple times with different messages to emit additional
    /// server-to-client messages.
    /// </para>
    /// </remarks>
    public IList<McpMessageFilter> OutgoingFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }
}
