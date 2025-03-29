using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension to host the MCP server
/// </summary>
public static class McpServerServiceCollectionExtension
{
    /// <summary>
    /// Adds the MCP server to the service collection with default options.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configureOptions"></param>
    /// <returns></returns>
    public static IMcpServerBuilder AddMcpServer(this IServiceCollection services, Action<McpServerOptions>? configureOptions = null)
    {
        services.AddSingleton(services =>
        {
            IServerTransport serverTransport = services.GetRequiredService<IServerTransport>();
            IOptions<McpServerOptions> options = services.GetRequiredService<IOptions<McpServerOptions>>();
            ILoggerFactory? loggerFactory = services.GetService<ILoggerFactory>();

            return McpServerFactory.Create(serverTransport, options.Value, loggerFactory, services);
        });

        services.AddOptions();
        services.AddTransient<IConfigureOptions<McpServerOptions>, McpServerOptionsSetup>();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        return new DefaultMcpServerBuilder(services);
    }
}
