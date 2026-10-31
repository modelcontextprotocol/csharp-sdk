namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Stores protected OAuth proxy records.
/// </summary>
/// <remarks>
/// Values supplied to this interface are already encrypted and authenticated. Implementations used
/// by multiple application instances must make <see cref="TakeAsync"/> atomic across those instances.
/// </remarks>
public interface IMcpOAuthProxyStore
{
    /// <summary>
    /// Stores a protected record.
    /// </summary>
    ValueTask SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a protected record without consuming it.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically gets and removes a protected record.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>?> TakeAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a protected record.
    /// </summary>
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
}
