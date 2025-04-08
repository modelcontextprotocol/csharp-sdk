using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a resources/list request from the client.
/// </summary>
/// <remarks>
/// This class inherits from <see cref="PaginatedResult"/>, which means it supports cursor-based
/// pagination through the <see cref="PaginatedResult.NextCursor"/> property. When there are more
/// results available, the server sets the NextCursor property to a value that clients can use
/// in subsequent requests to retrieve the next page of resources.
/// </remarks>
/// <example>
/// <code>
/// // Server-side implementation for handling a resources/list request
/// public Task&lt;ListResourcesResult&gt; HandleListResourcesRequest(RequestContext&lt;ListResourcesRequestParams&gt; context, CancellationToken cancellationToken)
/// {
///     // Get the starting position from the cursor, if provided
///     int startIndex = 0;
///     if (!string.IsNullOrEmpty(context.Request.Cursor))
///     {
///         try
///         {
///             var cursorBytes = Convert.FromBase64String(context.Request.Cursor);
///             startIndex = int.Parse(Encoding.UTF8.GetString(cursorBytes));
///         }
///         catch
///         {
///             // Invalid cursor format, start from the beginning
///         }
///     }
///     
///     // Get all available resources (in a real implementation, this would come from a database or other source)
///     var resources = GetAllResources();
///     
///     // Implement pagination (e.g., 10 items per page)
///     int pageSize = 10;
///     int endIndex = Math.Min(startIndex + pageSize, resources.Count);
///     
///     // Create a cursor for the next page if there are more results
///     string? nextCursor = null;
///     if (endIndex &lt; resources.Count)
///     {
///         nextCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(endIndex.ToString()));
///     }
///     
///     // Return the paginated results
///     return Task.FromResult(new ListResourcesResult()
///     {
///         NextCursor = nextCursor,
///         Resources = resources.GetRange(startIndex, endIndex - startIndex)
///     });
/// }
/// </code>
/// </example>
/// <seealso cref="ListResourcesRequestParams"/>
/// <seealso cref="Resource"/>
/// <seealso cref="PaginatedResult"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">Model Context Protocol schema for details</seealso>
public class ListResourcesResult : PaginatedResult
{
    /// <summary>
    /// A list of resources that the server offers.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("resources")]
    public List<Resource> Resources { get; set; } = [];
}
