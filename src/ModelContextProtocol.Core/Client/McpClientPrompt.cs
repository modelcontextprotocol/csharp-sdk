using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents a named prompt that can be retrieved from an MCP server and invoked with arguments.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a client-side wrapper around a prompt defined on an MCP server. It allows
/// retrieving the prompt's content by sending a request to the server with optional arguments.
/// Instances of this class are typically obtained by calling <see cref="McpClient.ListPromptsAsync"/>
/// or <see cref="McpClient.EnumeratePromptsAsync"/>.
/// </para>
/// <para>
/// Each prompt has a name and optionally a description, and it can be invoked with arguments
/// to produce customized prompt content from the server.
/// </para>
/// </remarks>
public sealed class McpClientPrompt
{
    private readonly McpClient _client;

    internal McpClientPrompt(McpClient client, Prompt prompt)
    {
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

    /// <summary>Gets a description of the prompt.</summary>
    public string? Description => ProtocolPrompt.Description;

    /// <summary>
    /// Gets this prompt's content by sending a request to the server with optional arguments.
    /// </summary>
    /// <param name="arguments">Optional arguments to pass to the prompt. Keys are parameter names, and values are the argument values.</param>
    /// <param name="serializerOptions">The serialization options governing argument serialization.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ValueTask"/> containing the prompt's result with content and messages.</returns>
    /// <remarks>
    /// <para>
    /// This method sends a request to the MCP server to execute this prompt with the provided arguments.
    /// The server will process the request and return a result containing messages or other content.
    /// </para>
    /// <para>
    /// This is a convenience method that internally calls <see cref="McpClient.GetPromptAsync"/> 
    /// with this prompt's name and arguments.
    /// </para>
    /// </remarks>
    public async ValueTask<GetPromptResult> GetAsync(
        IEnumerable<KeyValuePair<string, object?>>? arguments = null,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, object?>? argDict =
            arguments as IReadOnlyDictionary<string, object?> ??
            arguments?.ToDictionary();

        return await _client.GetPromptAsync(ProtocolPrompt.Name, argDict, serializerOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}