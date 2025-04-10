using ModelContextProtocol.Protocol.Messages;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a server's response to a <see cref="RequestMethods.ResourcesRead"/> request from the client.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public class ReadResourceResult
{
    /// <summary>
    /// Gets or sets a list of <see cref="ResourceContents"/> objects that this resource contains.
    /// </summary>
    /// <remarks>
    /// This property contains the actual content of the requested resource, which can be
    /// either text-based (<see cref="TextResourceContents"/>) or binary (<see cref="BlobResourceContents"/>).
    /// The type of content included depends on the resource being accessed.
    /// </remarks>
    [JsonPropertyName("contents")]
    public List<ResourceContents> Contents { get; set; } = [];
}
