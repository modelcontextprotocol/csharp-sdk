using ModelContextProtocol.Protocol;

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
    /// If <see langword="null"/>, the server will advertize to the client the version requested
    /// by the client if that version is known to be supported, and otherwise will advertize the latest
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
    /// guidance on how to effectively use the server's capabilities. They can include details
    /// about available tools, expected input formats, limitations, or other helpful information.
    /// Client applications typically use these instructions as system messages for LLM interactions
    /// to provide context about available functionality.
    /// </remarks>
    public string? ServerInstructions { get; set; }

    /// <summary>
    /// Gets or sets whether to create a new service provider scope for each handled request.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="true"/>. When <see langword="true"/>, each invocation of a request
    /// handler will be invoked within a new service scope.
    /// </remarks>
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
    /// Gets the filter collections for MCP server handlers.
    /// </summary>
    /// <remarks>
    /// This property provides access to filter collections that can be used to modify the behavior
    /// of various MCP server handlers. Filters are applied in reverse order, so the last filter
    /// added will be the outermost (first to execute).
    /// </remarks>
    public McpServerFilters Filters { get; } = new();

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
    /// <remarks>
    /// <para>
    /// This value is used in <see cref="McpServer.SampleAsync(IEnumerable{Microsoft.Extensions.AI.ChatMessage}, Microsoft.Extensions.AI.ChatOptions?, RequestOptions?, CancellationToken)"/>
    /// when <see cref="Microsoft.Extensions.AI.ChatOptions.MaxOutputTokens"/> is not set in the request options.
    /// </para>
    /// <para>
    /// The default value is 1000 tokens.
    /// </para>
    /// </remarks>
    public int MaxSamplingOutputTokens { get; set; } = 1000;
}
