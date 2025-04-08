using ModelContextProtocol.Protocol.Messages;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the capabilities that a client may support.
/// </summary>
/// <remarks>
/// <para>
/// Capabilities define the features and functionality that a client can handle when communicating with an MCP server.
/// These are advertised to the server during the initialize handshake.
/// </para>
/// <para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </para>
/// </remarks>
public class ClientCapabilities
{
    /// <summary>
    /// Experimental, non-standard capabilities that the client supports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Experimental dictionary allows clients to advertise support for features that are not yet 
    /// standardized in the Model Context Protocol specification. This extension mechanism enables 
    /// future protocol enhancements while maintaining backward compatibility.
    /// </para>
    /// <para>
    /// Values in this dictionary are implementation-specific and should be coordinated between client 
    /// and server implementations. Servers should not assume the presence of any experimental capability 
    /// without checking for it first.
    /// </para>
    /// </remarks>
    [JsonPropertyName("experimental")]
    public Dictionary<string, object>? Experimental { get; set; }

    /// <summary>
    /// Present if the client supports listing roots, which are entry points for resource navigation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When this capability is set, the client indicates that it can respond to server requests
    /// for listing root URIs. Root URIs serve as entry points for resource navigation in the protocol.
    /// </para>
    /// <para>
    /// The server can use <see cref="Server.McpServerExtensions.RequestRootsAsync"/> to request the list of
    /// available roots from the client, which will trigger the client's <see cref="RootsCapability.RootsHandler"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Setting up client capabilities with roots support
    /// var clientOptions = new McpClientOptions
    /// {
    ///     Capabilities = new ClientCapabilities
    ///     {
    ///         Roots = new RootsCapability
    ///         {
    ///             RootsHandler = (request, token) => 
    ///             {
    ///                 return Task.FromResult(new ListRootsResult
    ///                 {
    ///                     Roots = new List&lt;Root&gt;
    ///                     {
    ///                         new Root { Uri = "mcp://mymodel/", Name = "My Model" },
    ///                         new Root { Uri = "mcp://another-model/", Name = "Another Model" }
    ///                     }
    ///                 });
    ///             }
    ///         }
    ///     }
    /// };
    /// </code>
    /// </example>
    /// <seealso cref="RootsCapability"/>
    /// <seealso cref="ListRootsRequestParams"/>
    /// <seealso cref="ListRootsResult"/>
    /// <seealso cref="Root"/>
    /// <remarks>
    /// <para>
    /// When this capability is present, the client can respond to server requests for listing available root URIs.
    /// Roots typically represent top-level directories or container resources that can be accessed and traversed
    /// within the Model Context Protocol.
    /// </para>
    /// <para>
    /// The server can request the list of roots using the <see cref="RequestMethods.RootsList"/> method,
    /// and the client responds with a <see cref="ListRootsResult"/> containing the available roots.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Setting up client capabilities with roots support
    /// var clientOptions = new McpClientOptions
    /// {
    ///     Capabilities = new ClientCapabilities
    ///     {
    ///         Roots = new RootsCapability
    ///         {
    ///             RootsHandler = (request, token) => 
    ///             {
    ///                 return Task.FromResult(new ListRootsResult
    ///                 {
    ///                     Roots = new List&lt;Root&gt;
    ///                     {
    ///                         new Root { Uri = "mcp://mymodel/", Name = "My Model" },
    ///                         new Root { Uri = "mcp://another-model/", Name = "Another Model" }
    ///                     }
    ///                 });
    ///             }
    ///         }
    ///     }
    /// };
    /// </code>
    /// </example>
    /// <seealso cref="RootsCapability"/>
    /// <seealso cref="ListRootsRequestParams"/>
    /// <seealso cref="ListRootsResult"/>
    /// <seealso cref="Root"/>
    [JsonPropertyName("roots")]
    public RootsCapability? Roots { get; set; }

    /// <summary>
    /// Present if the client supports sampling from an LLM.
    /// </summary>
    [JsonPropertyName("sampling")]
    public SamplingCapability? Sampling { get; set; }

    /// <summary>Gets or sets notification handlers to register with the client.</summary>
    /// <remarks>
    /// <para>
    /// When constructed, the client will enumerate these handlers once, which may contain multiple handlers per notification method key.
    /// The client will not re-enumerate the sequence after initialization.
    /// </para>
    /// <para>
    /// Notification handlers allow the client to respond to server-sent notifications for specific methods.
    /// Each key in the collection is a notification method name, and each value is a callback that will be invoked
    /// when a notification with that method is received.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var clientOptions = new McpClientOptions
    /// {
    ///     Capabilities = new ClientCapabilities
    ///     {
    ///         NotificationHandlers =
    ///         [
    ///             new(NotificationMethods.ResourceUpdatedNotification, (notification, cancellationToken) =>
    ///             {
    ///                 var notificationParams = JsonSerializer.Deserialize&lt;ResourceUpdatedNotificationParams&gt;(notification.Params);
    ///                 Console.WriteLine($"Resource updated: {notificationParams?.ResourceUri}");
    ///                 return Task.CompletedTask;
    ///             })
    ///         ]
    ///     }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public IEnumerable<KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, Task>>>? NotificationHandlers { get; set; }
}