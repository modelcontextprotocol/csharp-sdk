using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ModelContextProtocol;

/// <summary>
/// Configures the McpServerOptions using additional services from DI.
/// </summary>
/// <param name="serverTools">The individually registered tools.</param>
/// <param name="serverPrompts">The individually registered prompts.</param>
/// <param name="serverResources">The individually registered resources.</param>
/// <param name="taskStore">The optional task store registered in DI.</param>
internal sealed class McpServerOptionsSetup(
    IEnumerable<McpServerTool> serverTools,
    IEnumerable<McpServerPrompt> serverPrompts,
    IEnumerable<McpServerResource> serverResources,
    IMcpTaskStore? taskStore = null) : IConfigureOptions<McpServerOptions>
{
    /// <summary>
    /// Configures the given McpServerOptions instance by setting server information
    /// and collecting registered server primitives.
    /// </summary>
    /// <param name="options">The options instance to be configured.</param>
    public void Configure(McpServerOptions options)
    {
        Throw.IfNull(options);

        options.TaskStore ??= taskStore;

        // Collect all of the provided tools into a tools collection. If the options already has
        // a collection, add to it, otherwise create a new one. We want to maintain the identity
        // of an existing collection in case someone has provided their own derived type, wants
        // change notifications, etc.
        McpServerPrimitiveCollection<McpServerTool> toolCollection = options.ToolCollection ?? [];
        foreach (var tool in serverTools)
        {
            toolCollection.TryAdd(tool);
        }

        if (!toolCollection.IsEmpty)
        {
            options.ToolCollection = toolCollection;
        }

        // Collect all of the provided prompts into a prompts collection. If the options already has
        // a collection, add to it, otherwise create a new one. We want to maintain the identity
        // of an existing collection in case someone has provided their own derived type, wants
        // change notifications, etc.
        McpServerPrimitiveCollection<McpServerPrompt> promptCollection = options.PromptCollection ?? [];
        foreach (var prompt in serverPrompts)
        {
            promptCollection.TryAdd(prompt);
        }

        if (!promptCollection.IsEmpty)
        {
            options.PromptCollection = promptCollection;
        }

        // Collect all of the provided resources into a resources collection. If the options already has
        // a collection, add to it, otherwise create a new one. We want to maintain the identity
        // of an existing collection in case someone has provided their own derived type, wants
        // change notifications, etc.
        McpServerResourceCollection resourceCollection = options.ResourceCollection ?? [];
        foreach (var resource in serverResources)
        {
            resourceCollection.TryAdd(resource);
        }

        if (!resourceCollection.IsEmpty)
        {
            options.ResourceCollection = resourceCollection;
        }
    }
}
