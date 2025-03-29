using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Client;

/// <summary>Provides an invocable prompt.</summary>
public sealed class McpClientPrompt
{
    private readonly IMcpClient _client;

    internal McpClientPrompt(IMcpClient client, Prompt prompt)
    {
        _client = client;
        ProtocolPrompt = prompt;
    }

    /// <summary>Gets the protocol <see cref="Prompt"/> type for this instance.</summary>
    public Prompt ProtocolPrompt { get; }

    /// <summary>
    /// Retrieves a specific prompt with optional arguments.
    /// </summary>
    /// <param name="arguments">Optional arguments for the prompt</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the prompt's content and messages.</returns>
    public async ValueTask<GetPromptResult> GetAsync(
        IEnumerable<KeyValuePair<string, object?>>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, object?>? argDict =
            arguments as IReadOnlyDictionary<string, object?> ??
            arguments?.ToDictionary();

        return await _client.GetPromptAsync(ProtocolPrompt.Name, argDict, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the name of the prompt.</summary>
    public string Name => ProtocolPrompt.Name;

    /// <summary>Gets a description of the prompt.</summary>
    public string? Description => ProtocolPrompt.Description;
}