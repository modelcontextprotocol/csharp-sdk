using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides configuration options for the MCP server.
/// </summary>
public class McpServerOptions
{
    /// <summary>
    /// Gets or sets information about this server implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// This information is sent to the client during initialization to identify the server.
    /// It's displayed in client logs and can be used for debugging and compatibility checks.
    /// </remarks>
    public Implementation? ServerInfo { get; set; }

    /// <summary>
    /// Gets or sets server capabilities to advertise to the client.
    /// </summary>
    /// <remarks>
    /// These determine which features will be available when a client connects.
    /// Capabilities can include "tools", "prompts", "resources", "logging", and other 
    /// protocol-specific functionality.
    /// </remarks>
    public ServerCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Gets or sets the protocol version supported by this server, using a date-based versioning scheme.
    /// </summary>
    /// <remarks>
    /// The protocol version defines which features and message formats this server supports.
    /// This uses a date-based versioning scheme in the format "YYYY-MM-DD".
    /// </remarks>
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>
    /// Gets or sets a timeout used for the client-server initialization handshake sequence.
    /// </summary>
    /// <remarks>
    /// This timeout determines how long the server will wait for client responses during
    /// the initialization protocol handshake. If the client doesn't respond within this timeframe,
    /// the initialization process will be aborted.
    /// </remarks>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets optional server instructions to send to clients.
    /// </summary>
    /// <remarks>
    /// These instructions are sent to clients during the initialization handshake and provide
    /// guidance on how to effectively use the server's capabilities. They can include details
    /// about available tools, expected input formats, limitations, or other helpful information.
    /// Client applications typically use these instructions as system messages for LLM interactions
    /// to provide context about available functionality.
    /// </remarks>
    public string? ServerInstructions { get; set; }
}
