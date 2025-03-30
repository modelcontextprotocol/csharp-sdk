namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Used by the client to get a prompt provided by the server.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class GetPromptRequestParams : RequestParams
{
    /// <summary>
    /// he name of the prompt or prompt template.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Arguments to use for templating the prompt.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("arguments")]
    public Dictionary<string, object>? Arguments { get; init; }
}
