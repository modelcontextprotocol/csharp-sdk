namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// A base class for result payloads that support cursor-based pagination.
/// </summary>
/// <remarks>
/// <para>
/// Pagination allows API responses to be broken into smaller, manageable chunks when
/// there are potentially many results to return. This helps optimize both client and server
/// performance by limiting the amount of data transferred in a single request.
/// </para>
/// <para>
/// Classes that inherit from <see cref="PaginatedResult"/> implement cursor-based pagination,
/// where the <see cref="NextCursor"/> property serves as an opaque token pointing to the next 
/// set of results. When a paginated result has more data available, the <see cref="NextCursor"/> 
/// property will contain a token that the client can use in subsequent requests to fetch the next page.
/// </para>
/// <para>
/// Typical usage pattern:
/// 1. Client makes an initial request without a cursor
/// 2. Server returns results and a NextCursor if more data is available
/// 3. Client makes subsequent requests with the Cursor parameter set to the NextCursor from the previous response
/// 4. This continues until NextCursor is null, indicating no more data is available
/// </para>
/// </remarks>
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
/// <seealso cref="Protocol.Types.PaginatedRequestParams"/>
public class PaginatedResult
{
    /// <summary>
    /// An opaque token representing the pagination position after the last returned result.
    /// If present, there may be more results available. Clients should pass this token
    /// in the <see cref="Protocol.Types.PaginatedRequestParams.Cursor"/> property of subsequent requests.
    /// </summary>
    /// <example>
    /// <code>
    /// // Server-side implementation
    /// string? nextCursor = null;
    /// if (endIndex &lt; resources.Count)
    /// {
    ///     // Create a cursor for the next page
    ///     nextCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(endIndex.ToString()));
    /// }
    /// return new ListResourcesResult()
    /// {
    ///     NextCursor = nextCursor,
    ///     Resources = resources.GetRange(startIndex, endIndex - startIndex)
    /// };
    /// 
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
    public string? NextCursor { get; set; }
}