using ModelContextProtocol.Protocol.Messages;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Provides a base class for paginated requests.
/// </summary>
/// <remarks>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </remarks>
public class PaginatedRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets an opaque token representing the current pagination position.
    /// </summary>
    /// <remarks>
    /// If provided, the server should return results starting after this cursor.
    /// This value should be obtained from the <see cref="PaginatedResult.NextCursor"/>
    /// property of a previous request's response.
    /// </remarks>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}