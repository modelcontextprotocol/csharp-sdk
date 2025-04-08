using ModelContextProtocol.Protocol.Messages;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the capabilities that a server may support.
/// </summary>
/// <remarks>
/// <para>
/// Server capabilities define the features and functionality available when clients connect.
/// These capabilities are advertised to clients during the initialize handshake.
/// </para>
/// <para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </para>
/// </remarks>
public class ServerCapabilities
{
    /// <summary>
    /// Experimental, non-standard capabilities that the server supports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Experimental dictionary allows servers to advertise support for features that are not yet 
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
    public Dictionary<string, object>? Experimental { get; set; }

    /// <summary>
    /// Present if the server supports sending log messages to the client.
    /// </summary>
    [JsonPropertyName("logging")]
    public LoggingCapability? Logging { get; set; }

    /// <summary>
    /// Present if the server supports predefined prompt templates that clients can discover and use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When configured, this capability enables clients to:
    /// <list type="bullet">
    ///   <item>
    ///     <description>List available prompts through the prompts/list endpoint</description>
    ///   </item>
    ///   <item>
    ///     <description>Retrieve specific prompts through the prompts/get endpoint</description>
    ///   </item>
    ///   <item>
    ///     <description>Receive notifications when the available prompts change (if <see cref="PromptsCapability.ListChanged"/> is true)</description>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// Prompts can be defined statically in the <see cref="PromptsCapability.PromptCollection"/> or
    /// dynamically generated through handlers. This capability is useful for servers that want to
    /// provide standardized, reusable prompts that clients can easily incorporate into their workflows.
    /// </para>
    /// </remarks>
    [JsonPropertyName("prompts")]
    public PromptsCapability? Prompts { get; set; }

    /// <summary>
    /// Present if the server offers any resources to read. Resources are sources of information that
    /// can be accessed by the client through URI-based identifiers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When configured, this capability enables clients to:
    /// <list type="bullet">
    ///   <item>
    ///     <description>List available resources through the resources/list endpoint</description>
    ///   </item>
    ///   <item>
    ///     <description>Read resource contents through the resources/read endpoint</description>
    ///   </item>
    ///   <item>
    ///     <description>Subscribe to resource updates if the server supports it</description>
    ///   </item>
    ///   <item>
    ///     <description>Receive notifications when resources change (if subscription is enabled)</description>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// Resources can represent various types of data such as documents, files, database records, or any
    /// other content that may be relevant to the client. The server can define how resources are accessed
    /// and what types of resources are available.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var serverOptions = new McpServerOptions
    /// {
    ///     Capabilities = new ServerCapabilities
    ///     {
    ///         Resources = new ResourcesCapability
    ///         {
    ///             Subscribe = true,
    ///             ListResourcesHandler = (context, cancellationToken) =>
    ///             {
    ///                 return Task.FromResult(new ListResourcesResult
    ///                 {
    ///                     Resources = 
    ///                     [
    ///                         new Resource
    ///                         {
    ///                             Uri = "document://123",
    ///                             Name = "Sample Document",
    ///                             Description = "A sample document resource"
    ///                         }
    ///                     ]
    ///                 });
    ///             },
    ///             ReadResourceHandler = (context, cancellationToken) =>
    ///             {
    ///                 // Handle resource reading logic
    ///                 return Task.FromResult(new ReadResourceResult());
    ///             }
    ///         }
    ///     }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    [JsonPropertyName("resources")]
    public ResourcesCapability? Resources { get; set; }

    /// <summary>
    /// Present if the server offers any tools to call.
    /// </summary>
    [JsonPropertyName("tools")]
    public ToolsCapability? Tools { get; set; }

    /// <summary>
    /// Present if the server supports argument autocompletion suggestions.
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
    /// Example usage:
    /// <code>
    /// var serverOptions = new McpServerOptions
    /// {
    ///     Capabilities = new ServerCapabilities
    ///     {
    ///         NotificationHandlers =
    ///         [
    ///             new(NotificationMethods.InitializedNotification, (notification, cancellationToken) =>
    ///             {
    ///                 Console.WriteLine("Client successfully initialized");
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
