namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides constants for well-known <c>_meta</c> field keys defined by the MCP protocol and its extensions.
/// </summary>
public static class MetaKeys
{
    /// <summary>
    /// The metadata key used to carry the MCP protocol version in a request's <c>_meta</c> field.
    /// </summary>
    /// <remarks>
    /// Introduced by the 2026-07-28 protocol revision (SEP-2575). For HTTP transports, the value MUST
    /// match the <c>MCP-Protocol-Version</c> header. Servers reject a header/body mismatch with
    /// <see cref="McpErrorCode.HeaderMismatch"/>.
    /// </remarks>
    public const string ProtocolVersion = "io.modelcontextprotocol/protocolVersion";

    /// <summary>
    /// The metadata key used to identify the client software in a request's <c>_meta</c> field.
    /// </summary>
    /// <remarks>
    /// Introduced by the 2026-07-28 protocol revision (SEP-2575). Carries an <see cref="Protocol.Implementation"/>
    /// describing the client; replaces the <c>clientInfo</c> previously sent only with <c>initialize</c>.
    /// </remarks>
    public const string ClientInfo = "io.modelcontextprotocol/clientInfo";

    /// <summary>
    /// The metadata key used to declare client capabilities in a request's <c>_meta</c> field.
    /// </summary>
    /// <remarks>
    /// Introduced by the 2026-07-28 protocol revision (SEP-2575). Carries a <see cref="Protocol.ClientCapabilities"/>
    /// describing what optional features the client supports for this specific request. Servers MUST NOT
    /// infer capabilities from previous requests.
    /// </remarks>
    public const string ClientCapabilities = "io.modelcontextprotocol/clientCapabilities";

    /// <summary>
    /// The metadata key used to specify the desired log level for a request's resulting log notifications.
    /// </summary>
    /// <remarks>
    /// Introduced by the 2026-07-28 protocol revision (SEP-2575). Carries a <see cref="Protocol.LoggingLevel"/>.
    /// Replaces the legacy <see cref="RequestMethods.LoggingSetLevel"/> RPC. When absent, the server
    /// MUST NOT send log notifications for the request.
    /// </remarks>
    public const string LogLevel = "io.modelcontextprotocol/logLevel";

    /// <summary>
    /// The metadata key used to associate a notification with the request ID of an active
    /// <see cref="RequestMethods.SubscriptionsListen"/> subscription.
    /// </summary>
    /// <remarks>
    /// Introduced by the 2026-07-28 protocol revision (SEP-2575). Allows clients to demultiplex notifications
    /// belonging to different subscriptions on a shared channel (especially STDIO).
    /// </remarks>
    public const string SubscriptionId = "io.modelcontextprotocol/subscriptionId";

    /// <summary>
    /// The metadata key used to associate requests, responses, and notifications with a task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This constant defines the key <c>"io.modelcontextprotocol/related-task"</c> used in the
    /// <c>_meta</c> field to associate messages with their originating task across the entire
    /// request lifecycle.
    /// </para>
    /// <para>
    /// For example, an elicitation that a task-augmented tool call depends on must share the
    /// same related task ID with that tool call's task.
    /// </para>
    /// <para>
    /// For <c>tasks/get</c>, <c>tasks/list</c>, and <c>tasks/cancel</c> operations, this
    /// metadata should not be included as the taskId is already present in the message structure.
    /// </para>
    /// </remarks>
    public const string RelatedTask = "io.modelcontextprotocol/related-task";
}
