using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a container for handlers used in the creation of an MCP server.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a centralized collection of delegates that implement various capabilities of the Model Context Protocol.
/// Each handler in this class corresponds to a specific endpoint in the Model Context Protocol and
/// is responsible for processing a particular type of message. The handlers are used to customize
/// the behavior of the MCP server by providing implementations for the various protocol operations.
/// </para>
/// <para>
/// When a client sends a message to the server, the appropriate handler is invoked to process it
/// according to the protocol specification. Which handler is selected
/// is done based on an ordinal, case-sensitive string comparison.
/// </para>
/// </remarks>
public sealed class McpServerHandlers
{
    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ToolsList"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler should return a list of available tools when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more tools.
    /// </para>
    /// <para>
    /// This handler works alongside any tools defined in the <see cref="McpServerTool"/> collection.
    /// Tools from both sources will be combined when returning results to clients.
    /// </para>
    /// </remarks>
    public McpRequestHandler<ListToolsRequestParams, ListToolsResult>? ListToolsHandler { get; set; }

#pragma warning disable MCPEXP002 // CallToolHandler and CallToolWithAlternateHandler reference the experimental ResultOrAlternate seam
    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ToolsCall"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client makes a call to a tool that isn't found in the <see cref="McpServerTool"/> collection.
    /// The handler should implement logic to execute the requested tool and return appropriate results.
    /// Use <see cref="CallToolWithAlternateHandler"/> instead if the tool may return an alternate result
    /// for the caller to handle.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><see cref="CallToolWithAlternateHandler"/> is already set.</exception>
    public McpRequestHandler<CallToolRequestParams, CallToolResult>? CallToolHandler
    {
        get;
        set
        {
            if (value is not null && CallToolWithAlternateHandler is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot set {nameof(CallToolHandler)} when {nameof(CallToolWithAlternateHandler)} is already set. Only one call tool handler may be configured.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ToolsCall"/> requests with alternate result support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler is invoked when a client makes a call to a tool, allowing the tool to return either
    /// a <see cref="CallToolResult"/> for immediate results or an alternate <see cref="Result"/> subtype.
    /// </para>
    /// <para>
    /// This is a low-level full replacement for the ordinary tool-call pipeline. It cannot be set if
    /// <see cref="CallToolHandler"/> is already set, and it cannot be composed with ordinary
    /// <see cref="McpRequestFilters.CallToolFilters"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException"><see cref="CallToolHandler"/> is already set.</exception>
    [Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
    public McpRequestHandler<CallToolRequestParams, ResultOrAlternate<CallToolResult>>? CallToolWithAlternateHandler
    {
        get;
        set
        {
            if (value is not null && CallToolHandler is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot set {nameof(CallToolWithAlternateHandler)} when {nameof(CallToolHandler)} is already set. Only one call tool handler may be configured.");
            }

            field = value;
        }
    }
#pragma warning restore MCPEXP002

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.PromptsList"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler should return a list of available prompts when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more prompts.
    /// </para>
    /// <para>
    /// This handler works alongside any prompts defined in the <see cref="McpServerPrompt"/> collection.
    /// Prompts from both sources will be combined when returning results to clients.
    /// </para>
    /// </remarks>
    public McpRequestHandler<ListPromptsRequestParams, ListPromptsResult>? ListPromptsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.PromptsGet"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client requests details for a specific prompt that isn't found in the <see cref="McpServerPrompt"/> collection.
    /// The handler should implement logic to fetch or generate the requested prompt and return appropriate results.
    /// </remarks>
    public McpRequestHandler<GetPromptRequestParams, GetPromptResult>? GetPromptHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesTemplatesList"/> requests.
    /// </summary>
    /// <remarks>
    /// The handler should return a list of available resource templates when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more resource templates.
    /// </remarks>
    public McpRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult>? ListResourceTemplatesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesList"/> requests.
    /// </summary>
    /// <remarks>
    /// The handler should return a list of available resources when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more resources.
    /// </remarks>
    public McpRequestHandler<ListResourcesRequestParams, ListResourcesResult>? ListResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesRead"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client requests the content of a specific resource identified by its URI.
    /// The handler should implement logic to locate and retrieve the requested resource.
    /// </remarks>
    public McpRequestHandler<ReadResourceRequestParams, ReadResourceResult>? ReadResourceHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.CompletionComplete"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler provides auto-completion suggestions for prompt arguments or resource references in the Model Context Protocol.
    /// The handler processes auto-completion requests, returning a list of suggestions based on the
    /// reference type and current argument value.
    /// </remarks>
    public McpRequestHandler<CompleteRequestParams, CompleteResult>? CompleteHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesSubscribe"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler is invoked when a client wants to receive notifications about changes to specific resources or resource patterns.
    /// The handler should implement logic to register the client's interest in the specified resources
    /// and set up the necessary infrastructure to send notifications when those resources change.
    /// </para>
    /// <para>
    /// After a successful subscription, the server should send resource change notifications to the client
    /// whenever a relevant resource is created, updated, or deleted.
    /// </para>
    /// </remarks>
    public McpRequestHandler<SubscribeRequestParams, EmptyResult>? SubscribeToResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesUnsubscribe"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler is invoked when a client wants to stop receiving notifications about previously subscribed resources.
    /// The handler should implement logic to remove the client's subscriptions to the specified resources
    /// and clean up any associated resources.
    /// </para>
    /// <para>
    /// After a successful unsubscription, the server should no longer send resource change notifications
    /// to the client for the specified resources.
    /// </para>
    /// </remarks>
    public McpRequestHandler<UnsubscribeRequestParams, EmptyResult>? UnsubscribeFromResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.SubscriptionsListen"/> requests (SEP-2575).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>subscriptions/listen</c> is a long-lived request introduced by the 2026-07-28 protocol revision. The
    /// held-open response is a solicited server-to-client stream: the server first acknowledges which
    /// subscriptions it will honor and then streams matching notifications until the request is cancelled.
    /// Setting this handler lets a server author own that stream directly to implement custom subscription
    /// kinds, application-driven <c>resources/updated</c> delivery, or subscriptions backed by their own event
    /// source. It is especially useful for stateless Streamable HTTP, where unsolicited notifications are
    /// dropped (there is no session-wide channel) but the listen request's response stream can still carry
    /// notifications for the duration of the request.
    /// </para>
    /// <para>
    /// This is a <b>full replacement</b> for the built-in <c>subscriptions/listen</c> handler. When set, the
    /// SDK does not track the subscription, does not send the acknowledgement, and does not perform any
    /// automatic <c>*/list_changed</c> fan-out for the request; the handler is solely responsible for the
    /// entire lifetime of the stream. The SDK still enforces protocol-version gating: the handler is only
    /// reached when the negotiated protocol revision is 2026-07-28 or later, and is otherwise rejected with
    /// <see cref="McpErrorCode.MethodNotFound"/>.
    /// </para>
    /// <para>
    /// An implementation of this handler is responsible for:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// Sending exactly one <see cref="NotificationMethods.SubscriptionsAcknowledgedNotification"/> before any
    /// subscription events, reporting only the filters it actually honors. Advertised server capabilities must
    /// match what the handler will actually deliver.
    /// </description></item>
    /// <item><description>
    /// Tagging every streamed notification with the listen request id under
    /// <c>_meta[<see cref="MetaKeys.SubscriptionId"/>]</c> so clients sharing a channel can demultiplex it.
    /// </description></item>
    /// <item><description>
    /// Remaining active for the subscription lifetime and cleaning up when the supplied
    /// <see cref="CancellationToken"/> is cancelled (client disconnect on HTTP, or
    /// <c>notifications/cancelled</c> on stdio).
    /// </description></item>
    /// <item><description>
    /// Returning <see cref="EmptyResult"/> when it deliberately completes the stream.
    /// </description></item>
    /// </list>
    /// <para>
    /// Notifications are sent through the request's server (for example <c>request.Server.SendMessageAsync</c>),
    /// which routes them over the request's own response stream. For extension filters not represented by
    /// <see cref="SubscriptionsListenRequestParams"/>, the handler can inspect
    /// <c>request.JsonRpcRequest.Params</c>. Application services and event buses can be resolved from
    /// <c>request.Services</c> or captured by the handler delegate.
    /// </para>
    /// </remarks>
    public McpRequestHandler<SubscriptionsListenRequestParams, EmptyResult>? SubscriptionsListenHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.LoggingSetLevel"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler processes <see cref="RequestMethods.LoggingSetLevel"/> requests from clients. When set, it enables
    /// clients to control which log messages they receive by specifying a minimum severity threshold.
    /// </para>
    /// <para>
    /// After handling a level change request, the server typically begins sending log messages
    /// at or above the specified level to the client as notifications/message notifications.
    /// </para>
    /// </remarks>
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public McpRequestHandler<SetLevelRequestParams, EmptyResult>? SetLoggingLevelHandler { get; set; }

    /// <summary>Gets or sets notification handlers to register with the server.</summary>
    /// <remarks>
    /// <para>
    /// When constructed, the server will enumerate these handlers, which may contain multiple handlers per notification method key, once.
    /// The server will not re-enumerate the sequence after initialization.
    /// </para>
    /// <para>
    /// Notification handlers allow the server to respond to client-sent notifications for specific methods.
    /// Each key in the collection is a notification method name, and each value is a callback that will be invoked
    /// when a notification with that method is received.
    /// </para>
    /// <para>
    /// Handlers provided via <see cref="NotificationHandlers"/> will be registered with the server for the lifetime of the server.
    /// For transient handlers, <see cref="McpSession.RegisterNotificationHandler"/> may be used to register a handler that can
    /// then be unregistered by disposing of the <see cref="IAsyncDisposable"/> returned from the method.
    /// </para>
    /// </remarks>
    public IEnumerable<KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>>? NotificationHandlers { get; set; }
}
