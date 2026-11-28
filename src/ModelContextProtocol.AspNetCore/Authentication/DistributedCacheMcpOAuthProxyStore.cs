using Microsoft.Extensions.Caching.Distributed;
namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Stores protected OAuth proxy records in an <see cref="IDistributedCache"/>.
/// </summary>
/// <remarks>
/// <see cref="IDistributedCache"/> does not expose an atomic get-and-remove operation. This
/// implementation serializes <see cref="TakeAsync"/> calls within one process. Multi-instance
/// deployments must replace it with a store whose <see cref="IMcpOAuthProxyStore.TakeAsync"/>
/// operation is atomic in the backing store.
/// </remarks>
public sealed class DistributedCacheMcpOAuthProxyStore(IDistributedCache cache) : IMcpOAuthProxyStore
{
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64)
        .Select(static _ => new SemaphoreSlim(1, 1))
        .ToArray();

    /// <inheritdoc />
    public async ValueTask SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        await cache.SetAsync(
            key,
            value.ToArray(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>?> TakeAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var gate = _locks[(StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % _locks.Length];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var value = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (value is not null)
            {
                await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            }

            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new(cache.RemoveAsync(key, cancellationToken));
    }
}
