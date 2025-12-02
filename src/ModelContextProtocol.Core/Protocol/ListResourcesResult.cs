using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a server's response to a <see cref="RequestMethods.ResourcesList"/> request from the client, containing available resources.
/// </summary>
/// <remarks>
/// <para>
/// This result is returned when a client sends a <see cref="RequestMethods.ResourcesList"/> request to discover available resources on the server.
/// </para>
/// <para>
/// It inherits from <see cref="PaginatedResult"/>, allowing for paginated responses when there are many resources.
/// The server can provide the <see cref="PaginatedResult.NextCursor"/> property to indicate there are more
/// resources available beyond what was returned in the current response.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class ListResourcesResult : PaginatedResult
{
    /// <summary>
    /// Gets or sets a list of resources that the server offers.
    /// </summary>
    [JsonPropertyName("resources")]
    public IList<Resource> Resources { get; set; } = [];
}
