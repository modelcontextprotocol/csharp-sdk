using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ModelContextProtocol;

/// <summary>
/// Post-configures <see cref="McpServerOptions"/> by resolving services from DI
/// when they haven't been explicitly set on the options.
/// </summary>
internal sealed class McpServerOptionsPostSetup(IServiceProvider serviceProvider) : IPostConfigureOptions<McpServerOptions>
{
    public void PostConfigure(string? name, McpServerOptions options)
    {
        options.TaskStore ??= serviceProvider.GetService<IMcpTaskStore>();
    }
}
