using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capabilities that a client may support.
/// </summary>
/// <remarks>
/// <para>
/// Capabilities define the features and functionality that a client can handle when communicating with an MCP server.
/// These are advertised to the server during the initialize handshake.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class ClientCapabilities
{
    /// <summary>
    /// Gets or sets experimental, non-standard capabilities that the client supports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Experimental"/> dictionary allows clients to advertise support for features that are not yet 
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
    public IDictionary<string, object>? Experimental { get; set; }

    /// <summary>
    /// Gets or sets the client's roots capability, which are entry points for resource navigation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="Roots"/> is non-<see langword="null"/>, the client indicates that it can respond to 
    /// server requests for listing root URIs. Root URIs serve as entry points for resource navigation in the protocol.
    /// </para>
    /// <para>
    /// The server can use <see cref="McpServer.RequestRootsAsync"/> to request the list of
    /// available roots from the client, which will trigger the client's <see cref="RootsCapability.RootsHandler"/>.
    /// </para>
    /// </remarks>
    [JsonPropertyName("roots")]
    public RootsCapability? Roots { get; set; }

    /// <summary>
    /// Gets or sets the client's sampling capability, which indicates whether the client 
    /// supports issuing requests to an LLM on behalf of the server.
    /// </summary>
    [JsonPropertyName("sampling")]
    public SamplingCapability? Sampling { get; set; }

    /// <summary>
    /// Gets or sets the client's elicitation capability, which indicates whether the client 
    /// supports elicitation of additional information from the user on behalf of the server.
    /// </summary>
    [JsonPropertyName("elicitation")]
    public ElicitationCapability? Elicitation { get; set; }

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
    /// Handlers provided via <see cref="NotificationHandlers"/> will be registered with the client for the lifetime of the client.
    /// For transient handlers, <see cref="McpSession.RegisterNotificationHandler"/> may be used to register a handler that can
    /// then be unregistered by disposing of the <see cref="IAsyncDisposable"/> returned from the method.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public IEnumerable<KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>>? NotificationHandlers { get; set; }
}