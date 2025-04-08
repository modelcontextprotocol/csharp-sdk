using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Client;

/// <summary>
/// Configuration options for the MCP client. This is passed to servers during the initialization sequence, letting them know about the client's capabilities and
/// protocol version.
/// <see href="https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle/">See the protocol specification for details on capability negotiation</see>
/// </summary>
/// <remarks>
/// <para>
/// These options are typically passed to <see cref="McpClientFactory.CreateAsync"/> when creating a client.
/// They define client capabilities, protocol version, and other client-specific settings.
/// </para>
/// <para>
/// Example:
/// <code>
/// // Create client options with custom settings
/// var clientOptions = new McpClientOptions
/// {
///     ClientInfo = new Implementation { Name = "MyMcpClient", Version = "1.0.0" },
///     Capabilities = new ClientCapabilities
///     {
///         Sampling = new SamplingCapability(),
///         // Add other capabilities as needed
///     },
///     ProtocolVersion = "2024-11-05",
///     InitializationTimeout = TimeSpan.FromSeconds(30)
/// };
/// 
/// // Use the options when creating a client
/// var client = await McpClientFactory.CreateAsync(
///     serverConfig,
///     clientOptions);
/// </code>
/// </para>
/// </remarks>
public class McpClientOptions
{
    /// <summary>
    /// Information about this client implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This information is sent to the server during initialization to identify the client.
    /// It's displayed in server logs and can be used for debugging and compatibility checks.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var options = new McpClientOptions
    /// {
    ///     ClientInfo = new Implementation { Name = "MyMcpClient", Version = "1.0.0" }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    public Implementation? ClientInfo { get; set; }

    /// <summary>
    /// Client capabilities to advertise to the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These define the features the client supports when connecting to a server.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var options = new McpClientOptions
    /// {
    ///     Capabilities = new ClientCapabilities
    ///     {
    ///         Sampling = new SamplingCapability()
    ///     }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    public ClientCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Protocol version to request from the server, using a date-based versioning scheme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The protocol version is a key part of the initialization handshake. The client and server must 
    /// agree on a compatible protocol version to communicate successfully. If the server doesn't support
    /// the requested version, it will throw a version mismatch exception.
    /// </para>
    /// <para>
    /// This uses a date-based versioning scheme in the format "YYYY-MM-DD".
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var options = new McpClientOptions
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
    /// This timeout determines how long the client will wait for the server to respond during
    /// the initialization protocol handshake. If the server doesn't respond within this timeframe,
    /// a <see cref="ModelContextProtocol.McpException"/> will be thrown with a "Initialization timed out" message.
    /// </para>
    /// <para>
    /// Setting an appropriate timeout prevents the client from hanging indefinitely when
    /// connecting to unresponsive servers.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var options = new McpClientOptions
    /// {
    ///     InitializationTimeout = TimeSpan.FromSeconds(30) // Set shorter timeout for faster failure detection
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
