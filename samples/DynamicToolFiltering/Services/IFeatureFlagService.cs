namespace DynamicToolFiltering.Services;

/// <summary>
/// Service for managing feature flags.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Checks if a feature flag is enabled for a specific user.
    /// </summary>
    /// <param name="flagName">The feature flag name.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the feature is enabled, false otherwise.</returns>
    Task<bool> IsEnabledAsync(string flagName, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all feature flags and their states for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of feature flag names and their enabled states.</returns>
    Task<Dictionary<string, bool>> GetAllFlagsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the state of a feature flag (for testing/admin purposes).
    /// </summary>
    /// <param name="flagName">The feature flag name.</param>
    /// <param name="enabled">Whether the flag should be enabled.</param>
    /// <param name="userId">Optional user ID for user-specific flags.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetFlagAsync(string flagName, bool enabled, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the rollout percentage for a feature flag.
    /// </summary>
    /// <param name="flagName">The feature flag name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rollout percentage (0-100).</returns>
    Task<int> GetRolloutPercentageAsync(string flagName, CancellationToken cancellationToken = default);
}