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
    /// <param name="configureOptions">Optional callback to configure the <see cref="McpServerOptions"/>.</param>
    /// <returns>An <see cref="IMcpServerBuilder"/> that can be used to further configure the MCP server.</returns>

    public static IMcpServerBuilder AddMcpServer(this IServiceCollection services, Action<McpServerOptions>? configureOptions = null)
    {
        services.AddOptions();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<McpServerOptions>, McpServerOptionsSetup>());
        
        // Capture default options from the configuration callback to avoid circular dependencies
        // when resolving IOptions<McpServerOptions> from within tool/prompt/resource factories
        var defaultOptions = new McpServerDefaultOptions();
        if (configureOptions is not null)
        {
            var tempOptions = new McpServerOptions();
            configureOptions(tempOptions);
            defaultOptions.JsonSerializerOptions = tempOptions.JsonSerializerOptions;
            defaultOptions.SchemaCreateOptions = tempOptions.SchemaCreateOptions;
            
            services.Configure(configureOptions);
        }
        
        // Register the default options as a singleton that can be safely resolved
        // without circular dependencies. Use AddSingleton (not TryAdd) to ensure it's registered.
        services.AddSingleton<McpServerDefaultOptions>(defaultOptions);

        return new DefaultMcpServerBuilder(services);
    }
}
