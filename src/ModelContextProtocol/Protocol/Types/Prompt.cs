namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// A prompt or prompt template that the server offers.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class Prompt
{
    /// <summary>
    /// A list of arguments to use for templating the prompt.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("arguments")]
    public List<PromptArgument>? Arguments { get; set; }

    /// <summary>
    /// An optional description of what this prompt provides
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The name of the prompt or prompt template.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
