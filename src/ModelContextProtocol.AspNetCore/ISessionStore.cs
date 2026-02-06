namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Defines a contract for persisting MCP session metadata to enable distributed session management.
/// </summary>
/// <remarks>
/// Implementations of this interface allow MCP sessions to survive server restarts and
/// enable horizontal scaling without sticky sessions. When a session is not found in
/// the in-memory cache, the session manager can use this store to retrieve session
/// metadata and recreate the session.
///
/// Note that only session metadata is persisted - the actual McpServer and transport
/// instances are always in-memory. When a session is "recreated" from storage, a new
/// McpServer instance is created with the stored capabilities and configuration.
/// </remarks>
public interface ISessionStore
{
    /// <summary>
    /// Saves session metadata to the store.
    /// </summary>
    /// <param name="metadata">The session metadata to persist.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveAsync(SessionMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves session metadata by session ID.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The session metadata if found; otherwise, null.</returns>
    Task<SessionMetadata?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last activity timestamp for a session.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="lastActivityUtc">The UTC timestamp of the last activity.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateActivityAsync(string sessionId, DateTime lastActivityUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes session metadata from the store.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the session was found and removed; otherwise, false.</returns>
    Task<bool> RemoveAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all sessions that have been idle longer than the specified timeout.
    /// </summary>
    /// <param name="idleTimeout">The maximum allowed idle time.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of sessions removed.</returns>
    Task<int> PruneIdleSessionsAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default);
}
