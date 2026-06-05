using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides configuration options for the MCP server.
/// </summary>
public sealed class McpServerOptions
{
    /// <summary>
    /// Gets or sets information about this server implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// This information is sent to the client during initialization to identify the server.
    /// It's displayed in client logs and can be used for debugging and compatibility checks.
    /// </remarks>
    public Implementation? ServerInfo { get; set; }

    /// <summary>
    /// Gets or sets server capabilities to advertise to the client.
    /// </summary>
    /// <remarks>
    /// These determine which features will be available when a client connects.
    /// Capabilities can include "tools", "prompts", "resources", "logging", and other
    /// protocol-specific functionality.
    /// </remarks>
    public ServerCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Gets or sets the protocol version supported by this server, using a date-based versioning scheme.
    /// </summary>
    /// <remarks>
    /// The protocol version defines which features and message formats this server supports.
    /// This uses a date-based versioning scheme in the format "YYYY-MM-DD".
    /// If <see langword="null"/>, the server will advertise to the client the version requested
    /// by the client if that version is known to be supported, and otherwise will advertise the latest
    /// version supported by the server.
    /// </remarks>
    public string? ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets a timeout used for the client-server initialization handshake sequence.
    /// </summary>
    /// <remarks>
    /// This timeout determines how long the server will wait for client responses during
    /// the initialization protocol handshake. If the client doesn't respond within this timeframe,
    /// the initialization process will be aborted.
    /// </remarks>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets optional server instructions to send to clients.
    /// </summary>
    /// <remarks>
    /// These instructions are sent to clients during the initialization handshake and provide
    /// guidance on how to effectively use the server's capabilities. They should focus on
    /// information that helps models use the server effectively and should not duplicate
    /// tool, prompt, or resource descriptions already exposed elsewhere.
    /// Client applications typically use these instructions as system messages for LLM interactions
    /// to provide context about available functionality.
    /// </remarks>
    public string? ServerInstructions { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether to create a new service provider scope for each handled request.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if each invocation of a request handler is invoked within a new service scope.
    /// The default is <see langword="true"/>.
    /// </value>
    public bool ScopeRequests { get; set; } = true;

    /// <summary>
    /// Gets or sets preexisting knowledge about the client including its name and version to help support
    /// stateless Streamable HTTP servers that encode this knowledge in the mcp-session-id header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When not specified, this information is sourced from the client's initialize request.
    /// </para>
    /// </remarks>
    public Implementation? KnownClientInfo { get; set; }

    /// <summary>
    /// Gets or sets preexisting knowledge about the client's capabilities to support session migration
    /// scenarios where the client will not re-send the initialize request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When not specified, this information is sourced from the client's initialize request.
    /// This is typically set during session migration in conjunction with <see cref="KnownClientInfo"/>.
    /// </para>
    /// </remarks>
    public ClientCapabilities? KnownClientCapabilities { get; set; }

    /// <summary>
    /// Gets or sets the container of handlers used by the server for processing protocol messages.
    /// </summary>
    public McpServerHandlers Handlers
    {
        get => field ??= new();
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filter collections for MCP server handlers.
    /// </summary>
    /// <remarks>
    /// This property provides access to filter collections that can be used to modify the behavior
    /// of various MCP server handlers. The first filter added is the outermost (first to execute),
    /// and each subsequent filter wraps closer to the handler.
    /// </remarks>
    public McpServerFilters Filters
    {
        get => field ??= new();
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets a collection of tools served by the server.
    /// </summary>
    /// <remarks>
    /// Tools specified via <see cref="ToolCollection"/> augment the <see cref="McpServerHandlers.ListToolsHandler"/> and
    /// <see cref="McpServerHandlers.CallToolHandler"/>, if provided. ListTools requests will output information about every tool
    /// in <see cref="ToolCollection"/> and then also any tools output by <see cref="McpServerHandlers.ListToolsHandler"/>, if it's
    /// non-<see langword="null"/>. CallTool requests will first check <see cref="ToolCollection"/> for the tool
    /// being requested, and if the tool is not found in the <see cref="ToolCollection"/>, any specified <see cref="McpServerHandlers.CallToolHandler"/>
    /// will be invoked as a fallback.
    /// </remarks>
    public McpServerPrimitiveCollection<McpServerTool>? ToolCollection { get; set; }

    /// <summary>
    /// Gets or sets a collection of resources served by the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resources specified via <see cref="ResourceCollection"/> augment the <see cref="McpServerHandlers.ListResourcesHandler"/>, <see cref="McpServerHandlers.ListResourceTemplatesHandler"/>
    /// and <see cref="McpServerHandlers.ReadResourceHandler"/> handlers, if provided. Resources with template expressions in their URI templates are considered resource templates
    /// and are listed via ListResourceTemplate, whereas resources without template parameters are considered static resources and are listed with ListResources.
    /// </para>
    /// <para>
    /// ReadResource requests will first check the <see cref="ResourceCollection"/> for the exact resource being requested. If no match is found, they'll proceed to
    /// try to match the resource against each resource template in <see cref="ResourceCollection"/>. If no match is still found, the request will fall back to
    /// any handler registered for <see cref="McpServerHandlers.ReadResourceHandler"/>.
    /// </para>
    /// </remarks>
    public McpServerResourceCollection? ResourceCollection { get; set; }

    /// <summary>
    /// Gets or sets a collection of prompts that will be served by the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="PromptCollection"/> contains the predefined prompts that clients can request from the server.
    /// This collection works in conjunction with <see cref="McpServerHandlers.ListPromptsHandler"/> and <see cref="McpServerHandlers.GetPromptHandler"/>
    /// when those are provided:
    /// </para>
    /// <para>
    /// - For <see cref="RequestMethods.PromptsList"/> requests: The server returns all prompts from this collection
    ///   plus any additional prompts provided by the <see cref="McpServerHandlers.ListPromptsHandler"/> if it's set.
    /// </para>
    /// <para>
    /// - For <see cref="RequestMethods.PromptsGet"/> requests: The server first checks this collection for the requested prompt.
    ///   If not found, it will invoke the <see cref="McpServerHandlers.GetPromptHandler"/> as a fallback if one is set.
    /// </para>
    /// </remarks>
    public McpServerPrimitiveCollection<McpServerPrompt>? PromptCollection { get; set; }

    /// <summary>
    /// Gets or sets the default maximum number of tokens to use for sampling requests when not explicitly specified.
    /// </summary>
    /// <value>
    /// The default maximum number of tokens to use for sampling requests. The default value is 1000 tokens.
    /// </value>
    /// <remarks>
    /// This value is used in <see cref="McpServer.SampleAsync(IEnumerable{Microsoft.Extensions.AI.ChatMessage}, Microsoft.Extensions.AI.ChatOptions?, System.Text.Json.JsonSerializerOptions?, CancellationToken)"/>
    /// when <see cref="Microsoft.Extensions.AI.ChatOptions.MaxOutputTokens"/> is not set in the request options.
    /// </remarks>
    public int MaxSamplingOutputTokens { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the task store for managing asynchronous task execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When non-null, enables explicit task support with persistence, allowing clients to:
    /// <list type="bullet">
    /// <item><description>Execute operations asynchronously by augmenting requests with task metadata</description></item>
    /// <item><description>Poll for task status via tasks/get requests</description></item>
    /// <item><description>Retrieve task results via tasks/result requests</description></item>
    /// <item><description>List all tasks via tasks/list requests</description></item>
    /// <item><description>Cancel tasks via tasks/cancel requests</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// When null, implicit task support may still be available for async methods (returning <see cref="Task"/> or
    /// <see cref="ValueTask"/>), but tasks will be ephemeral and not persisted. Use <see cref="InMemoryMcpTaskStore"/>
    /// for development/testing or implement <see cref="IMcpTaskStore"/> for production scenarios.
    /// </para>
    /// <para>
    /// The server will automatically advertise task capabilities based on the presence of a task store
    /// and the detection of async server primitives (tools, prompts, resources).
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public IMcpTaskStore? TaskStore { get; set; }

    /// <summary>
    /// Gets or sets whether to send task status notifications to clients.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to send optional <c>notifications/tasks/status</c> notifications when task status changes;
    /// <see langword="false"/> to not send notifications. The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// When enabled, the server will send <c>notifications/tasks/status</c> notifications to inform clients
    /// of task state changes. According to the MCP specification, these notifications are optional and
    /// receivers MAY send them but are not required to.
    /// </para>
    /// <para>
    /// Clients must not rely on receiving these notifications and should continue polling via <c>tasks/get</c>
    /// requests to ensure they receive status updates.
    /// </para>
    /// <para>
    /// Even when this is set to <see langword="true"/>, notifications are only sent when <see cref="TaskStore"/>
    /// is configured, as task-augmented requests require a task store.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public bool SendTaskStatusNotifications { get; set; }
}
