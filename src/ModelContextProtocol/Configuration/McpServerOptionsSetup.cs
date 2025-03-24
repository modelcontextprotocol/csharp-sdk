using System.Reflection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Configuration;

/// <summary>
/// Configures the McpServerOptions using provided server handlers.
/// </summary>
/// <param name="serverHandlers">The server handlers configuration options.</param>
internal sealed class McpServerOptionsSetup(IOptions<McpServerHandlers> serverHandlers) : IConfigureOptions<McpServerOptions>
{
    /// <summary>
    /// Configures the given McpServerOptions instance by setting server information
    /// and applying custom server handlers.
    /// </summary>
    /// <param name="options">The options instance to be configured.</param>
    public void Configure(McpServerOptions options)
    {
        Throw.IfNull(options);

        var assemblyName = Assembly.GetEntryAssembly()?.GetName();

        // Set server information based on the entry assembly
        options.ServerInfo = new Implementation
        {
            Name = assemblyName?.Name ?? "McpServer",
            Version = assemblyName?.Version?.ToString() ?? "1.0.0",
        };

        // Apply custom server handlers
        serverHandlers.Value.OverwriteWithSetHandlers(options);
    }
}
