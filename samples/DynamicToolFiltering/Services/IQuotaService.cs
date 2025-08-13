namespace DynamicToolFiltering.Services;

/// <summary>
/// Service for managing user quotas and usage tracking.
/// </summary>
public interface IQuotaService
{
    /// <summary>
    /// Checks if a user has available quota for a specific tool.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="userRole">The user role.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if quota is available, false otherwise.</returns>
    Task<bool> HasAvailableQuotaAsync(string userId, string userRole, string toolName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes quota for a tool usage.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="cost">The quota cost to consume.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConsumeQuotaAsync(string userId, string toolName, int cost, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current quota usage for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current usage amount.</returns>
    Task<int> GetCurrentUsageAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the quota limit for a user based on their role.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="userRole">The user role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The quota limit (-1 for unlimited).</returns>
    Task<int> GetQuotaLimitAsync(string userId, string userRole, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the remaining quota for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="userRole">The user role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The remaining quota amount.</returns>
    Task<int> GetRemainingQuotaAsync(string userId, string userRole, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the date when the user's quota will reset.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The quota reset date.</returns>
    Task<DateTime> GetQuotaResetDateAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets quota for a user (for admin purposes or period rollover).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResetQuotaAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed quota usage breakdown by tool for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of tool names and their usage amounts.</returns>
    Task<Dictionary<string, int>> GetUsageBreakdownAsync(string userId, CancellationToken cancellationToken = default);
}