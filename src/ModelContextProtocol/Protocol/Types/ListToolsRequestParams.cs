namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the client to request a list of tools the server has.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ListToolsRequestParams
{
    /// <summary>
    /// An opaque token representing the current pagination position.
    /// If provided, the server should return results starting after this cursor.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}
