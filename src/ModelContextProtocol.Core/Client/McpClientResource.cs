using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents a named resource that can be retrieved from an MCP server.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a client-side wrapper around a resource defined on an MCP server. It allows
/// retrieving the resource's content by sending a request to the server with the resource's URI.
/// Instances of this class are typically obtained by calling <see cref="McpClient.ListResourcesAsync"/>
/// or <see cref="McpClient.EnumerateResourcesAsync"/>.
/// </para>
/// </remarks>
public sealed class McpClientResource
{
    private readonly McpClient _client;

    internal McpClientResource(McpClient client, Resource resource)
    {
        _client = client;
        ProtocolResource = resource;
    }

    /// <summary>Gets the underlying protocol <see cref="Resource"/> type for this instance.</summary>
    /// <remarks>
    /// <para>
    /// This property provides direct access to the underlying protocol representation of the resource,
    /// which can be useful for advanced scenarios or when implementing custom MCP client extensions.
    /// </para>
    /// <para>
    /// For most common use cases, you can use the more convenient <see cref="Name"/> and 
    /// <see cref="Description"/> properties instead of accessing the <see cref="ProtocolResource"/> directly.
    /// </para>
    /// </remarks>
    public Resource ProtocolResource { get; }

    /// <summary>Gets the URI of the resource.</summary>
    public string Uri => ProtocolResource.Uri;

    /// <summary>Gets the name of the resource.</summary>
    public string Name => ProtocolResource.Name;

    /// <summary>Gets the title of the resource.</summary>
    public string? Title => ProtocolResource.Title;

    /// <summary>Gets a description of the resource.</summary>
    public string? Description => ProtocolResource.Description;

    /// <summary>Gets a media (MIME) type of the resource.</summary>
    public string? MimeType => ProtocolResource.MimeType;

    /// <summary>
    /// Gets this resource's content by sending a request to the server.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ValueTask{ReadResourceResult}"/> containing the resource's result with content and messages.</returns>
    /// <remarks>
    /// <para>
    /// This is a convenience method that internally calls <see cref="McpClient.ReadResourceAsync(string, CancellationToken)"/>.
    /// </para>
    /// </remarks>
    public ValueTask<ReadResourceResult> ReadAsync(
        CancellationToken cancellationToken = default) =>
        _client.ReadResourceAsync(Uri, cancellationToken);
}