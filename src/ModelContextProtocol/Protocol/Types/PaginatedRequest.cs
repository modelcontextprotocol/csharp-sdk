namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Used as a base class for paginated requests.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public class PaginatedRequestParams : RequestParams
{
    /// <summary>
    /// An opaque token representing the current pagination position.
    /// If provided, the server should return results starting after this cursor.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}