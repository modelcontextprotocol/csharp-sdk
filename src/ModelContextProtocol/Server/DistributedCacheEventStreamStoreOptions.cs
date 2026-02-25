using Microsoft.Extensions.Caching.Distributed;

namespace ModelContextProtocol.Server;

/// <summary>
/// Configuration options for <see cref="DistributedCacheEventStreamStore"/>.
/// </summary>
public sealed class DistributedCacheEventStreamStoreOptions
{
    /// <summary>
    /// Gets or sets the <see cref="IDistributedCache"/> to use for event storage.
    /// </summary>
    /// <remarks>
    /// When using dependency injection with <c>WithDistributedCacheEventStreamStore()</c>, this is
    /// automatically populated from the <see cref="IDistributedCache"/> registered in DI.
    /// Set this property explicitly to use a specific cache instance.
    /// </remarks>
    public IDistributedCache? Cache { get; set; }

    /// <summary>
    /// Gets or sets the sliding expiration for individual events in the cache.
    /// </summary>
    /// <remarks>
    /// Events are refreshed on each access. If an event is not accessed within this
    /// time period, it may be evicted from the cache.
    /// </remarks>
    public TimeSpan? EventSlidingExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets or sets the absolute expiration for individual events in the cache.
    /// </summary>
    /// <remarks>
    /// Events will be evicted from the cache after this time period, regardless of access.
    /// </remarks>
    public TimeSpan? EventAbsoluteExpiration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Gets or sets the sliding expiration for stream metadata in the cache.
    /// </summary>
    /// <remarks>
    /// Stream metadata includes mode and completion status. This should typically be
    /// set to a longer duration than event expiration to allow for resumability.
    /// </remarks>
    public TimeSpan? MetadataSlidingExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the absolute expiration for stream metadata in the cache.
    /// </summary>
    /// <remarks>
    /// Stream metadata will be evicted from the cache after this time period, regardless of access.
    /// </remarks>
    public TimeSpan? MetadataAbsoluteExpiration { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Gets or sets the interval between polling attempts when a stream reader is waiting for new events
    /// in the default <see cref="SseEventStreamMode.Streaming"/> mode.
    /// </summary>
    /// <remarks>
    /// This only affects stream readers. A shorter interval provides lower latency for new events
    /// but increases cache access frequency.
    /// </remarks>
    public TimeSpan StreamReaderPollingInterval { get; set; } = TimeSpan.FromSeconds(1);
}
