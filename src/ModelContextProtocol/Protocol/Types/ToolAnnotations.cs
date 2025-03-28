using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Additional properties describing a Tool to clients.
/// NOTE: all properties in ToolAnnotations are **hints**.
/// They are not guaranteed to provide a faithful description of tool behavior (including descriptive properties like `title`).
/// Clients should never make tool use decisions based on ToolAnnotations received from untrusted servers.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// There are multiple subtypes of content, depending on the "type" field, these are represented as separate classes.
/// </summary>
public class ToolAnnotations
{
    /// <summary>
    /// A human-readable title for the tool.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// If true, the tool may perform destructive updates to its environment.
    /// If false, the tool performs only additive updates.
    /// (This property is meaningful only when <see cref="ReadOnlyHint"/> is false).
    /// Default: true.
    /// </summary>
    [JsonPropertyName("destructiveHint")]
    public bool? DestructiveHint { get; set; }

    /// <summary>
    /// If true, calling the tool repeatedly with the same arguments 
    /// will have no additional effect on its environment.
    /// (This property is meaningful only when <see cref="ReadOnlyHint"/> is false).
    /// Default: false.
    /// </summary>
    [JsonPropertyName("idempotentHint")]
    public bool? IdempotentHint { get; set; }

    /// <summary>
    /// If true, this tool may interact with an "open world" of external entities.
    /// If false, the tool's domain of interaction is closed.
    /// For example, the world of a web search tool is open, whereas that of a memory tool is not.
    /// Default: true.
    /// </summary>
    [JsonPropertyName("openWorldHint")]
    public bool? OpenWorldHint { get; set; }

    /// <summary>
    /// If true, the tool does not modify its environment.
    /// Default: false.
    /// </summary>
    [JsonPropertyName("readOnlyHint")]
    public bool? ReadOnlyHint { get; set; }
}