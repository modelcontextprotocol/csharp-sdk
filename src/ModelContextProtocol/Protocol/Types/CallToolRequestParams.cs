namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Used by the client to invoke a tool provided by the server.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class CallToolRequestParams : RequestParams
{
    /// <summary>
    /// Tool name.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Optional arguments to pass to the tool.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("arguments")]
    public Dictionary<string,object>? Arguments { get; init; }
}
