using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

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