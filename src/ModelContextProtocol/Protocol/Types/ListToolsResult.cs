using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a tools/list request from the client, containing available tools and pagination information.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// <para>
/// This result is returned when a client sends a tools/list request to discover available tools on the server.
/// It inherits from <see cref="PaginatedResult"/>, allowing for paginated responses when there are many tools.
/// </para>
/// <para>
/// When the number of tools is large, the server can use the <see cref="PaginatedResult.NextCursor"/> property
/// to indicate more tools are available beyond what was returned in the current response. Clients can use this cursor
/// in subsequent requests to retrieve additional pages of tools.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Client-side pagination example
/// var result = await client.ListToolsAsync();
/// List&lt;Tool&gt; allTools = new(result.Tools);
/// 
/// // Continue fetching while there are more pages
/// while (result.NextCursor != null)
/// {
///     result = await client.ListToolsAsync(new() { Cursor = result.NextCursor });
///     allTools.AddRange(result.Tools);
/// }
/// </code>
/// </example>
/// <seealso cref="PaginatedResult"/>
/// <seealso cref="Tool"/>
public class ListToolsResult : PaginatedResult
{
    /// <summary>
    /// The server's response to a tools/list request from the client.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("tools")]
    public List<Tool> Tools { get; set; } = [];
}
