// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;

namespace ModelContextProtocol.AspNetCore.Distributed;

/// <summary>
/// Extension methods for configuring MCP session affinity services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the required services for MCP session affinity.
    /// This includes YARP reverse proxy and the session affinity routing filter.
    /// Uses HybridCache for session storage (L1 memory + L2 distributed caching).
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configure">Optional action to configure SessionAffinityOptions.</param>
    /// <returns>A builder for configuring MCP session affinity.</returns>
    public static ISessionAffinityBuilder AddMcpHttpSessionAffinity(
        this IServiceCollection services,
        Action<SessionAffinityOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure options using the options pattern
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Add validation for SessionAffinityOptions using source-generated validator
        services.TryAddSingleton<
            IValidateOptions<SessionAffinityOptions>,
            SessionAffinityOptionsValidator
        >();

        // Register HybridCache with default configuration
        // This provides L1 (in-memory) + L2 (distributed) caching
        // Consumers can add their own distributed cache (Redis, SQL Server, etc.)
        // via AddStackExchangeRedisCache, AddDistributedSqlServerCache, etc.
        // Use source-generated serialization for SessionOwnerInfo (AOT-compatible)
        services
            .AddHybridCache()
            .AddSerializer<SessionOwnerInfo, SessionOwnerInfoSerializer>();

        // Register HybridCache session store
        services.TryAdd(
            ServiceDescriptor.Singleton<ISessionStore>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<SessionAffinityOptions>>().Value;
                var cache = options.HybridCacheServiceKey is null
                    ? sp.GetRequiredService<HybridCache>()
                    : sp.GetRequiredKeyedService<HybridCache>(options.HybridCacheServiceKey);
                var logger = sp.GetRequiredService<ILogger<HybridCacheSessionStore>>();
                return new HybridCacheSessionStore(cache, logger);
            })
        );

        services.TryAddSingleton<IListeningEndpointResolver, ListeningEndpointResolver>();

        // Add YARP reverse proxy for request forwarding
        services.AddReverseProxy();

        // Register the endpoint filter for dependency injection
        services.TryAddSingleton<SessionAffinityEndpointFilter>();

        return new SessionAffinityBuilder(services);
    }
}
