using System.Reflection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Configuration;

internal sealed class McpServerOptionsSetup(IOptions<McpServerHandlers> serverHandlers) : IConfigureOptions<McpServerOptions>
{
    public void Configure(McpServerOptions options)
    {
        Throw.IfNull(options);

        var assemblyName = Assembly.GetEntryAssembly()?.GetName();
        options.ServerInfo = new Implementation
        {
            Name = assemblyName?.Name ?? "McpServer",
            Version = assemblyName?.Version?.ToString() ?? "1.0.0",
        };

        serverHandlers.Value.OverwriteWithSetHandlers(options);
    }
}
