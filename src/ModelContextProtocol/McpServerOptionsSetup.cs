using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.ExceptionSummarization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ModelContextProtocol;

/// <summary>
/// Configures the McpServerOptions using additional services from DI.
/// </summary>
/// <param name="serverTools">The individually registered tools.</param>
/// <param name="serverPrompts">The individually registered prompts.</param>
/// <param name="serverResources">The individually registered resources.</param>
/// <param name="services">The application's service provider, used to resolve optional services.</param>
internal sealed class McpServerOptionsSetup(
    IEnumerable<McpServerTool> serverTools,
    IEnumerable<McpServerPrompt> serverPrompts,
    IEnumerable<McpServerResource> serverResources,
    IServiceProvider services) : IConfigureOptions<McpServerOptions>
{
    /// <summary>
    /// Configures the given McpServerOptions instance by setting server information
    /// and collecting registered server primitives.
    /// </summary>
    /// <param name="options">The options instance to be configured.</param>
    public void Configure(McpServerOptions options)
    {
        Throw.IfNull(options);

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

        // Default the summarizer from an optionally registered IExceptionSummarizer. An explicitly
        // configured delegate always wins, whether it was set before this runs or by a later IConfigureOptions.
        if (options.ExceptionSummarizer is null &&
            services.GetService<IExceptionSummarizer>() is { } summarizer)
        {
            options.ExceptionSummarizer = ex =>
            {
                // ExceptionType and Description are both documented as free of privacy-sensitive information.
                // AdditionalDetails is not, and ExceptionSummary.ToString() appends it, so neither is used here.
                // Description alone is "Unknown" for exception types no provider handles, which would drop the
                // type from the log entirely, so the type is prepended.
                ExceptionSummary summary = summarizer.Summarize(ex);
                return $"{summary.ExceptionType}: {summary.Description}";
            };
        }
    }
}
