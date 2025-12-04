using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a server's response to a <see cref="RequestMethods.ToolsList"/> request from the client, containing available tools.
/// </summary>
/// <remarks>
/// <para>
/// This result is returned when a client sends a <see cref="RequestMethods.ToolsList"/> request to discover available tools on the server.
/// </para>
/// <para>
/// It inherits from <see cref="PaginatedResult"/>, allowing for paginated responses when there are many tools.
/// The server can provide the <see cref="PaginatedResult.NextCursor"/> property to indicate there are more
/// tools available beyond what was returned in the current response.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class ListToolsResult : PaginatedResult
{
    /// <summary>
    /// Gets or sets the server's response to a tools/list request from the client.
    /// </summary>
    [JsonPropertyName("tools")]
    public IList<Tool> Tools { get; set; } = [];
}
