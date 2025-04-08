
using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Server;

/// <summary>
/// Configuration options for the MCP server. This is passed to the client during the initialization sequence, letting it know about the server's capabilities and
/// protocol version.
/// <see href="https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle/">See the protocol specification for details on capability negotiation</see>
/// </summary>
public class McpServerOptions
{
    /// <summary>
    /// Information about this server implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This information is sent to the client during initialization to identify the server.
    /// It's displayed in client logs and can be used for debugging and compatibility checks.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var options = new McpServerOptions
    /// {
    ///     ServerInfo = new Implementation { Name = "MyMcpServer", Version = "1.0.0" }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    public Implementation? ServerInfo { get; set; }

    /// <summary>
    /// Server capabilities to advertise to the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These determine which features will be available when a client connects.
    /// Capabilities can include tools, prompts, resources, logging settings, and other 
    /// protocol-specific functionality.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var options = new McpServerOptions
    /// {
    ///     Capabilities = new ServerCapabilities
    ///     {
    ///         Tools = new ToolsCapability(),
    ///         Logging = new LoggingCapability()
    ///     }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    public ServerCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Protocol version supported by this server, using a date-based versioning scheme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The protocol version defines which features and message formats this server supports.
    /// During initialization, the server will compare this version with the client's requested
    /// version to ensure compatibility.
    /// </para>
    /// <para>
    /// This uses a date-based versioning scheme in the format "YYYY-MM-DD". The server will reject
    /// connections from clients requesting incompatible protocol versions.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var options = new McpServerOptions
    /// {
    ///     ProtocolVersion = "2024-11-05" // November 5, 2024 protocol version
    /// };
    /// </code>
    /// </para>
    /// <see href="https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle/">See the protocol specification for version details</see>
    /// </remarks>
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>
    /// Timeout for the client-server initialization handshake sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This timeout determines how long the server will wait for client responses during
    /// the initialization protocol handshake. If the client doesn't respond within this timeframe,
    /// the initialization process will be aborted.
    /// </para>
    /// <para>
    /// Setting an appropriate timeout prevents the server from allocating resources indefinitely
    /// when clients fail to complete the initialization protocol.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var options = new McpServerOptions
    /// {
    ///     InitializationTimeout = TimeSpan.FromSeconds(30) // Use shorter timeout in constrained environments
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Optional server instructions to send to clients.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These instructions are sent to clients during the initialization handshake and provide
    /// guidance on how to effectively use the server's capabilities. They can include details
    /// about available tools, expected input formats, limitations, or other helpful information.
    /// </para>
    /// <para>
    /// Client applications typically use these instructions as system messages for LLM interactions
    /// to provide context about available functionality.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var options = new McpServerOptions
    /// {
    ///     ServerInstructions = "This server provides image generation and analysis tools. " +
    ///                         "Use 'generateImage' with a text prompt or 'analyzeImage' with an image URL."
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    public string ServerInstructions { get; set; } = string.Empty;
}
