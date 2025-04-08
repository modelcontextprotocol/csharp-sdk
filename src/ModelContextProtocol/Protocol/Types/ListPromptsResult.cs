using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a prompts/list request from the client, containing available prompts and pagination information.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// <para>
/// This result is returned when a client sends a prompts/list request to discover available prompts on the server.
/// It inherits from <see cref="PaginatedResult"/>, allowing for paginated responses when there are many prompts.
/// </para>
/// <para>
/// The server can provide the <see cref="PaginatedResult.NextCursor"/> property to indicate there are more
/// prompts available beyond what was returned in the current response.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Server-side implementation example
/// public Task&lt;ListPromptsResult&gt; HandleListPromptsRequest(
///     RequestContext&lt;ListPromptsRequestParams&gt; request,
///     CancellationToken cancellationToken)
/// {
///     // Get available prompts (potentially from a database or repository)
///     var allPrompts = GetAvailablePrompts();
///     
///     // Handle pagination
///     int pageSize = 10;
///     int startIndex = 0;
///     
///     // If a cursor is provided, decode it to get the starting position
///     if (!string.IsNullOrEmpty(request.Params?.Cursor))
///     {
///         startIndex = int.Parse(
///             Encoding.UTF8.GetString(
///                 Convert.FromBase64String(request.Params.Cursor)));
///     }
///     
///     int endIndex = Math.Min(startIndex + pageSize, allPrompts.Count);
///     
///     // Create cursor for next page if needed
///     string? nextCursor = null;
///     if (endIndex &lt; allPrompts.Count)
///     {
///         nextCursor = Convert.ToBase64String(
///             Encoding.UTF8.GetBytes(endIndex.ToString()));
///     }
///     
///     return Task.FromResult(new ListPromptsResult()
///     {
///         NextCursor = nextCursor,
///         Prompts = allPrompts.GetRange(startIndex, endIndex - startIndex)
///     });
/// }
/// </code>
/// </example>
public class ListPromptsResult : PaginatedResult
{
    /// <summary>
    /// A list of prompts or prompt templates that the server offers.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("prompts")]
    public List<Prompt> Prompts { get; set; } = [];
}