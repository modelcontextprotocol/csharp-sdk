using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Integration tests for SSE resumability using <see cref="DistributedCacheEventStreamStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class runs the same resumability tests as <see cref="ResumabilityIntegrationTests"/> but
/// using the production <see cref="DistributedCacheEventStreamStore"/> implementation backed by
/// an in-memory distributed cache.
/// </para>
/// <para>
/// These tests verify that the distributed cache implementation correctly stores and retrieves
/// events for resumability, including across simulated disconnections.
/// </para>
/// </remarks>
public class DistributedCacheResumabilityIntegrationTests(ITestOutputHelper testOutputHelper) : ResumabilityIntegrationTestsBase(testOutputHelper)
{
    /// <inheritdoc />
    protected override ValueTask<ISseEventStreamStore> CreateEventStreamStoreAsync()
    {
        // Create a new in-memory distributed cache for each test
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        // Configure the store with shorter expiration times suitable for testing
        var options = new DistributedCacheEventStreamStoreOptions
        {
            // Use the in-memory distributed cache
            Cache = cache,

            // Use shorter polling interval for faster test execution
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(50),

            // Use shorter expiration times for tests
            EventSlidingExpiration = TimeSpan.FromMinutes(5),
            EventAbsoluteExpiration = TimeSpan.FromMinutes(10),
            MetadataSlidingExpiration = TimeSpan.FromMinutes(5),
            MetadataAbsoluteExpiration = TimeSpan.FromMinutes(10),
        };

        var store = new DistributedCacheEventStreamStore(Options.Create(options), LoggerFactory.CreateLogger<DistributedCacheEventStreamStore>());
        return new ValueTask<ISseEventStreamStore>(store);
    }
}
