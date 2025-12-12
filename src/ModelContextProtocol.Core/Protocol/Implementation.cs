using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

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
public sealed class Implementation : IBaseMetadata
{
    /// <inheritdoc />
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the version of the implementation.
    /// </summary>
    /// <remarks>
    /// The version is used during client-server handshake to identify implementation versions,
    /// which can be important for troubleshooting compatibility issues or when reporting bugs.
    /// </remarks>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>
    /// Gets or sets an optional description of the implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This description helps users and developers understand what the implementation provides
    /// and its purpose. It should clearly explain the functionality and capabilities offered.
    /// </para>
    /// <para>
    /// The description is typically used in documentation, UI displays, and for providing context
    /// to users about the server or client they are interacting with.
    /// </para>
    /// </remarks>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets an optional list of icons for this implementation.
    /// </summary>
    /// <remarks>
    /// This value can be used by clients to display the implementation's icon in a user interface.
    /// </remarks>
    [JsonPropertyName("icons")]
    public IList<Icon>? Icons { get; set; }

    /// <summary>
    /// Gets or sets an optional URL of the website for this implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This URL can be used by clients to link to documentation or more information about the implementation.
    /// </para>
    /// <para>
    /// Consumers SHOULD take steps to ensure URLs are from the same domain as the client/server
    /// or a trusted domain to prevent security issues.
    /// </para>
    /// </remarks>
    [JsonPropertyName("websiteUrl")]
    public string? WebsiteUrl { get; set; }
}
