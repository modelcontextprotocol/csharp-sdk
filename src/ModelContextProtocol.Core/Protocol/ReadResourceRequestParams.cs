using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ResourcesRead"/> request from a client to get a resource provided by a server.
/// </summary>
/// <remarks>
/// The server will respond with a <see cref="ReadResourceResult"/> containing the resulting resource data.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class ReadResourceRequestParams : RequestParams
{
    /// <summary>
    /// The URI of the resource to read. The URI can use any protocol; it is up to the server how to interpret it.
    /// </summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}
