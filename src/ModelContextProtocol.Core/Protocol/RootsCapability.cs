using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a client capability that enables root resource discovery in the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// When present in <see cref="ClientCapabilities"/>, it indicates that the client supports listing
/// root URIs that serve as entry points for resource navigation.
/// </para>
/// <para>
/// The roots capability establishes a mechanism for servers to discover and access the hierarchical
/// structure of resources provided by a client. Root URIs represent top-level entry points from which
/// servers can navigate to access specific resources.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class RootsCapability
{
    /// <summary>
    /// Gets or sets a value that indicates whether the client supports notifications for changes to the roots list.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, the client can notify servers when roots are added,
    /// removed, or modified, allowing servers to refresh their roots cache accordingly.
    /// This enables servers to stay synchronized with client-side changes to available roots.
    /// </remarks>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}
