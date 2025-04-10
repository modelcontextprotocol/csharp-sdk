using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Provides the name and version of an MCP implementation.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Implementation"/> class is used to identify MCP clients and servers during the initialization handshake.
/// It provides version and name information that can be used for compatibility checks, logging, and debugging.
/// </para>
/// <para>
/// Both clients and servers provide this information during connection establishment.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public class Implementation
{
    /// <summary>
    /// Gets or sets the name of the implementation.
    /// </summary>
    /// <remarks>
    /// This is typically the name of the client or server library/application.
    /// </remarks>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the version of the implementation.
    /// </summary>
    /// <remarks>
    /// The version is used during client-server handshake to identify implementation versions,
    /// which can be important for troubleshooting compatibility issues or when reporting bugs.
    /// </remarks>
    [JsonPropertyName("version")]
    public required string Version { get; set; }
}