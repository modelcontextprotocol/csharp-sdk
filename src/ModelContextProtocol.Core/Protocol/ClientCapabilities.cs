using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capabilities that a client supports.
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
    /// available roots from the client, which will trigger the client's <see cref="McpClientHandlers.RootsHandler"/>.
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

    /// <summary>
    /// Gets or sets the client's tasks capability for supporting task-augmented requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tasks capability enables servers to augment their requests with tasks for long-running
    /// operations. When present, servers can request that certain operations (like sampling or
    /// elicitation) execute asynchronously, with the ability to poll for status and retrieve results later.
    /// </para>
    /// <para>
    /// See <see cref="McpTasksCapability"/> for details on configuring which operations support tasks.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    [JsonIgnore]
    public McpTasksCapability? Tasks
    {
        get => TasksCore;
        set => TasksCore = value;
    }

    // See ExperimentalInternalPropertyTests.cs before modifying this property.
    [JsonInclude]
    [JsonPropertyName("tasks")]
    internal McpTasksCapability? TasksCore { get; set; }

    /// <summary>
    /// Gets or sets optional MCP extensions that the client supports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keys are extension identifiers in reverse domain notation with an extension name
    /// (e.g., <c>"io.modelcontextprotocol/oauth-client-credentials"</c>), and values are
    /// per-extension settings objects. An empty object indicates support with no additional settings.
    /// </para>
    /// <para>
    /// Extensions provide a framework for extending the Model Context Protocol while maintaining
    /// interoperability. Clients advertise extension support via this field during the initialization handshake.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Extensions_DiagnosticId, UrlFormat = Experimentals.Extensions_Url)]
    [JsonIgnore]
    public IDictionary<string, object>? Extensions
    {
        get => ExtensionsCore;
        set => ExtensionsCore = value;
    }

    // See ExperimentalInternalPropertyTests.cs before modifying this property.
    [JsonInclude]
    [JsonPropertyName("extensions")]
    internal IDictionary<string, object>? ExtensionsCore { get; set; }
}
