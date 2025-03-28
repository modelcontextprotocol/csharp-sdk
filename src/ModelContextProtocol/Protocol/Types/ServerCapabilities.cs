using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the capabilities that a server may support.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ServerCapabilities
{
    /// <summary>
    /// Experimental, non-standard capabilities that the server supports.
    /// </summary>
    [JsonPropertyName("experimental")]
    public Dictionary<string, object>? Experimental { get; set; }

    /// <summary>
    /// Present if the server supports sending log messages to the client.
    /// </summary>
    [JsonPropertyName("logging")]
    public LoggingCapability? Logging { get; set; }

    /// <summary>
    /// Present if the server offers any prompt templates.
    /// </summary>
    [JsonPropertyName("prompts")]
    public PromptsCapability? Prompts { get; set; }

    /// <summary>
    /// Present if the server offers any resources to read.
    /// </summary>
    [JsonPropertyName("resources")]
    public ResourcesCapability? Resources { get; set; }

    /// <summary>
    /// Present if the server offers any tools to call.
    /// </summary>
    [JsonPropertyName("tools")]
    public ToolsCapability? Tools { get; set; }
}
