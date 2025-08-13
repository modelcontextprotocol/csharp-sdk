namespace DynamicToolFiltering.Services;

/// <summary>
/// Service for managing rate limiting and usage tracking.
/// </summary>
public interface IRateLimitingService
{
    /// <summary>
    /// Gets the current usage count for a user/tool combination within the specified time window.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="windowStart">The start of the time window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current usage count.</returns>
    Task<int> GetUsageCountAsync(string userId, string toolName, DateTime windowStart, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a tool usage for rate limiting tracking.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="timestamp">The timestamp of the usage.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordUsageAsync(string userId, string toolName, DateTime timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up old usage records that are outside the retention window.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupOldRecordsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets usage statistics for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Usage statistics.</returns>
    Task<Dictionary<string, int>> GetUsageStatisticsAsync(string userId, CancellationToken cancellationToken = default);
}