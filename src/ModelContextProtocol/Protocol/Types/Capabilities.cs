using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the capabilities that a client may support.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ClientCapabilities
{
    /// <summary>
    /// Experimental, non-standard capabilities that the client supports.
    /// </summary>
    [JsonPropertyName("experimental")]
    public Dictionary<string, object>? Experimental { get; set; }

    /// <summary>
    /// Present if the client supports listing roots.
    /// </summary>
    [JsonPropertyName("roots")]
    public RootsCapability? Roots { get; set; }

    /// <summary>
    /// Present if the client supports sampling from an LLM.
    /// </summary>
    [JsonPropertyName("sampling")]
    public SamplingCapability? Sampling { get; set; }
}

/// <summary>
/// Represents the roots capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class RootsCapability
{
    /// <summary>
    /// Whether the client supports notifications for changes to the roots list.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>Gets or sets the handler for sampling requests.</summary>
    [JsonIgnore]
    public Func<ListRootsRequestParams?, CancellationToken, Task<ListRootsResult>>? RootsHandler { get; set; }
}

/// <summary>
/// Represents the sampling capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class SamplingCapability
{
    // Currently empty in the spec, but may be extended in the future

    /// <summary>Gets or sets the handler for sampling requests.</summary>
    [JsonIgnore]
    public Func<CreateMessageRequestParams?, CancellationToken, Task<CreateMessageResult>>? SamplingHandler { get; set; }
}

/// <summary>
/// Represents the logging capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class LoggingCapability
{
    // Currently empty in the spec, but may be extended in the future


    /// <summary>
    /// Gets or sets the handler for set logging level requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<SetLevelRequestParams>, CancellationToken, Task<EmptyResult>>? SetLoggingLevelHandler { get; set; }
}

/// <summary>
/// Represents the prompts capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class PromptsCapability
{
    /// <summary>
    /// Whether this server supports notifications for changes to the prompt list.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>
    /// Gets or sets the handler for list prompts requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ListPromptsRequestParams>, CancellationToken, Task<ListPromptsResult>>? ListPromptsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for get prompt requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<GetPromptRequestParams>, CancellationToken, Task<GetPromptResult>>? GetPromptHandler { get; set; }

    /// <summary>Gets or sets a collection of prompts served by the server.</summary>
    /// <remarks>
    /// Prompts will specified via <see cref="PromptCollection"/> augment the <see cref="ListPromptsHandler"/> and
    /// <see cref="GetPromptHandler"/>, if provided. ListPrompts requests will output information about every prompt
    /// in <see cref="PromptCollection"/> and then also any tools output by <see cref="ListPromptsHandler"/>, if it's
    /// non-<see langword="null"/>. GetPrompt requests will first check <see cref="PromptCollection"/> for the prompt
    /// being requested, and if the tool is not found in the <see cref="PromptCollection"/>, any specified <see cref="GetPromptHandler"/>
    /// will be invoked as a fallback.
    /// </remarks>
    [JsonIgnore]
    public McpServerPrimitiveCollection<McpServerPrompt>? PromptCollection { get; set; }
}

/// <summary>
/// Represents the resources capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ResourcesCapability
{
    /// <summary>
    /// Whether this server supports subscribing to resource updates.
    /// </summary>
    [JsonPropertyName("subscribe")]
    public bool? Subscribe { get; set; }

    /// <summary>
    /// Whether this server supports notifications for changes to the resource list.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>
    /// Gets or sets the handler for list resource templates requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, Task<ListResourceTemplatesResult>>? ListResourceTemplatesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for list resources requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ListResourcesRequestParams>, CancellationToken, Task<ListResourcesResult>>? ListResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for read resources requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ReadResourceRequestParams>, CancellationToken, Task<ReadResourceResult>>? ReadResourceHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for subscribe to resources messages.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<SubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? SubscribeToResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for unsubscribe from resources messages.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? UnsubscribeFromResourcesHandler { get; set; }
}

/// <summary>
/// Represents the tools capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ToolsCapability
{
    /// <summary>
    /// Gets or sets whether this server supports notifications for changes to the tool list.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>
    /// Gets or sets the handler for list tools requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<ListToolsRequestParams>, CancellationToken, Task<ListToolsResult>>? ListToolsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for call tool requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<CallToolResponse>>? CallToolHandler { get; set; }

    /// <summary>Gets or sets a collection of tools served by the server.</summary>
    /// <remarks>
    /// Tools will specified via <see cref="ToolCollection"/> augment the <see cref="ListToolsHandler"/> and
    /// <see cref="CallToolHandler"/>, if provided. ListTools requests will output information about every tool
    /// in <see cref="ToolCollection"/> and then also any tools output by <see cref="ListToolsHandler"/>, if it's
    /// non-<see langword="null"/>. CallTool requests will first check <see cref="ToolCollection"/> for the tool
    /// being requested, and if the tool is not found in the <see cref="ToolCollection"/>, any specified <see cref="CallToolHandler"/>
    /// will be invoked as a fallback.
    /// </remarks>
    [JsonIgnore]
    public McpServerPrimitiveCollection<McpServerTool>? ToolCollection { get; set; }
}