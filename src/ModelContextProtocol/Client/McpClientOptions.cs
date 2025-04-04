using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Client;

/// <summary>
/// Configuration options for the MCP client. This is passed to servers during the initialization sequence, letting them know about the client's capabilities and
/// protocol version.
/// <see href="https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle/">See the protocol specification for details on capability negotiation</see>
/// </summary>
public class McpClientOptions
{
    /// <summary>
    /// Information about this client implementation.
    /// </summary>
    public Implementation? ClientInfo { get; set; }

    /// <summary>
    /// Client capabilities to advertise to the server.
    /// </summary>
    public ClientCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Protocol version to request from the server.
    /// </summary>
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>
    /// Timeout for initialization sequence.
    /// </summary>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
