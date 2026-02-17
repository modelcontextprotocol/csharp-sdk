using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ResourcesRead"/> request from a client to get a resource provided by a server.
/// </summary>
/// <remarks>
/// <para>
/// The server will respond with a <see cref="ReadResourceResult"/> containing the resulting resource data.
/// </para>
/// <para>
/// Alternatively, if the resource URI uses the <c>https://</c> scheme, clients may fetch the resource
/// directly from the web instead of using <see cref="RequestMethods.ResourcesRead"/>.
/// Servers should only use the <c>https://</c> scheme when the client is able to fetch and load the
/// resource directly on its own.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class ReadResourceRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the URI of the resource to read. The URI can use any protocol; it is up to the server how to interpret it.
    /// </summary>
    [JsonPropertyName("uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public required string Uri { get; set; }
}
