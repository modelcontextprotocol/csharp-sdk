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

        // Register IMcpTaskStore from options if not already registered.
        // This allows users to either:
        // 1. Register IMcpTaskStore directly in DI (takes precedence)
        // 2. Set options.TaskStore in the configuration callback (used as fallback)
        // If neither is done, resolving IMcpTaskStore will throw.
        services.TryAddSingleton<IMcpTaskStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;
            return options.TaskStore ?? throw new InvalidOperationException("No IMcpTaskStore has been configured. Either register an IMcpTaskStore in the service collection or set McpServerOptions.TaskStore when configuring the MCP server.");
        });

        return new DefaultMcpServerBuilder(services);
    }
}
