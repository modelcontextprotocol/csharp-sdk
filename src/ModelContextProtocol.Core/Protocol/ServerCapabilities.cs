using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capabilities that a server supports.
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
    /// Gets or sets a server's logging capability for sending log messages to the client.
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

    /// <summary>
    /// Gets or sets a server's tasks capability for supporting task-augmented requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tasks capability enables clients to augment their requests with tasks for long-running
    /// operations. When present, clients can request that certain operations (like tool calls)
    /// execute asynchronously, with the ability to poll for status and retrieve results later.
    /// </para>
    /// <para>
    /// See <see cref="McpTasksCapability"/> for details on configuring which operations support tasks.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    [JsonIgnore]
    public McpTasksCapability? Tasks
    {
        get => _tasks as McpTasksCapability;
        set => _tasks = value;
    }

    [JsonInclude]
    [JsonPropertyName("tasks")]
    [JsonConverter(typeof(ExperimentalJsonConverter<McpTasksCapability>))]
    internal object? _tasks;
}
