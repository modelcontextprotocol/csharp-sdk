using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents a named resource that can be retrieved from an MCP server.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a client-side wrapper around a resource defined on an MCP server. It allows
/// retrieving the resource's content by sending a request to the server with the resource's URI.
/// Instances of this class are typically obtained by calling <see cref="McpClient.ListResourcesAsync(RequestOptions?, CancellationToken)"/>.
/// </para>
/// </remarks>
public sealed class McpClientResource
{
    private readonly McpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientResource"/> class.
    /// </summary>
    /// <param name="client">The <see cref="McpClient"/> instance to use for reading the resource.</param>
    /// <param name="resource">The protocol <see cref="Resource"/> definition describing the resource's metadata.</param>
    /// <remarks>
    /// <para>
    /// This constructor enables reusing cached resource definitions across different <see cref="McpClient"/> instances
    /// without needing to call <see cref="McpClient.ListResourcesAsync(RequestOptions?, CancellationToken)"/> on every reconnect. This is particularly useful
    /// in scenarios where resource definitions are stable and network round-trips should be minimized.
    /// </para>
    /// <para>
    /// The provided <paramref name="resource"/> must represent a resource that is actually available on the server
    /// associated with the <paramref name="client"/>. Attempting to read a resource that doesn't exist on the
    /// server will result in an <see cref="McpException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="resource"/> is <see langword="null"/>.</exception>
    public McpClientResource(McpClient client, Resource resource)
    {
        Throw.IfNull(client);
        Throw.IfNull(resource);

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

    /// <summary>Gets the description of the resource.</summary>
    public string? Description => ProtocolResource.Description;

    /// <summary>Gets the media (MIME) type of the resource.</summary>
    public string? MimeType => ProtocolResource.MimeType;

    /// <summary>
    /// Gets this resource's content by sending a request to the server.
    /// </summary>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ValueTask{ReadResourceResult}"/> containing the resource's result with content and messages.</returns>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This is a convenience method that internally calls <see cref="McpClient.ReadResourceAsync(string, RequestOptions, CancellationToken)"/>.
    /// </para>
    /// </remarks>
    public ValueTask<ReadResourceResult> ReadAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _client.ReadResourceAsync(Uri, options, cancellationToken);
}
