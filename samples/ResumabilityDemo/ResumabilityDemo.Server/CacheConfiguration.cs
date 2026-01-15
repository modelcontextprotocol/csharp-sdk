using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.SqlServer;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace ResumabilityDemo.Server;

/// <summary>
/// Extension methods for configuring the distributed cache and event stream store.
/// </summary>
public static class CacheConfiguration
{
    /// <summary>
    /// Configures the distributed cache based on the "CacheProvider" configuration value.
    /// Supported values: "Memory", "Redis", "SqlServer"
    /// </summary>
    public static IServiceCollection AddConfiguredDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var cacheProvider = configuration.GetValue<string>("CacheProvider") ?? "Memory";

        switch (cacheProvider.ToLowerInvariant())
        {
            case "redis":
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = configuration.GetValue<string>("Redis:Configuration")
                        ?? "localhost:6379";
                    options.InstanceName = "McpResumability:";
                });
                break;

            case "sqlserver":
                services.AddDistributedSqlServerCache(options =>
                {
                    options.ConnectionString = configuration.GetValue<string>("SqlServer:ConnectionString")
                        ?? throw new InvalidOperationException("SqlServer:ConnectionString is required when using SqlServer cache provider");
                    options.SchemaName = configuration.GetValue<string>("SqlServer:SchemaName") ?? "dbo";
                    options.TableName = configuration.GetValue<string>("SqlServer:TableName") ?? "SseEventCache";
                });
                break;

            case "memory":
            default:
                services.AddDistributedMemoryCache();
                break;
        }

        return services;
    }

    /// <summary>
    /// Creates a <see cref="DistributedCacheEventStreamStore"/> configured from app settings.
    /// </summary>
    public static DistributedCacheEventStreamStore CreateEventStreamStore(
        IServiceProvider services,
        IConfiguration configuration)
    {
        var cache = services.GetRequiredService<IDistributedCache>();
        var logger = services.GetRequiredService<ILogger<DistributedCacheEventStreamStore>>();
        var options = GetEventStreamStoreOptions(configuration);

        return new DistributedCacheEventStreamStore(cache, options, logger);
    }

    /// <summary>
    /// Reads <see cref="DistributedCacheEventStreamStoreOptions"/> from configuration.
    /// </summary>
    public static DistributedCacheEventStreamStoreOptions GetEventStreamStoreOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("EventStreamStore");

        return new DistributedCacheEventStreamStoreOptions
        {
            EventSlidingExpiration = section.GetValue<int?>("EventSlidingExpirationMinutes") is int eventSliding
                ? TimeSpan.FromMinutes(eventSliding)
                : TimeSpan.FromMinutes(30),

            EventAbsoluteExpiration = section.GetValue<int?>("EventAbsoluteExpirationHours") is int eventAbsolute
                ? TimeSpan.FromHours(eventAbsolute)
                : TimeSpan.FromHours(2),

            MetadataSlidingExpiration = section.GetValue<int?>("MetadataSlidingExpirationHours") is int metaSliding
                ? TimeSpan.FromHours(metaSliding)
                : TimeSpan.FromHours(1),

            MetadataAbsoluteExpiration = section.GetValue<int?>("MetadataAbsoluteExpirationHours") is int metaAbsolute
                ? TimeSpan.FromHours(metaAbsolute)
                : TimeSpan.FromHours(4),

            PollingInterval = section.GetValue<int?>("PollingIntervalMilliseconds") is int polling
                ? TimeSpan.FromMilliseconds(polling)
                : TimeSpan.FromMilliseconds(100),
        };
    }
}

/// <summary>
/// Post-configures <see cref="HttpServerTransportOptions"/> to set up the event stream store
/// with the resolved <see cref="IDistributedCache"/> from DI.
/// </summary>
public sealed class EventStreamStorePostConfigure : IPostConfigureOptions<HttpServerTransportOptions>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public EventStreamStorePostConfigure(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public void PostConfigure(string? name, HttpServerTransportOptions options)
    {
        // Only set if not already configured
        if (options.EventStreamStore is null)
        {
            options.EventStreamStore = CacheConfiguration.CreateEventStreamStore(_serviceProvider, _configuration);
        }
    }
}
