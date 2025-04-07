using System.Text.Json;
using System.Text.Json.Serialization;

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
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Arguments to use for templating the prompt.
    /// </summary>
    [JsonPropertyName("arguments")]
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; init; }
}
