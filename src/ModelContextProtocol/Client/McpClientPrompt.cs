using ModelContextProtocol.Protocol.Types;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents a named prompt that can be retrieved from an MCP server and invoked with arguments.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a client-side wrapper around a prompt defined on an MCP server. It allows
/// retrieving the prompt's content by sending a request to the server with optional arguments.
/// </para>
/// <para>
/// Instances of this class are typically obtained by calling <see cref="McpClientExtensions.ListPromptsAsync"/>
/// which returns a collection of all available prompts from the server.
/// </para>
/// <para>
/// Each prompt has a name and optionally a description, and it can be executed with arguments
/// to produce customized content based on the template stored on the server.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // List all available prompts from the server
/// var prompts = await mcpClient.ListPromptsAsync();
/// 
/// // Find a specific prompt by name
/// var weatherPrompt = prompts.FirstOrDefault(p => p.Name == "weather_forecast");
/// 
/// if (weatherPrompt != null)
/// {
///     // Invoke the prompt with arguments
///     var result = await weatherPrompt.GetAsync(new Dictionary&lt;string, object?&gt;
///     {
///         ["location"] = "Seattle",
///         ["days"] = 5
///     });
///     
///     // Process the prompt result
///     foreach (var message in result.Messages)
///     {
///         Console.WriteLine($"{message.Role}: {message.Content.Text}");
///     }
/// }
/// </code>
/// </example>
public sealed class McpClientPrompt
{
    private readonly IMcpClient _client;

    internal McpClientPrompt(IMcpClient client, Prompt prompt)
    {
        _client = client;
        ProtocolPrompt = prompt;
    }

    /// <summary>Gets the protocol <see cref="Prompt"/> type for this instance.</summary>
    /// <remarks>
    /// <para>
    /// The ProtocolPrompt property contains the complete prompt definition as returned by the MCP server.
    /// It includes the prompt's name, description, and information about the arguments it accepts.
    /// </para>
    /// <para>
    /// This property provides direct access to the underlying protocol representation of the prompt,
    /// which can be useful for advanced scenarios or when implementing custom MCP client extensions.
    /// </para>
    /// <para>
    /// For most common use cases, you can use the more convenient <see cref="Name"/> and 
    /// <see cref="Description"/> properties instead of accessing the ProtocolPrompt directly.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Check if a prompt accepts a specific argument
    /// bool acceptsLocationArg = prompt.ProtocolPrompt.Arguments?.Any(a => a.Name == "location") ?? false;
    /// 
    /// // Get all required arguments for a prompt
    /// var requiredArgs = prompt.ProtocolPrompt.Arguments?.Where(a => a.Required).ToList();
    /// </code>
    /// </example>
    public Prompt ProtocolPrompt { get; }

    /// <summary>
    /// Retrieves this prompt's content by sending a request to the server with optional arguments.
    /// </summary>
    /// <param name="arguments">Optional arguments to pass to the prompt. Keys are argument names, and values are the argument values.</param>
    /// <param name="serializerOptions">The serialization options governing argument serialization.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ValueTask"/> containing the prompt's result with content and messages.</returns>
    /// <remarks>
    /// <para>
    /// This method sends a request to the MCP server to execute this prompt with the provided arguments.
    /// The server will process the prompt and return a result containing messages or other content.
    /// </para>
    /// <para>
    /// This is a convenience method that internally calls <see cref="McpClientExtensions.GetPromptAsync"/> 
    /// with this prompt's name.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get a prompt with no arguments
    /// var result = await promptClient.GetAsync();
    /// 
    /// // Get a prompt with arguments
    /// var result = await promptClient.GetAsync(new Dictionary&lt;string, object?&gt;
    /// {
    ///     ["message"] = "Tell me about the weather",
    ///     ["temperature"] = 75.5
    /// });
    /// 
    /// // Access the prompt messages
    /// foreach (var message in result.Messages)
    /// {
    ///     Console.WriteLine($"{message.Role}: {message.Content.Text}");
    /// }
    /// </code>
    /// </example>
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

    /// <summary>Gets the name of the prompt.</summary>
    public string Name => ProtocolPrompt.Name;

    /// <summary>Gets a description of the prompt.</summary>
    public string? Description => ProtocolPrompt.Description;
}