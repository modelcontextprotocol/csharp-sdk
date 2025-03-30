namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Describes a message returned as part of a prompt.
/// 
/// This is similar to `SamplingMessage`, but also supports the embedding of 
/// resources from the MCP server.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class PromptMessage
{
    /// <summary>
    /// The content of the message. Any of TextContent, ImageContent, EmbeddedResource.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public Content Content { get; set; } = new();

    /// <summary>
    /// The role of the message ("user" or "assistant").
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("role")]
    public Role Role { get; set; } = new();
}
