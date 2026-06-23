using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

#pragma warning disable MCP9006 // This type only exists to configure the obsolete legacy resumability store.

/// <summary>
/// Configures <see cref="DistributedCacheEventStreamStoreOptions"/> by resolving
/// the <see cref="IDistributedCache"/> from DI when not explicitly set.
/// </summary>
internal sealed class DistributedCacheEventStreamStoreOptionsSetup(IDistributedCache? cache = null) : IConfigureOptions<DistributedCacheEventStreamStoreOptions>
{
    public void Configure(DistributedCacheEventStreamStoreOptions options)
    {
        options.Cache ??= cache;
    }
}
