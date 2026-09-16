using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring MCP servers with dependency injection.
/// </summary>
public static class McpServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Model Context Protocol (MCP) server to the service collection with default options.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the server to.</param>
    /// <param name="configureOptions">An optional callback to configure the <see cref="McpServerOptions"/>.</param>
    /// <returns>An <see cref="IMcpServerBuilder"/> that can be used to further configure the MCP server.</returns>

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
