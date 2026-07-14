using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable MCPEXP001

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
    /// This information is sent to the client during initialization or discovery to identify the server.
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
    /// <para>
    /// The protocol version defines which features and message formats this server supports. Supported
    /// values are <c>2024-11-05</c>, <c>2025-03-26</c>, <c>2025-06-18</c>, <c>2025-11-25</c>, and
    /// <c>2026-07-28</c>.
    /// </para>
    /// <para>
    /// If <see langword="null"/>, the server supports all of the versions listed above. For clients using
    /// the <c>initialize</c> handshake, the server returns the requested initialize-capable version when it
    /// is supported and otherwise returns <c>2025-11-25</c>. For clients using <c>server/discover</c> and
    /// per-request metadata, the server advertises the supported per-request metadata versions; currently
    /// this is <c>2026-07-28</c>.
    /// </para>
    /// <para>
    /// Set this property to a specific supported value to pin the server to that version. Setting it to
    /// <c>2026-07-28</c> makes the server reject <c>initialize</c> handshakes; setting it to an earlier
    /// value makes the server reject <c>2026-07-28</c> per-request metadata.
    /// </para>
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
    /// Gets or sets preexisting knowledge about the client including its name and version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When not specified, this information is sourced from the client's <c>initialize</c> request or,
    /// for protocol versions that use per-request metadata, from the current request's <c>_meta</c> field.
    /// This is typically set during session migration in conjunction with <see cref="KnownClientCapabilities"/>.
    /// </para>
    /// </remarks>
    public Implementation? KnownClientInfo { get; set; }

    /// <summary>
    /// Gets or sets preexisting knowledge about the client's capabilities to support session migration
    /// scenarios where the client will not re-send the initialize request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When not specified, this information is sourced from the client's <c>initialize</c> request or,
    /// for protocol versions that use per-request metadata, from the current request's <c>_meta</c> field.
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
    [Obsolete(Obsoletions.DeprecatedSampling_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public int MaxSamplingOutputTokens { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the task store for managing asynchronous task executions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set, the server automatically enables the <c>io.modelcontextprotocol/tasks</c> extension
    /// and wires up <c>tasks/get</c>, <c>tasks/update</c>, and <c>tasks/cancel</c> handlers backed by this store.
    /// Tool executions from clients that signal task support will be wrapped in tasks via the store.
    /// </para>
    /// <para>
    /// If explicit task handlers are also set on <see cref="Handlers"/>, the explicit handlers take precedence.
    /// </para>
    /// </remarks>
    public IMcpTaskStore? TaskStore { get; set; }
}
