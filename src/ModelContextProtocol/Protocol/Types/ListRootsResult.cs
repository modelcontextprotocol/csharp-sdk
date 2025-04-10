using ModelContextProtocol.Protocol.Messages;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a client's response to a <see cref="RequestMethods.RootsList"/> request from the server,
/// containing available roots.
/// </summary>
/// <remarks>
/// <para>
/// This result is returned when a server sends a <see cref="RequestMethods.RootsList"/> request to discover 
/// available roots on the client.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public class ListRootsResult
{
    /// <summary>
    /// Gets or sets additional metadata for the result.
    /// </summary>
    /// <remarks>
    /// This property is reserved by the protocol for future use.
    /// </remarks>
    [JsonPropertyName("meta")]
    public JsonElement? Meta { get; init; }

    /// <summary>
    /// Gets or sets the list of root URIs provided by the client.
    /// </summary>
    /// <remarks>
    /// This collection contains all available root URIs and their associated metadata.
    /// Each root serves as an entry point for resource navigation in the Model Context Protocol.
    /// </remarks>
    [JsonPropertyName("roots")]
    public required IReadOnlyList<Root> Roots { get; init; }
}
