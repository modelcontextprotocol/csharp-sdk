using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class for paginated requests.
/// </summary>
/// <remarks>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </remarks>
public abstract class PaginatedRequestParams : RequestParams
{
    /// <summary>Prevent external derivations.</summary>
    private protected PaginatedRequestParams()
    {
    }

    /// <summary>
    /// Gets or sets an opaque token representing the current pagination position.
    /// </summary>
    /// <remarks>
    /// If provided, the server should return results starting after this cursor.
    /// This value should be obtained from the <see cref="PaginatedResult.NextCursor"/>
    /// property of a previous request's response.
    /// </remarks>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}
