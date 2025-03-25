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

        // Configure the option's server information based on the current process,
        // if it otherwise lacks server information.
        var assemblyName = (Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly()).GetName();
        if (options.ServerInfo is not { } serverInfo ||
            serverInfo.Name is null ||
            serverInfo.Version is null)
        {
            options.ServerInfo = options.ServerInfo is null ?
                new()
                {
                    Name = assemblyName.Name ?? "McpServer",
                    Version = assemblyName.Version?.ToString() ?? "1.0.0",
                } :
                options.ServerInfo with
                {
                    Name = options.ServerInfo.Name ?? assemblyName.Name ?? "McpServer",
                    Version = options.ServerInfo.Version ?? assemblyName.Version?.ToString() ?? "1.0.0",
                };
        }

        // Collect all of the provided tools into a tools collection. If the options already has
        // a collection, add to it, otherwise create a new one. We want to maintain the identity
        // of an existing collection in case someone has provided their own derived type, wants
        // change notifications, etc.
        McpServerToolCollection toolsCollection = options.Capabilities?.Tools?.ToolCollection ?? [];
        foreach (var tool in serverTools)
        {
            toolsCollection.TryAdd(tool);
        }

        if (!toolsCollection.IsEmpty)
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

        // Apply custom server handlers.
        serverHandlers.Value.OverwriteWithSetHandlers(options);
    }
}
