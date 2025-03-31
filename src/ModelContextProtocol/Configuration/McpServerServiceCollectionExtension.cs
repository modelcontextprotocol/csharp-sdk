﻿using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ModelContextProtocol;

/// <summary>
/// Extension to host an MCP server
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
        services.AddOptions();
        services.AddTransient<IConfigureOptions<McpServerOptions>, McpServerOptionsSetup>();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        return new DefaultMcpServerBuilder(services);
    }
}
