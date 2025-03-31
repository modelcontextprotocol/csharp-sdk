namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a prompts/get request from the client.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class GetPromptResult
{
    /// <summary>
    /// An optional description for the prompt.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The prompt or prompt template that the server offers.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    public List<PromptMessage> Messages { get; set; } = [];
}
