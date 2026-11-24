using System.Collections.Concurrent;

namespace AspNetCoreMcpClient;

/// <summary>
/// Owns one asynchronously disposable client per application session and serializes operations within each session.
/// </summary>
/// <typeparam name="TClient">The client type stored for each session.</typeparam>
public sealed class SessionClientRegistry<TClient> : IAsyncDisposable
    where TClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<string, CancellationToken, Task<TClient>> _clientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _idleTimeout;
    private int _disposed;

    public SessionClientRegistry(
        Func<string, CancellationToken, Task<TClient>> clientFactory,
        TimeProvider timeProvider,
        TimeSpan idleTimeout)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleTimeout, TimeSpan.Zero);

        _clientFactory = clientFactory;
        _timeProvider = timeProvider;
        _idleTimeout = idleTimeout;
    }

    /// <summary>
    /// Runs an operation against the session's client. Operations for different sessions can run concurrently, while
    /// operations for the same session are serialized.
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        string sessionId,
        Func<TClient, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        while (true)
        {
            var entry = _entries.GetOrAdd(sessionId, _ => new Entry(_timeProvider.GetUtcNow()));
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (entry.Removed)
                {
                    continue;
                }

                if (Volatile.Read(ref _disposed) != 0)
                {
                    RemoveExact(sessionId, entry);
                    entry.Removed = true;
                    throw new ObjectDisposedException(GetType().FullName);
                }

                if (entry.Client is null)
                {
                    try
                    {
                        entry.Client = await _clientFactory(sessionId, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        RemoveExact(sessionId, entry);
                        entry.Removed = true;
                        throw;
                    }
                }

                entry.LastAccess = _timeProvider.GetUtcNow();
                try
                {
                    return await operation(entry.Client, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    entry.LastAccess = _timeProvider.GetUtcNow();
                }
            }
            finally
            {
                entry.Gate.Release();
            }
        }
    }

    /// <summary>Removes and disposes a session client, if one exists.</summary>
    public async Task<bool> RemoveAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_entries.TryGetValue(sessionId, out var entry) || !RemoveExact(sessionId, entry))
        {
            return false;
        }

        await DisposeEntryAsync(entry).ConfigureAwait(false);
        return true;
    }

    /// <summary>Removes clients that have not been used within the configured idle timeout.</summary>
    public async Task<int> RemoveIdleAsync()
    {
        var removed = 0;
        var now = _timeProvider.GetUtcNow();

        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            if (!await entry.Gate.WaitAsync(0).ConfigureAwait(false))
            {
                continue;
            }

            try
            {
                if (!entry.Removed && now - entry.LastAccess >= _idleTimeout && RemoveExact(pair.Key, entry))
                {
                    entry.Removed = true;
                    if (entry.Client is not null)
                    {
                        await entry.Client.DisposeAsync().ConfigureAwait(false);
                        entry.Client = default;
                    }

                    removed++;
                }
            }
            finally
            {
                entry.Gate.Release();
            }
        }

        return removed;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        while (!_entries.IsEmpty)
        {
            foreach (var pair in _entries)
            {
                if (RemoveExact(pair.Key, pair.Value))
                {
                    await DisposeEntryAsync(pair.Value).ConfigureAwait(false);
                }
            }
        }
    }

    private bool RemoveExact(string sessionId, Entry entry) =>
        ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new(sessionId, entry));

    private static async Task DisposeEntryAsync(Entry entry)
    {
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            entry.Removed = true;
            if (entry.Client is not null)
            {
                await entry.Client.DisposeAsync().ConfigureAwait(false);
                entry.Client = default;
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private sealed class Entry(DateTimeOffset lastAccess)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public TClient? Client { get; set; }

        public DateTimeOffset LastAccess { get; set; } = lastAccess;

        public bool Removed { get; set; }
    }
}
