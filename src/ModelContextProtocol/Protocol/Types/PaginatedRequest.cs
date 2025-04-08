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
    /// This value should be obtained from the <see cref="Protocol.Messages.PaginatedResult.NextCursor"/>
    /// property of a previous request's response.
    /// </summary>
    /// <example>
    /// <code>
    /// // Client-side pagination example
    /// var result = await client.ListResourcesAsync();
    /// List&lt;Resource&gt; allResources = new(result.Resources);
    /// 
    /// // Continue fetching while there are more pages
    /// while (result.NextCursor != null)
    /// {
    ///     // Use the NextCursor from the previous result as the Cursor for the next request
    ///     result = await client.ListResourcesAsync(new() { Cursor = result.NextCursor });
    ///     allResources.AddRange(result.Resources);
    /// }
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}