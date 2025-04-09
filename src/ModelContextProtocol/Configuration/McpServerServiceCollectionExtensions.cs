using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension to host an MCP server
/// </summary>
public static class McpServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds the MCP server to the service collection with default options.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configureOptions"></param>
    /// <returns></returns>
    public static IMcpServerBuilder AddMcpServer(this IServiceCollection services, Action<McpServerOptions>? configureOptions = null)
    {
        services.AddOptions();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<McpServerOptions>, McpServerOptionsSetup>());
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        return new DefaultMcpServerBuilder(services);
    }
}
