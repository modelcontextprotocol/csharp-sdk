using System.Collections.Concurrent;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// A simple in-memory implementation of <see cref="ISessionStore"/> for testing and development.
/// </summary>
/// <remarks>
/// This implementation stores session metadata in a <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// It is NOT suitable for production use in distributed scenarios since the data is not shared
/// across server instances. Use a database-backed implementation for production deployments.
/// </remarks>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionMetadata> _sessions = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemorySessionStore"/> class.
    /// </summary>
    /// <param name="timeProvider">Optional time provider for testing. Defaults to <see cref="TimeProvider.System"/>.</param>
    public InMemorySessionStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets the current number of sessions in the store.
    /// </summary>
    public int Count => _sessions.Count;

    /// <inheritdoc />
    public Task SaveAsync(SessionMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _sessions[metadata.SessionId] = metadata;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SessionMetadata?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(sessionId, out var metadata);
        return Task.FromResult(metadata);
    }

    /// <inheritdoc />
    public Task UpdateActivityAsync(string sessionId, DateTime lastActivityUtc, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var metadata))
        {
            metadata.LastActivityUtc = lastActivityUtc;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_sessions.TryRemove(sessionId, out _));
    }

    /// <inheritdoc />
    public Task<int> PruneIdleSessionsAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow().DateTime - idleTimeout;
        var removed = 0;

        foreach (var kvp in _sessions)
        {
            if (kvp.Value.LastActivityUtc < cutoff)
            {
                if (_sessions.TryRemove(kvp.Key, out _))
                {
                    removed++;
                }
            }
        }

        return Task.FromResult(removed);
    }

    /// <summary>
    /// Clears all sessions from the store. Useful for test cleanup.
    /// </summary>
    public void Clear() => _sessions.Clear();
}
