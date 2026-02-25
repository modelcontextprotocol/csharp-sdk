using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
    /// to handle MCP requests and sessions using the MCP Streamable HTTP transport.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configureOptions">Configures options for the Streamable HTTP transport. This allows configuring per-session
    /// <see cref="McpServerOptions"/> and running logic before and after a session.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <remarks>For more information on configuring the underlying HTTP server
    /// to control things like port binding and custom TLS certificates, see the <see href="https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis">Minimal APIs quick reference</see>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithHttpTransport(this IMcpServerBuilder builder, Action<HttpServerTransportOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<StatefulSessionManager>();
        builder.Services.TryAddSingleton<StreamableHttpHandler>();
        builder.Services.TryAddSingleton<SseHandler>();
        builder.Services.AddHostedService<IdleTrackingBackgroundService>();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IPostConfigureOptions<McpServerOptions>, AuthorizationFilterSetup>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<HttpServerTransportOptions>, HttpServerTransportOptionsSetup>());

        if (configureOptions is not null)
        {
            builder.Services.Configure(configureOptions);
        }

        return builder;
    }

    /// <summary>
    /// Adds authorization filters to support <see cref="AuthorizeAttribute"/>
    /// on MCP server tools, prompts, and resources. This method should always be called when using
    /// ASP.NET Core integration to ensure proper authorization support.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method automatically configures authorization filters for all MCP server handlers. These filters respect
    /// authorization attributes such as <see cref="AuthorizeAttribute"/>
    /// and <see cref="AllowAnonymousAttribute"/>.
    /// </remarks>
    public static IMcpServerBuilder AddAuthorizationFilters(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Allow the authorization filters to get added multiple times in case other middleware changes the matched primitive.
        builder.Services.AddTransient<IConfigureOptions<McpServerOptions>, AuthorizationFilterSetup>();

        return builder;
    }

    /// <summary>
    /// Registers a <see cref="DistributedCacheEventStreamStore"/> as the <see cref="ISseEventStreamStore"/> for SSE resumability.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configureOptions">An optional action to configure <see cref="DistributedCacheEventStreamStoreOptions"/>.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// An <see cref="IDistributedCache"/> implementation must be registered in the service collection before calling this method.
    /// The registered cache is automatically assigned to <see cref="DistributedCacheEventStreamStoreOptions.Cache"/>.
    /// </para>
    /// <para>
    /// To use a specific <see cref="IDistributedCache"/> instance instead of the one registered in DI,
    /// set the <see cref="DistributedCacheEventStreamStoreOptions.Cache"/> property in the <paramref name="configureOptions"/> callback.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithDistributedCacheEventStreamStore(this IMcpServerBuilder builder, Action<DistributedCacheEventStreamStoreOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<DistributedCacheEventStreamStoreOptions>, DistributedCacheEventStreamStoreOptionsSetup>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DistributedCacheEventStreamStoreOptions>, DistributedCacheEventStreamStoreOptionsValidator>());
        builder.Services.AddSingleton<ISseEventStreamStore, DistributedCacheEventStreamStore>();

        if (configureOptions is not null)
        {
            builder.Services.Configure(configureOptions);
        }

        return builder;
    }
}
