using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Describes the name and version of an MCP implementation.
/// </summary>
/// <remarks>
/// <para>
/// The Implementation class is used to identify MCP clients and servers during the initialization handshake.
/// It provides version and name information that can be used for compatibility checks, logging, and debugging.
/// </para>
/// <para>
/// Both clients and servers provide this information during connection establishment.
/// </para>
/// <para>
/// Example:
/// <code>
/// var implementation = new Implementation 
/// { 
///     Name = "MyMcpClient", 
///     Version = "1.0.0" 
/// };
/// </code>
/// </para>
/// <para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </para>
/// </remarks>
public class Implementation
{
    /// <summary>
    /// Name of the implementation.
    /// </summary>
    /// <remarks>
    /// This is typically the name of the client or server library/application.
    /// </remarks>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Version of the implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This should follow a standard versioning format, like semantic versioning (e.g., "1.0.0").
    /// </para>
    /// <para>
    /// The version is used during client-server handshake to identify implementation versions,
    /// which can be important for troubleshooting compatibility issues or when reporting bugs.
    /// It's not to be confused with the ProtocolVersion which defines the message format compatibility.
    /// </para>
    /// <para>
    /// Example usage with client initialization:
    /// <code>
    /// var clientOptions = new McpClientOptions
    /// {
    ///     ClientInfo = new Implementation { Name = "MyMcpClient", Version = "1.0.0" },
    ///     ProtocolVersion = "2024-11-05"
    /// };
    /// </code>
    /// </para>
    /// <para>
    /// Example usage with server initialization:
    /// <code>
    /// var serverOptions = new McpServerOptions
    /// {
    ///     ServerInfo = new Implementation { Name = "MyMcpServer", Version = "1.0.0" }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    [JsonPropertyName("version")]
    public required string Version { get; set; }
}