using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capabilities that a server may support.
/// </summary>
/// <remarks>
/// <para>
/// Server capabilities define the features and functionality available when clients connect.
/// These capabilities are advertised to clients during the initialize handshake.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class ServerCapabilities
{
    /// <summary>
    /// Gets or sets experimental, non-standard capabilities that the server supports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Experimental"/> dictionary allows servers to advertise support for features that are not yet 
    /// standardized in the Model Context Protocol specification. This extension mechanism enables 
    /// future protocol enhancements while maintaining backward compatibility.
    /// </para>
    /// <para>
    /// Values in this dictionary are implementation-specific and should be coordinated between client 
    /// and server implementations. Clients should not assume the presence of any experimental capability 
    /// without checking for it first.
    /// </para>
    /// </remarks>
    [JsonPropertyName("experimental")]
    public IDictionary<string, object>? Experimental { get; set; }

    /// <summary>
    /// Gets or sets a server's logging capability, supporting sending log messages to the client.
    /// </summary>
    [JsonPropertyName("logging")]
    public LoggingCapability? Logging { get; set; }

    /// <summary>
    /// Gets or sets a server's prompts capability for serving predefined prompt templates that clients can discover and use.
    /// </summary>
    [JsonPropertyName("prompts")]
    public PromptsCapability? Prompts { get; set; }

    /// <summary>
    /// Gets or sets a server's resources capability for serving predefined resources that clients can discover and use.
    /// </summary>
    [JsonPropertyName("resources")]
    public ResourcesCapability? Resources { get; set; }

    /// <summary>
    /// Gets or sets a server's tools capability for listing tools that a client is able to invoke.
    /// </summary>
    [JsonPropertyName("tools")]
    public ToolsCapability? Tools { get; set; }

    /// <summary>
    /// Gets or sets a server's completions capability for supporting argument auto-completion suggestions.
    /// </summary>
    [JsonPropertyName("completions")]
    public CompletionsCapability? Completions { get; set; }

    /// <summary>Gets or sets notification handlers to register with the server.</summary>
    /// <remarks>
    /// <para>
    /// When constructed, the server will enumerate these handlers once, which may contain multiple handlers per notification method key.
    /// The server will not re-enumerate the sequence after initialization.
    /// </para>
    /// <para>
    /// Notification handlers allow the server to respond to client-sent notifications for specific methods.
    /// Each key in the collection is a notification method name, and each value is a callback that will be invoked
    /// when a notification with that method is received.
    /// </para>
    /// <para>
    /// Handlers provided via <see cref="NotificationHandlers"/> will be registered with the server for the lifetime of the server.
    /// For transient handlers, <see cref="McpSession.RegisterNotificationHandler"/> may be used to register a handler that can
    /// then be unregistered by disposing of the <see cref="IAsyncDisposable"/> returned from the method.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public IEnumerable<KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>>? NotificationHandlers { get; set; }
}
