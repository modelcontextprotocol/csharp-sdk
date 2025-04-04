
using ModelContextProtocol.Protocol.Types;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Server;

/// <summary>
/// Configuration options for the MCP server. This is passed to the client during the initialization sequence, letting it know about the server's capabilities and
/// protocol version.
/// <see href="https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle/">See the protocol specification for details on capability negotiation</see>
/// </summary>
public class McpServerOptions
{
    /// <summary>
    /// Information about this server implementation.
    /// </summary>
    public Implementation? ServerInfo { get; set; }

    /// <summary>
    /// Server capabilities to advertise to the server.
    /// </summary>
    public ServerCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Protocol version to request from the server.
    /// </summary>
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>
    /// Timeout for initialization sequence.
    /// </summary>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Optional server instructions to send to clients
    /// </summary>
    public string ServerInstructions { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the handler for get completion requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<CompleteRequestParams>, CancellationToken, Task<CompleteResult>>? GetCompletionHandler { get; set; }
}
