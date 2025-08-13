using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DynamicToolFiltering.Services;

/// <summary>
/// In-memory implementation of feature flag service with percentage rollout support.
/// Note: This is for demonstration purposes. In production, use a dedicated feature flag service.
/// </summary>
public class InMemoryFeatureFlagService : IFeatureFlagService
{
    private readonly ConcurrentDictionary<string, FeatureFlag> _flags = new();
    private readonly ILogger<InMemoryFeatureFlagService> _logger;

    public InMemoryFeatureFlagService(ILogger<InMemoryFeatureFlagService> logger)
    {
        _logger = logger;
        InitializeDefaultFlags();
    }

    public Task<bool> IsEnabledAsync(string flagName, string userId, CancellationToken cancellationToken = default)
    {
        if (!_flags.TryGetValue(flagName, out var flag))
        {
            _logger.LogDebug("Feature flag {FlagName} not found, returning false", flagName);
            return Task.FromResult(false);
        }

        // Check if globally enabled/disabled
        if (!flag.Enabled)
        {
            return Task.FromResult(false);
        }

        // Check user-specific overrides
        if (flag.UserOverrides.TryGetValue(userId, out var userOverride))
        {
            _logger.LogDebug("Feature flag {FlagName} has user override for {UserId}: {Enabled}", flagName, userId, userOverride);
            return Task.FromResult(userOverride);
        }

        // Check percentage rollout
        if (flag.RolloutPercentage < 100)
        {
            var userHash = GetUserHash(userId, flagName);
            var enabled = userHash < flag.RolloutPercentage;
            _logger.LogDebug("Feature flag {FlagName} percentage rollout for {UserId}: {Percentage}% -> {Enabled}", 
                flagName, userId, flag.RolloutPercentage, enabled);
            return Task.FromResult(enabled);
        }

        return Task.FromResult(true);
    }

    public Task<Dictionary<string, bool>> GetAllFlagsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, bool>();

        foreach (var kvp in _flags)
        {
            var flagName = kvp.Key;
            var isEnabled = IsEnabledAsync(flagName, userId, cancellationToken).Result;
            result[flagName] = isEnabled;
        }

        return Task.FromResult(result);
    }

    public Task SetFlagAsync(string flagName, bool enabled, string? userId = null, CancellationToken cancellationToken = default)
    {
        _flags.AddOrUpdate(flagName,
            new FeatureFlag { Name = flagName, Enabled = enabled },
            (_, existingFlag) =>
            {
                if (userId != null)
                {
                    existingFlag.UserOverrides[userId] = enabled;
                    _logger.LogInformation("Set user override for feature flag {FlagName}, User: {UserId}, Enabled: {Enabled}", 
                        flagName, userId, enabled);
                }
                else
                {
                    existingFlag.Enabled = enabled;
                    _logger.LogInformation("Set global state for feature flag {FlagName}, Enabled: {Enabled}", flagName, enabled);
                }
                return existingFlag;
            });

        return Task.CompletedTask;
    }

    public Task<int> GetRolloutPercentageAsync(string flagName, CancellationToken cancellationToken = default)
    {
        if (_flags.TryGetValue(flagName, out var flag))
        {
            return Task.FromResult(flag.RolloutPercentage);
        }
        
        return Task.FromResult(0);
    }

    /// <summary>
    /// Sets the rollout percentage for a feature flag.
    /// </summary>
    public Task SetRolloutPercentageAsync(string flagName, int percentage, CancellationToken cancellationToken = default)
    {
        if (percentage < 0 || percentage > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), "Percentage must be between 0 and 100");
        }

        _flags.AddOrUpdate(flagName,
            new FeatureFlag { Name = flagName, Enabled = true, RolloutPercentage = percentage },
            (_, existingFlag) =>
            {
                existingFlag.RolloutPercentage = percentage;
                return existingFlag;
            });

        _logger.LogInformation("Set rollout percentage for feature flag {FlagName}: {Percentage}%", flagName, percentage);
        
        return Task.CompletedTask;
    }

    private void InitializeDefaultFlags()
    {
        // Initialize some example feature flags
        var defaultFlags = new[]
        {
            new FeatureFlag { Name = "premium_features", Enabled = true, RolloutPercentage = 50 },
            new FeatureFlag { Name = "admin_performance_tools", Enabled = true, RolloutPercentage = 25 },
            new FeatureFlag { Name = "experimental_tools", Enabled = false, RolloutPercentage = 5 },
            new FeatureFlag { Name = "beta_features", Enabled = true, RolloutPercentage = 75 }
        };

        foreach (var flag in defaultFlags)
        {
            _flags.TryAdd(flag.Name, flag);
        }

        _logger.LogInformation("Initialized {Count} default feature flags", defaultFlags.Length);
    }

    private static int GetUserHash(string userId, string flagName)
    {
        // Create a consistent hash for the user/flag combination
        // This ensures the same user always gets the same result for a flag
        var input = $"{userId}:{flagName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        
        // Convert first 4 bytes to int and get percentage (0-99)
        var hashInt = BitConverter.ToInt32(hash, 0);
        return Math.Abs(hashInt) % 100;
    }

    private class FeatureFlag
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int RolloutPercentage { get; set; } = 100;
        public ConcurrentDictionary<string, bool> UserOverrides { get; set; } = new();
    }
}