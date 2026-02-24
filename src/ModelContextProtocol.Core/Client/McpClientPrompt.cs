using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents a named prompt that can be retrieved from an MCP server and invoked with arguments.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a client-side wrapper around a prompt defined on an MCP server. It allows
/// retrieving the prompt's content by sending a request to the server with optional arguments.
/// Instances of this class are typically obtained by calling <see cref="McpClient.ListPromptsAsync(RequestOptions?, CancellationToken)"/>.
/// </para>
/// <para>
/// Each prompt has a name and optionally a description, and it can be invoked with arguments
/// to produce customized prompt content from the server.
/// </para>
/// </remarks>
public sealed class McpClientPrompt
{
    private readonly McpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientPrompt"/> class.
    /// </summary>
    /// <param name="client">The <see cref="McpClient"/> instance to use for invoking the prompt.</param>
    /// <param name="prompt">The protocol <see cref="Prompt"/> definition describing the prompt's metadata.</param>
    /// <remarks>
    /// <para>
    /// This constructor enables reusing cached prompt definitions across different <see cref="McpClient"/> instances
    /// without needing to call <see cref="McpClient.ListPromptsAsync(RequestOptions?, CancellationToken)"/> on every reconnect. This is particularly useful
    /// in scenarios where prompt definitions are stable and network round-trips should be minimized.
    /// </para>
    /// <para>
    /// The provided <paramref name="prompt"/> must represent a prompt that is actually available on the server
    /// associated with the <paramref name="client"/>. Attempting to invoke a prompt that doesn't exist on the
    /// server will result in an <see cref="McpException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="prompt"/> is <see langword="null"/>.</exception>
    public McpClientPrompt(McpClient client, Prompt prompt)
    {
        Throw.IfNull(client);
        Throw.IfNull(prompt);

        _client = client;
        ProtocolPrompt = prompt;
    }

    /// <summary>Gets the underlying protocol <see cref="Prompt"/> type for this instance.</summary>
    /// <remarks>
    /// <para>
    /// This property provides direct access to the underlying protocol representation of the prompt,
    /// which can be useful for advanced scenarios or when implementing custom MCP client extensions.
    /// </para>
    /// <para>
    /// For most common use cases, you can use the more convenient <see cref="Name"/> and
    /// <see cref="Description"/> properties instead of accessing the <see cref="ProtocolPrompt"/> directly.
    /// </para>
    /// </remarks>
    public Prompt ProtocolPrompt { get; }

    /// <summary>Gets the name of the prompt.</summary>
    public string Name => ProtocolPrompt.Name;

    /// <summary>Gets the title of the prompt.</summary>
    public string? Title => ProtocolPrompt.Title;

    /// <summary>Gets the description of the prompt.</summary>
    public string? Description => ProtocolPrompt.Description;

    /// <summary>
    /// Gets this prompt's content by sending a request to the server with optional arguments.
    /// </summary>
    /// <param name="arguments">Optional arguments to pass to the prompt. Keys are parameter names, and values are the argument values.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ValueTask"/> containing the prompt's result with content and messages.</returns>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This method sends a request to the MCP server to execute this prompt with the provided arguments.
    /// The server will process the request and return a result containing messages or other content.
    /// </para>
    /// <para>
    /// This is a convenience method that internally calls 
    /// <see cref="McpClient.GetPromptAsync(string, IReadOnlyDictionary{string, object?}?, RequestOptions?, CancellationToken)"/>
    /// with this prompt's name and arguments.
    /// </para>
    /// </remarks>
    public async ValueTask<GetPromptResult> GetAsync(
        IEnumerable<KeyValuePair<string, object?>>? arguments = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, object?>? argDict =
            arguments as IReadOnlyDictionary<string, object?> ??
            arguments?.ToDictionary();

        return await _client.GetPromptAsync(ProtocolPrompt.Name, argDict, options, cancellationToken).ConfigureAwait(false);
    }
}
