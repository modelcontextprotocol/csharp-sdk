using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides methods for configuring HTTP MCP servers via dependency injection.
/// </summary>
public static class HttpMcpServerBuilderExtensions
{
    /// <summary>
    /// Adds the services necessary for <see cref="M:McpEndpointRouteBuilderExtensions.MapMcp"/>
    /// to handle MCP requests and sessions using the MCP Streamable HTTP transport. For more information on configuring the underlying HTTP server
    /// to control things like port binding custom TLS certificates, see the <see href="https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis">Minimal APIs quick reference</see>.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configureOptions">Configures options for the Streamable HTTP transport. This allows configuring per-session
    /// <see cref="McpServerOptions"/> and running logic before and after a session.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithHttpTransport(this IMcpServerBuilder builder, Action<HttpServerTransportOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<StreamableHttpHandler>();
        builder.Services.TryAddSingleton<SseHandler>();
        builder.Services.AddHostedService<IdleTrackingBackgroundService>();
        builder.Services.AddDataProtection();

        if (configureOptions is not null)
        {
            builder.Services.Configure(configureOptions);
        }

        return builder;
    }
}
