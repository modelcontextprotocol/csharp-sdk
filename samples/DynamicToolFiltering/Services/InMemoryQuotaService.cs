using System.Collections.Concurrent;
using DynamicToolFiltering.Configuration;
using Microsoft.Extensions.Options;

namespace DynamicToolFiltering.Services;

/// <summary>
/// In-memory implementation of quota service with period-based quotas.
/// Note: This is for demonstration purposes. In production, use a persistent store.
/// </summary>
public class InMemoryQuotaService : IQuotaService
{
    private readonly QuotaManagementOptions _options;
    private readonly ConcurrentDictionary<string, UserQuotaInfo> _userQuotas = new();
    private readonly ILogger<InMemoryQuotaService> _logger;
    private readonly Timer _resetTimer;

    public InMemoryQuotaService(IOptions<FilteringOptions> options, ILogger<InMemoryQuotaService> logger)
    {
        _options = options.Value.BusinessLogic.QuotaManagement;
        _logger = logger;
        
        // Check for quota resets daily
        _resetTimer = new Timer(async _ => await ProcessQuotaResetsAsync(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    public Task<bool> HasAvailableQuotaAsync(string userId, string userRole, string toolName, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(true);
        }

        var quotaLimit = GetQuotaLimitForRole(userRole);
        if (quotaLimit == -1)
        {
            return Task.FromResult(true); // Unlimited
        }

        var userQuota = GetOrCreateUserQuota(userId);
        var quotaCost = GetQuotaCost(toolName);
        
        lock (userQuota)
        {
            var hasQuota = userQuota.CurrentUsage + quotaCost <= quotaLimit;
            return Task.FromResult(hasQuota);
        }
    }

    public Task ConsumeQuotaAsync(string userId, string toolName, int cost, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        var userQuota = GetOrCreateUserQuota(userId);
        
        lock (userQuota)
        {
            userQuota.CurrentUsage += cost;
            userQuota.LastUsage = DateTime.UtcNow;
            
            // Track tool-specific usage
            userQuota.ToolUsage.TryGetValue(toolName, out var currentToolUsage);
            userQuota.ToolUsage[toolName] = currentToolUsage + cost;
            
            _logger.LogDebug("Consumed {Cost} quota for user {UserId}, tool {ToolName}. Total usage: {Usage}", 
                cost, userId, toolName, userQuota.CurrentUsage);
        }

        return Task.CompletedTask;
    }

    public Task<int> GetCurrentUsageAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(0);
        }

        var userQuota = GetOrCreateUserQuota(userId);
        
        lock (userQuota)
        {
            return Task.FromResult(userQuota.CurrentUsage);
        }
    }

    public Task<int> GetQuotaLimitAsync(string userId, string userRole, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(-1); // Unlimited when disabled
        }

        return Task.FromResult(GetQuotaLimitForRole(userRole));
    }

    public Task<int> GetRemainingQuotaAsync(string userId, string userRole, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(-1); // Unlimited when disabled
        }

        var quotaLimit = GetQuotaLimitForRole(userRole);
        if (quotaLimit == -1)
        {
            return Task.FromResult(-1); // Unlimited
        }

        var userQuota = GetOrCreateUserQuota(userId);
        
        lock (userQuota)
        {
            var remaining = Math.Max(0, quotaLimit - userQuota.CurrentUsage);
            return Task.FromResult(remaining);
        }
    }

    public Task<DateTime> GetQuotaResetDateAsync(string userId, CancellationToken cancellationToken = default)
    {
        var userQuota = GetOrCreateUserQuota(userId);
        
        lock (userQuota)
        {
            return Task.FromResult(userQuota.NextResetDate);
        }
    }

    public Task ResetQuotaAsync(string userId, CancellationToken cancellationToken = default)
    {
        var userQuota = GetOrCreateUserQuota(userId);
        
        lock (userQuota)
        {
            userQuota.CurrentUsage = 0;
            userQuota.ToolUsage.Clear();
            userQuota.NextResetDate = CalculateNextResetDate(DateTime.UtcNow);
            
            _logger.LogInformation("Reset quota for user {UserId}. Next reset: {NextReset}", userId, userQuota.NextResetDate);
        }

        return Task.CompletedTask;
    }

    public Task<Dictionary<string, int>> GetUsageBreakdownAsync(string userId, CancellationToken cancellationToken = default)
    {
        var userQuota = GetOrCreateUserQuota(userId);
        
        lock (userQuota)
        {
            return Task.FromResult(new Dictionary<string, int>(userQuota.ToolUsage));
        }
    }

    private UserQuotaInfo GetOrCreateUserQuota(string userId)
    {
        return _userQuotas.GetOrAdd(userId, _ => new UserQuotaInfo
        {
            UserId = userId,
            CurrentUsage = 0,
            NextResetDate = CalculateNextResetDate(DateTime.UtcNow),
            LastUsage = DateTime.UtcNow,
            ToolUsage = new ConcurrentDictionary<string, int>()
        });
    }

    private int GetQuotaLimitForRole(string userRole)
    {
        if (_options.RoleQuotas.TryGetValue(userRole, out var limit))
        {
            return limit;
        }
        
        // Default to user quota if role not found
        return _options.RoleQuotas.TryGetValue("user", out var userLimit) ? userLimit : 1000;
    }

    private int GetQuotaCost(string toolName)
    {
        foreach (var mapping in _options.ToolQuotaCosts)
        {
            if (IsPatternMatch(mapping.Key, toolName))
            {
                return mapping.Value;
            }
        }
        
        return 1; // Default cost
    }

    private DateTime CalculateNextResetDate(DateTime fromDate)
    {
        return fromDate.AddDays(_options.QuotaPeriodDays);
    }

    private async Task ProcessQuotaResetsAsync()
    {
        var now = DateTime.UtcNow;
        var usersToReset = new List<string>();
        
        foreach (var kvp in _userQuotas)
        {
            var userQuota = kvp.Value;
            
            lock (userQuota)
            {
                if (now >= userQuota.NextResetDate)
                {
                    usersToReset.Add(kvp.Key);
                }
            }
        }

        foreach (var userId in usersToReset)
        {
            await ResetQuotaAsync(userId);
        }

        if (usersToReset.Count > 0)
        {
            _logger.LogInformation("Reset quotas for {Count} users", usersToReset.Count);
        }
    }

    private static bool IsPatternMatch(string pattern, string toolName)
    {
        if (pattern == "*")
        {
            return true;
        }
        
        // Simple glob pattern matching
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        
        return string.Equals(pattern, toolName, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _resetTimer?.Dispose();
    }

    private class UserQuotaInfo
    {
        public string UserId { get; set; } = "";
        public int CurrentUsage { get; set; }
        public DateTime NextResetDate { get; set; }
        public DateTime LastUsage { get; set; }
        public ConcurrentDictionary<string, int> ToolUsage { get; set; } = new();
    }
}