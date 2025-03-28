namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a resources/read request from the client.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public class ReadResourceResult
{
    /// <summary>
    /// A list of ResourceContents that this resource contains.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("contents")]
    public List<ResourceContents> Contents { get; set; } = [];
}
