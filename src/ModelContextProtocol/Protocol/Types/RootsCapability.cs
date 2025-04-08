using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a client capability that enables root resource discovery in the Model Context Protocol.
/// When present in <see cref="ClientCapabilities"/>, it indicates that the client supports listing
/// root URIs that serve as entry points for resource navigation.
/// </summary>
/// <remarks>
/// <para>
/// The roots capability establishes a mechanism for servers to discover and access the hierarchical 
/// structure of resources provided by a client. Root URIs represent top-level entry points from which
/// servers can navigate to access specific resources.
/// </para>
/// <para>
/// When a client supports this capability, a server can send a <see cref="ListRootsRequestParams"/> request
/// to discover available roots, and the client will respond with a <see cref="ListRootsResult"/> containing
/// a collection of <see cref="Root"/> objects.
/// </para>
/// <para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Client configuring roots capability
/// var clientOptions = new McpClientOptions
/// {
///     Capabilities = new ClientCapabilities
///     {
///         Roots = new RootsCapability
///         {
///             // Optionally support notifications when the roots list changes
///             ListChanged = true,
///             
///             // Handler to respond to roots listing requests
///             RootsHandler = (params, token) =>
///             {
///                 // Return available roots
///                 return Task.FromResult(new ListRootsResult
///                 {
///                     Roots = new List&lt;Root&gt;
///                     {
///                         new Root { Uri = "mcp://model1/", Name = "Model 1" },
///                         new Root { Uri = "mcp://model2/", Name = "Model 2" }
///                     }
///                 });
///             }
///         }
///     }
/// };
/// </code>
/// </example>
/// <seealso cref="ClientCapabilities"/>
/// <seealso cref="Root"/>
/// <seealso cref="ListRootsRequestParams"/>
/// <seealso cref="ListRootsResult"/>
public class RootsCapability
{
    /// <summary>
    /// Gets or sets whether the server supports notifications for changes to the roots list.
    /// When set to <see langword="true"/>, the server can notify clients when roots are added, 
    /// removed, or modified, allowing clients to refresh their roots cache accordingly.
    /// </summary>
    /// <remarks>
    /// This capability enables clients to stay synchronized with server-side changes to available roots.
    /// Unlike other capabilities, this one refers to the server's ability to send notifications about
    /// roots changes, which clients can handle to maintain an updated view of available roots.
    /// </remarks>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>
    /// Gets or sets the handler for root listing requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client sends a roots/list request to retrieve available roots.
    /// The handler receives request parameters and a cancellation token, and should return a 
    /// <see cref="ListRootsResult"/> containing the collection of available roots.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Setting up a roots handler
    /// client.SetCapabilities(new ClientCapabilities {
    ///     Roots = new RootsCapability {
    ///         RootsHandler = (request, token) => {
    ///             return Task.FromResult(new ListRootsResult {
    ///                 Roots = new List&lt;Root&gt; {
    ///                     new Root { Uri = "mcp://mymodel/", Name = "My Model" },
    ///                     new Root { Uri = "mcp://another-model/", Name = "Another Model" }
    ///                 }
    ///             });
    ///         }
    ///     }
    /// });
    /// </code>
    /// </example>
    [JsonIgnore]
    public Func<ListRootsRequestParams?, CancellationToken, Task<ListRootsResult>>? RootsHandler { get; set; }
}