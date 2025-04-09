using System.ComponentModel;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides options for controlling the creation of an <see cref="McpServerPrompt"/>.
/// </summary>
/// <remarks>
/// <para>
/// These options allow for customizing the behavior and metadata of prompts created with
/// <see cref="M:McpServerPrompt.Create"/>. They provide control over naming, description,
/// and dependency injection integration.
/// </para>
/// <para>
/// When creating prompts programmatically rather than using attributes, these options
/// provide the same level of configuration flexibility.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// // Create a prompt with custom options
/// var promptOptions = new McpServerPromptCreateOptions
/// {
///     Name = "weather-inquiry",
///     Description = "A prompt that asks about the weather in a specific city",
///     Services = serviceProvider // For dependency injection
/// };
/// 
/// // Create a prompt with the options
/// var prompt = McpServerPrompt.Create(
///     () => "What is the weather like in Paris today?",
///     promptOptions);
///     
/// // Add to server options
/// serverOptions.Capabilities.Prompts.PromptCollection.Add(prompt);
/// </code>
/// </para>
/// </remarks>
public sealed class McpServerPromptCreateOptions
{
    /// <summary>
    /// Gets or sets optional services used in the construction of the <see cref="McpServerPrompt"/>.
    /// </summary>
    /// <remarks>
    /// These services will be used to determine which parameters should be satisifed from dependency injection; what services
    /// are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </remarks>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// Gets or sets the name to use for the <see cref="McpServerPrompt"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but an <see cref="McpServerPromptAttribute"/> is applied to the method,
    /// the name from the attribute will be used. If that's not present, a name based on the method's name will be used.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or set the description to use for the <see cref="McpServerPrompt"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but a <see cref="DescriptionAttribute"/> is applied to the method,
    /// the description from that attribute will be used.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>
    /// Creates a shallow clone of the current <see cref="McpServerPromptCreateOptions"/> instance.
    /// </summary>
    internal McpServerPromptCreateOptions Clone() =>
        new McpServerPromptCreateOptions()
        {
            Services = Services,
            Name = Name,
            Description = Description,
        };
}
