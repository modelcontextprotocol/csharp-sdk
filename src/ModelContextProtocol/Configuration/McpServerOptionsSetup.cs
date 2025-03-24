using System.Reflection;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Configuration;

/// <summary>
/// Configures the McpServerOptions using addition services from DI.
/// </summary>
/// <param name="serverHandlers">The server handlers configuration options.</param>
/// <param name="serverTools">Tools individually registered.</param>
internal sealed class McpServerOptionsSetup(
    IOptions<McpServerHandlers> serverHandlers,
    IEnumerable<McpServerTool> serverTools) : IConfigureOptions<McpServerOptions>
{
    /// <summary>
    /// Configures the given McpServerOptions instance by setting server information
    /// and applying custom server handlers and tools.
    /// </summary>
    /// <param name="options">The options instance to be configured.</param>
    public void Configure(McpServerOptions options)
    {
        Throw.IfNull(options);

        var assemblyName = (Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly()).GetName();
        options.ServerInfo = new()
        {
            Name = assemblyName.Name ?? "McpServer",
            Version = assemblyName.Version?.ToString() ?? "1.0.0",
        };

        McpServerToolCollection toolsCollection = new();
        foreach (var tool in serverTools)
        {
            toolsCollection.Add(tool);
        }

        if (options.Capabilities?.Tools?.ToolCollection is { } existingToolsCollection)
        {
            existingToolsCollection.AddRange(toolsCollection);
        }
        else if (!toolsCollection.IsEmpty)
        {
            options.Capabilities = options.Capabilities is null ?
                new() { Tools = new() { ToolCollection = toolsCollection } } :
                options.Capabilities with
                {
                    Tools = options.Capabilities.Tools is null ?
                        new() { ToolCollection = toolsCollection } :
                        options.Capabilities.Tools with { ToolCollection = toolsCollection },
                };
        }

        // Apply custom server handlers
        serverHandlers.Value.OverwriteWithSetHandlers(options);
    }
}
