using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the capabilities that a client may support.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public record ClientCapabilities
{
    /// <summary>
    /// Experimental, non-standard capabilities that the client supports.
    /// </summary>
    [JsonPropertyName("experimental")]
    public Dictionary<string, object>? Experimental { get; init; }

    /// <summary>
    /// Present if the client supports listing roots.
    /// </summary>
    [JsonPropertyName("roots")]
    public RootsCapability? Roots { get; init; }

    /// <summary>
    /// Present if the client supports sampling from an LLM.
    /// </summary>
    [JsonPropertyName("sampling")]
    public SamplingCapability? Sampling { get; init; }
}

/// <summary>
/// Represents the roots capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public record RootsCapability
{
    /// <summary>
    /// Whether the client supports notifications for changes to the roots list.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; init; }

    /// <summary>Gets or sets the handler for sampling requests.</summary>
    [JsonIgnore]
    public Func<ListRootsRequestParams?, CancellationToken, Task<ListRootsResult>>? RootsHandler { get; init; }
}

/// <summary>
/// Represents the sampling capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public record SamplingCapability
{
    // Currently empty in the spec, but may be extended in the future

    /// <summary>Gets or sets the handler for sampling requests.</summary>
    [JsonIgnore]
    public Func<CreateMessageRequestParams?, CancellationToken, Task<CreateMessageResult>>? SamplingHandler { get; init; }
}

/// <summary>
/// Represents the logging capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public record LoggingCapability
{
    // Currently empty in the spec, but may be extended in the future


    /// <summary>
    /// Gets or sets the handler for set logging level requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<SetLevelRequestParams>, CancellationToken, Task<EmptyResult>>? SetLoggingLevelHandler { get; init; }
}

/// <summary>
/// Represents the prompts capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public record PromptsCapability
{
    /// <summary>
    /// Whether this server supports notifications for changes to the prompt list.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; init; }

    /// <summary>
    /// Gets or sets the handler for list prompts requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ListPromptsRequestParams>, CancellationToken, Task<ListPromptsResult>>? ListPromptsHandler { get; init; }

    /// <summary>
    /// Gets or sets the handler for get prompt requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<GetPromptRequestParams>, CancellationToken, Task<GetPromptResult>>? GetPromptHandler { get; init; }
}

/// <summary>
/// Represents the resources capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public record ResourcesCapability
{
    /// <summary>
    /// Whether this server supports subscribing to resource updates.
    /// </summary>
    [JsonPropertyName("subscribe")]
    public bool? Subscribe { get; init; }

    /// <summary>
    /// Whether this server supports notifications for changes to the resource list.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; init; }

    /// <summary>
    /// Gets or sets the handler for list resource templates requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, Task<ListResourceTemplatesResult>>? ListResourceTemplatesHandler { get; init; }

    /// <summary>
    /// Gets or sets the handler for list resources requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ListResourcesRequestParams>, CancellationToken, Task<ListResourcesResult>>? ListResourcesHandler { get; init; }

    /// <summary>
    /// Gets or sets the handler for read resources requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ReadResourceRequestParams>, CancellationToken, Task<ReadResourceResult>>? ReadResourceHandler { get; init; }

    /// <summary>
    /// Gets or sets the handler for subscribe to resources messages.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<SubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? SubscribeToResourcesHandler { get; init; }

    /// <summary>
    /// Gets or sets the handler for unsubscribe from resources messages.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? UnsubscribeFromResourcesHandler { get; init; }
}

/// <summary>
/// Represents the tools capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public record ToolsCapability
{
    /// <summary>
    /// Whether this server supports notifications for changes to the tool list.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; init; }

    /// <summary>
    /// Gets or sets the handler for list tools requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ListToolsRequestParams>, CancellationToken, Task<ListToolsResult>>? ListToolsHandler { get; init; }

    /// <summary>
    /// Gets or sets the handler for call tool requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<CallToolResponse>>? CallToolHandler { get; init; }
}