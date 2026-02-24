using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents a named resource template that can be retrieved from an MCP server.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a client-side wrapper around a resource template defined on an MCP server. It allows
/// retrieving the resource template's content by sending a request to the server with the resource's URI.
/// Instances of this class are typically obtained by calling <see cref="McpClient.ListResourceTemplatesAsync(RequestOptions?, CancellationToken)"/>.
/// </para>
/// </remarks>
public sealed class McpClientResourceTemplate
{
    private readonly McpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientResourceTemplate"/> class.
    /// </summary>
    /// <param name="client">The <see cref="McpClient"/> instance to use for reading the resource template.</param>
    /// <param name="resourceTemplate">The protocol <see cref="ResourceTemplate"/> definition describing the resource template's metadata.</param>
    /// <remarks>
    /// <para>
    /// This constructor enables reusing cached resource template definitions across different <see cref="McpClient"/> instances
    /// without needing to call <see cref="McpClient.ListResourceTemplatesAsync(RequestOptions?, CancellationToken)"/> on every reconnect. This is particularly useful
    /// in scenarios where resource template definitions are stable and network round-trips should be minimized.
    /// </para>
    /// <para>
    /// The provided <paramref name="resourceTemplate"/> must represent a resource template that is actually available on the server
    /// associated with the <paramref name="client"/>. Attempting to read a resource template that doesn't exist on the
    /// server will result in an <see cref="McpException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="resourceTemplate"/> is <see langword="null"/>.</exception>
    public McpClientResourceTemplate(McpClient client, ResourceTemplate resourceTemplate)
    {
        Throw.IfNull(client);
        Throw.IfNull(resourceTemplate);

        _client = client;
        ProtocolResourceTemplate = resourceTemplate;
    }

    /// <summary>Gets the underlying protocol <see cref="ResourceTemplate"/> type for this instance.</summary>
    /// <remarks>
    /// <para>
    /// This property provides direct access to the underlying protocol representation of the resource template,
    /// which can be useful for advanced scenarios or when implementing custom MCP client extensions.
    /// </para>
    /// <para>
    /// For most common use cases, you can use the more convenient <see cref="UriTemplate"/> and
    /// <see cref="Description"/> properties instead of accessing the <see cref="ProtocolResourceTemplate"/> directly.
    /// </para>
    /// </remarks>
    public ResourceTemplate ProtocolResourceTemplate { get; }

    /// <summary>Gets the URI template of the resource template.</summary>
    public string UriTemplate => ProtocolResourceTemplate.UriTemplate;

    /// <summary>Gets the name of the resource template.</summary>
    public string Name => ProtocolResourceTemplate.Name;

    /// <summary>Gets the title of the resource template.</summary>
    public string? Title => ProtocolResourceTemplate.Title;

    /// <summary>Gets the description of the resource template.</summary>
    public string? Description => ProtocolResourceTemplate.Description;

    /// <summary>Gets the media (MIME) type of the resource template.</summary>
    public string? MimeType => ProtocolResourceTemplate.MimeType;

    /// <summary>
    /// Gets this resource template's content by formatting a URI from the template and supplied arguments
    /// and sending a request to the server.
    /// </summary>
    /// <param name="arguments">A dictionary of arguments to pass to the tool. Each key represents a parameter name,
    /// and its associated value represents the argument value.
    /// </param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ValueTask{ReadResourceResult}"/> containing the resource template's result with content and messages.</returns>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<ReadResourceResult> ReadAsync(
        IReadOnlyDictionary<string, object?> arguments,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _client.ReadResourceAsync(UriTemplate, arguments, options, cancellationToken);
}
