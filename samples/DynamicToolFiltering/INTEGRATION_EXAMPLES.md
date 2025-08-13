# Integration Examples for Dynamic Tool Filtering

This document provides practical examples for integrating the Dynamic Tool Filtering system with external services and real-world scenarios.

## Table of Contents

1. [JWT Integration with Identity Providers](#jwt-integration-with-identity-providers)
2. [Redis Integration for Rate Limiting](#redis-integration-for-rate-limiting)
3. [Database Integration for Quotas](#database-integration-for-quotas)
4. [External Feature Flag Services](#external-feature-flag-services)
5. [Multi-Tenant SaaS Integration](#multi-tenant-saas-integration)
6. [Monitoring and Observability](#monitoring-and-observability)
7. [Custom Filter Development](#custom-filter-development)

## JWT Integration with Identity Providers

### Auth0 Integration

```csharp
// Program.cs - Configure Auth0 JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://your-tenant.auth0.com/";
        options.Audience = "your-api-identifier";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    });

// Custom claims transformation for Auth0
builder.Services.AddSingleton<IClaimsTransformation, Auth0ClaimsTransformation>();

public class Auth0ClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var claimsIdentity = (ClaimsIdentity)principal.Identity!;
        
        // Map Auth0 custom claims to standard claims
        var permissions = principal.FindFirst("permissions")?.Value;
        if (!string.IsNullOrEmpty(permissions))
        {
            var permissionList = JsonSerializer.Deserialize<string[]>(permissions);
            foreach (var permission in permissionList)
            {
                claimsIdentity.AddClaim(new Claim("scope", permission));
            }
        }
        
        // Map Auth0 roles
        var roles = principal.FindFirst("https://myapp.com/roles")?.Value;
        if (!string.IsNullOrEmpty(roles))
        {
            var roleList = JsonSerializer.Deserialize<string[]>(roles);
            foreach (var role in roleList)
            {
                claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
        
        return Task.FromResult(principal);
    }
}
```

### Azure AD B2C Integration

```csharp
// Program.cs - Configure Azure AD B2C
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAdB2C"));

// appsettings.json
{
  "AzureAdB2C": {
    "Instance": "https://yourtenant.b2clogin.com",
    "ClientId": "your-client-id",
    "Domain": "yourtenant.onmicrosoft.com",
    "SignUpSignInPolicyId": "B2C_1_signupsignin1"
  }
}
```

## Redis Integration for Rate Limiting

### Production-Ready Rate Limiting Service

```csharp
// Services/RedisRateLimitingService.cs
public class RedisRateLimitingService : IRateLimitingService
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisRateLimitingService> _logger;
    private const string USAGE_KEY_PREFIX = "rate_limit:usage:";
    private const string STATISTICS_KEY_PREFIX = "rate_limit:stats:";

    public RedisRateLimitingService(IConnectionMultiplexer redis, ILogger<RedisRateLimitingService> logger)
    {
        _database = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<int> GetUsageCountAsync(string userId, string toolName, DateTime windowStart, CancellationToken cancellationToken = default)
    {
        var key = GetUsageKey(userId, toolName);
        var windowEnd = windowStart.AddHours(1); // 1-hour window
        
        var count = await _database.SortedSetCountAsync(key, 
            windowStart.Ticks, windowEnd.Ticks);
        
        return (int)count;
    }

    public async Task RecordUsageAsync(string userId, string toolName, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var key = GetUsageKey(userId, toolName);
        var score = timestamp.Ticks;
        
        // Add usage record
        await _database.SortedSetAddAsync(key, Guid.NewGuid().ToString(), score);
        
        // Set expiration for cleanup
        await _database.KeyExpireAsync(key, TimeSpan.FromDays(1));
        
        // Update statistics
        await UpdateStatisticsAsync(userId, toolName, timestamp);
        
        _logger.LogDebug("Recorded usage for {UserId}, {ToolName} at {Timestamp}", 
            userId, toolName, timestamp);
    }

    public async Task CleanupOldRecordsAsync(CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow.AddHours(-24);
        var pattern = $"{USAGE_KEY_PREFIX}*";
        
        await foreach (var key in _database.Multiplexer.GetServer().KeysAsync(pattern: pattern))
        {
            await _database.SortedSetRemoveRangeByScoreAsync(key, 
                double.NegativeInfinity, cutoffTime.Ticks);
        }
    }

    public async Task<Dictionary<string, int>> GetUsageStatisticsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var statsKey = $"{STATISTICS_KEY_PREFIX}{userId}";
        var hash = await _database.HashGetAllAsync(statsKey);
        
        return hash.ToDictionary(
            h => h.Name.ToString(),
            h => (int)h.Value
        );
    }

    private async Task UpdateStatisticsAsync(string userId, string toolName, DateTime timestamp)
    {
        var statsKey = $"{STATISTICS_KEY_PREFIX}{userId}";
        var hourKey = $"{toolName}:{timestamp:yyyy-MM-dd:HH}";
        
        await _database.HashIncrementAsync(statsKey, hourKey);
        await _database.KeyExpireAsync(statsKey, TimeSpan.FromDays(30));
    }

    private string GetUsageKey(string userId, string toolName) => 
        $"{USAGE_KEY_PREFIX}{userId}:{toolName}";
}

// Program.cs - Register Redis services
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")));

builder.Services.AddSingleton<IRateLimitingService, RedisRateLimitingService>();
```

## Database Integration for Quotas

### Entity Framework Quota Service

```csharp
// Models/QuotaDbContext.cs
public class QuotaDbContext : DbContext
{
    public QuotaDbContext(DbContextOptions<QuotaDbContext> options) : base(options) { }

    public DbSet<UserQuota> UserQuotas { get; set; }
    public DbSet<QuotaUsage> QuotaUsages { get; set; }
    public DbSet<QuotaReset> QuotaResets { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserQuota>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasMaxLength(100);
            entity.Property(e => e.Role).HasMaxLength(50);
            entity.HasIndex(e => e.Role);
            entity.HasIndex(e => e.NextResetDate);
        });

        modelBuilder.Entity<QuotaUsage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).HasMaxLength(100);
            entity.Property(e => e.ToolName).HasMaxLength(100);
            entity.HasIndex(e => new { e.UserId, e.ToolName });
            entity.HasIndex(e => e.UsageDate);
        });

        modelBuilder.Entity<QuotaReset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).HasMaxLength(100);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ResetDate);
        });
    }
}

// Models/QuotaEntities.cs
public class UserQuota
{
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "";
    public int CurrentUsage { get; set; }
    public int QuotaLimit { get; set; }
    public DateTime NextResetDate { get; set; }
    public DateTime LastUsage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class QuotaUsage
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public int Cost { get; set; }
    public DateTime UsageDate { get; set; }
    public string? RequestId { get; set; }
}

public class QuotaReset
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public DateTime ResetDate { get; set; }
    public int PreviousUsage { get; set; }
    public string ResetReason { get; set; } = "";
}

// Services/DatabaseQuotaService.cs
public class DatabaseQuotaService : IQuotaService
{
    private readonly QuotaDbContext _context;
    private readonly QuotaManagementOptions _options;
    private readonly ILogger<DatabaseQuotaService> _logger;

    public DatabaseQuotaService(
        QuotaDbContext context, 
        IOptions<FilteringOptions> options,
        ILogger<DatabaseQuotaService> logger)
    {
        _context = context;
        _options = options.Value.BusinessLogic.QuotaManagement;
        _logger = logger;
    }

    public async Task<bool> HasAvailableQuotaAsync(string userId, string userRole, string toolName, CancellationToken cancellationToken = default)
    {
        var userQuota = await GetOrCreateUserQuotaAsync(userId, userRole);
        var quotaCost = GetQuotaCost(toolName);
        
        return userQuota.QuotaLimit == -1 || // Unlimited
               userQuota.CurrentUsage + quotaCost <= userQuota.QuotaLimit;
    }

    public async Task ConsumeQuotaAsync(string userId, string toolName, int cost, CancellationToken cancellationToken = default)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        
        try
        {
            var userQuota = await _context.UserQuotas
                .FirstOrDefaultAsync(q => q.UserId == userId, cancellationToken);
            
            if (userQuota != null)
            {
                userQuota.CurrentUsage += cost;
                userQuota.LastUsage = DateTime.UtcNow;
                userQuota.UpdatedAt = DateTime.UtcNow;
            }

            var usage = new QuotaUsage
            {
                UserId = userId,
                ToolName = toolName,
                Cost = cost,
                UsageDate = DateTime.UtcNow,
                RequestId = Guid.NewGuid().ToString()
            };

            _context.QuotaUsages.Add(usage);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogDebug("Consumed {Cost} quota for user {UserId}, tool {ToolName}", 
                cost, userId, toolName);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<int> GetCurrentUsageAsync(string userId, CancellationToken cancellationToken = default)
    {
        var userQuota = await _context.UserQuotas
            .FirstOrDefaultAsync(q => q.UserId == userId, cancellationToken);
        
        return userQuota?.CurrentUsage ?? 0;
    }

    private async Task<UserQuota> GetOrCreateUserQuotaAsync(string userId, string userRole)
    {
        var userQuota = await _context.UserQuotas
            .FirstOrDefaultAsync(q => q.UserId == userId);

        if (userQuota == null)
        {
            var quotaLimit = GetQuotaLimitForRole(userRole);
            userQuota = new UserQuota
            {
                UserId = userId,
                Role = userRole,
                CurrentUsage = 0,
                QuotaLimit = quotaLimit,
                NextResetDate = CalculateNextResetDate(DateTime.UtcNow),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.UserQuotas.Add(userQuota);
            await _context.SaveChangesAsync();
        }

        return userQuota;
    }

    private int GetQuotaLimitForRole(string userRole)
    {
        return _options.RoleQuotas.TryGetValue(userRole, out var limit) ? limit : 1000;
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
        return 1;
    }

    private DateTime CalculateNextResetDate(DateTime fromDate)
    {
        return fromDate.AddDays(_options.QuotaPeriodDays);
    }

    private static bool IsPatternMatch(string pattern, string toolName)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(pattern, toolName, StringComparison.OrdinalIgnoreCase);
    }
}

// Program.cs - Register database services
builder.Services.AddDbContext<QuotaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IQuotaService, DatabaseQuotaService>();
```

## External Feature Flag Services

### LaunchDarkly Integration

```csharp
// Services/LaunchDarklyFeatureFlagService.cs
public class LaunchDarklyFeatureFlagService : IFeatureFlagService
{
    private readonly LdClient _client;
    private readonly ILogger<LaunchDarklyFeatureFlagService> _logger;

    public LaunchDarklyFeatureFlagService(IConfiguration configuration, ILogger<LaunchDarklyFeatureFlagService> logger)
    {
        var sdkKey = configuration["LaunchDarkly:SdkKey"];
        var config = Configuration.Default(sdkKey);
        _client = new LdClient(config);
        _logger = logger;
    }

    public Task<bool> IsEnabledAsync(string flagName, string userId, CancellationToken cancellationToken = default)
    {
        var user = User.WithKey(userId);
        var isEnabled = _client.BoolVariation(flagName, user, false);
        
        _logger.LogDebug("Feature flag {FlagName} for user {UserId}: {Enabled}", flagName, userId, isEnabled);
        
        return Task.FromResult(isEnabled);
    }

    public async Task<Dictionary<string, bool>> GetAllFlagsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = User.WithKey(userId);
        var allFlags = _client.AllFlagsState(user);
        
        var result = new Dictionary<string, bool>();
        foreach (var flag in allFlags.ToValuesMap())
        {
            if (flag.Value is bool boolValue)
            {
                result[flag.Key] = boolValue;
            }
        }
        
        return result;
    }

    public Task SetFlagAsync(string flagName, bool enabled, string? userId = null, CancellationToken cancellationToken = default)
    {
        // LaunchDarkly doesn't support programmatic flag setting from SDK
        // This would typically be done through their REST API or dashboard
        _logger.LogWarning("Cannot set flag {FlagName} programmatically with LaunchDarkly SDK", flagName);
        return Task.CompletedTask;
    }

    public Task<int> GetRolloutPercentageAsync(string flagName, CancellationToken cancellationToken = default)
    {
        // This would require calling LaunchDarkly's REST API to get flag configuration
        _logger.LogWarning("Cannot get rollout percentage for flag {FlagName} with current implementation", flagName);
        return Task.FromResult(100);
    }
}

// Program.cs
builder.Services.AddSingleton<IFeatureFlagService, LaunchDarklyFeatureFlagService>();
```

### Azure App Configuration Integration

```csharp
// Services/AzureFeatureFlagService.cs
public class AzureFeatureFlagService : IFeatureFlagService
{
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<AzureFeatureFlagService> _logger;

    public AzureFeatureFlagService(IFeatureManager featureManager, ILogger<AzureFeatureFlagService> logger)
    {
        _featureManager = featureManager;
        _logger = logger;
    }

    public async Task<bool> IsEnabledAsync(string flagName, string userId, CancellationToken cancellationToken = default)
    {
        var context = new TargetingContext
        {
            UserId = userId
        };

        var isEnabled = await _featureManager.IsEnabledAsync(flagName, context);
        
        _logger.LogDebug("Feature flag {FlagName} for user {UserId}: {Enabled}", flagName, userId, isEnabled);
        
        return isEnabled;
    }

    public async Task<Dictionary<string, bool>> GetAllFlagsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var context = new TargetingContext { UserId = userId };
        var result = new Dictionary<string, bool>();

        var flagNames = new[] { "premium_features", "admin_performance_tools", "experimental_tools", "beta_features" };
        
        foreach (var flagName in flagNames)
        {
            result[flagName] = await _featureManager.IsEnabledAsync(flagName, context);
        }

        return result;
    }

    // Other methods would interact with Azure App Configuration REST API
}

// Program.cs
builder.Configuration.AddAzureAppConfiguration(options =>
{
    options.Connect(builder.Configuration.GetConnectionString("AzureAppConfiguration"))
           .UseFeatureFlags();
});

builder.Services.AddFeatureManagement();
builder.Services.AddSingleton<IFeatureFlagService, AzureFeatureFlagService>();
```

## Multi-Tenant SaaS Integration

### Advanced Tenant Isolation Filter

```csharp
// Services/TenantManagementService.cs
public interface ITenantManagementService
{
    Task<TenantInfo?> GetTenantAsync(string tenantId);
    Task<List<string>> GetTenantToolsAsync(string tenantId);
    Task<Dictionary<string, int>> GetTenantRateLimitsAsync(string tenantId);
    Task<bool> IsTenantActiveAsync(string tenantId);
}

public class TenantManagementService : ITenantManagementService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantManagementService> _logger;

    public TenantManagementService(HttpClient httpClient, IMemoryCache cache, ILogger<TenantManagementService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TenantInfo?> GetTenantAsync(string tenantId)
    {
        var cacheKey = $"tenant:{tenantId}";
        
        if (_cache.TryGetValue(cacheKey, out TenantInfo? cachedTenant))
        {
            return cachedTenant;
        }

        try
        {
            var response = await _httpClient.GetAsync($"/api/tenants/{tenantId}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var tenant = JsonSerializer.Deserialize<TenantInfo>(json);
                
                _cache.Set(cacheKey, tenant, TimeSpan.FromMinutes(15));
                return tenant;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch tenant {TenantId}", tenantId);
        }

        return null;
    }

    public async Task<List<string>> GetTenantToolsAsync(string tenantId)
    {
        var tenant = await GetTenantAsync(tenantId);
        return tenant?.AllowedTools?.ToList() ?? new List<string>();
    }

    public async Task<Dictionary<string, int>> GetTenantRateLimitsAsync(string tenantId)
    {
        var tenant = await GetTenantAsync(tenantId);
        return tenant?.CustomRateLimits ?? new Dictionary<string, int>();
    }

    public async Task<bool> IsTenantActiveAsync(string tenantId)
    {
        var tenant = await GetTenantAsync(tenantId);
        return tenant?.IsActive ?? false;
    }
}

// Enhanced Tenant Isolation Filter
public class EnhancedTenantIsolationFilter : IToolFilter
{
    private readonly TenantIsolationOptions _options;
    private readonly ITenantManagementService _tenantService;
    private readonly ILogger<EnhancedTenantIsolationFilter> _logger;

    public EnhancedTenantIsolationFilter(
        IOptions<FilteringOptions> options,
        ITenantManagementService tenantService,
        ILogger<EnhancedTenantIsolationFilter> logger)
    {
        _options = options.Value.TenantIsolation;
        _tenantService = tenantService;
        _logger = logger;
    }

    public int Priority => _options.Priority;

    public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return true;

        var tenantId = GetTenantId(context);
        if (string.IsNullOrEmpty(tenantId)) return true;

        var allowedTools = await _tenantService.GetTenantToolsAsync(tenantId);
        return IsToolAllowed(tool.Name, allowedTools);
    }

    public async Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return AuthorizationResult.Allow("Tenant isolation disabled");

        var tenantId = GetTenantId(context);
        if (string.IsNullOrEmpty(tenantId))
            return AuthorizationResult.Allow("No tenant context");

        // Check if tenant is active
        if (!await _tenantService.IsTenantActiveAsync(tenantId))
        {
            var reason = $"Tenant '{tenantId}' is not active";
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Tenant",
                ("realm", "mcp-api"),
                ("tenant_id", tenantId),
                ("status", "inactive"));
            
            return AuthorizationResult.DenyWithChallenge(reason, challenge);
        }

        // Check tool access
        var allowedTools = await _tenantService.GetTenantToolsAsync(tenantId);
        if (!IsToolAllowed(toolName, allowedTools))
        {
            var reason = $"Tool '{toolName}' is not allowed for tenant '{tenantId}'";
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Tenant",
                ("realm", "mcp-api"),
                ("tenant_id", tenantId),
                ("tool_name", toolName),
                ("restriction", "tool_not_allowed"));
            
            return AuthorizationResult.DenyWithChallenge(reason, challenge);
        }

        return AuthorizationResult.Allow($"Tool '{toolName}' allowed for tenant '{tenantId}'");
    }

    private string? GetTenantId(ToolAuthorizationContext context)
    {
        // Try to get tenant ID from claims first
        var tenantClaim = context.User?.FindFirst(_options.TenantClaimType)?.Value;
        if (!string.IsNullOrEmpty(tenantClaim))
            return tenantClaim;

        // Fall back to header if available in the context
        // This would need to be passed through from the HTTP context
        return context.AdditionalData?.TryGetValue("TenantId", out var tenantId) == true 
            ? tenantId?.ToString() 
            : null;
    }

    private bool IsToolAllowed(string toolName, List<string> allowedTools)
    {
        return allowedTools.Any(pattern => IsPatternMatch(pattern, toolName));
    }

    private static bool IsPatternMatch(string pattern, string toolName)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(pattern, toolName, StringComparison.OrdinalIgnoreCase);
    }
}

// Models/TenantInfo.cs
public class TenantInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public string SubscriptionTier { get; set; } = "";
    public string[] AllowedTools { get; set; } = Array.Empty<string>();
    public string[] DeniedTools { get; set; } = Array.Empty<string>();
    public Dictionary<string, int> CustomRateLimits { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

## Monitoring and Observability

### Application Insights Integration

```csharp
// Services/TelemetryFilterWrapper.cs
public class TelemetryFilterWrapper : IToolFilter
{
    private readonly IToolFilter _innerFilter;
    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger<TelemetryFilterWrapper> _logger;

    public TelemetryFilterWrapper(IToolFilter innerFilter, TelemetryClient telemetryClient, ILogger<TelemetryFilterWrapper> logger)
    {
        _innerFilter = innerFilter;
        _telemetryClient = telemetryClient;
        _logger = logger;
    }

    public int Priority => _innerFilter.Priority;

    public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var filterName = _innerFilter.GetType().Name;
        
        try
        {
            var result = await _innerFilter.ShouldIncludeToolAsync(tool, context, cancellationToken);
            
            stopwatch.Stop();
            
            _telemetryClient.TrackDependency("Filter", filterName, $"ShouldInclude:{tool.Name}", 
                DateTime.UtcNow.Subtract(stopwatch.Elapsed), stopwatch.Elapsed, result.ToString());
            
            _telemetryClient.TrackMetric($"Filter.{filterName}.ShouldInclude.Duration", stopwatch.ElapsedMilliseconds);
            _telemetryClient.TrackMetric($"Filter.{filterName}.ShouldInclude.{(result ? "Allow" : "Deny")}", 1);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                ["FilterName"] = filterName,
                ["ToolName"] = tool.Name,
                ["Operation"] = "ShouldIncludeToolAsync"
            });
            
            throw;
        }
    }

    public async Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var filterName = _innerFilter.GetType().Name;
        
        try
        {
            var result = await _innerFilter.CanExecuteToolAsync(toolName, context, cancellationToken);
            
            stopwatch.Stop();
            
            _telemetryClient.TrackDependency("Filter", filterName, $"CanExecute:{toolName}", 
                DateTime.UtcNow.Subtract(stopwatch.Elapsed), stopwatch.Elapsed, result.IsAuthorized.ToString());
            
            _telemetryClient.TrackMetric($"Filter.{filterName}.CanExecute.Duration", stopwatch.ElapsedMilliseconds);
            _telemetryClient.TrackMetric($"Filter.{filterName}.CanExecute.{(result.IsAuthorized ? "Allow" : "Deny")}", 1);
            
            if (!result.IsAuthorized)
            {
                _telemetryClient.TrackEvent("FilterDenied", new Dictionary<string, string>
                {
                    ["FilterName"] = filterName,
                    ["ToolName"] = toolName,
                    ["Reason"] = result.Reason,
                    ["UserId"] = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Anonymous"
                });
            }
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                ["FilterName"] = filterName,
                ["ToolName"] = toolName,
                ["Operation"] = "CanExecuteToolAsync"
            });
            
            throw;
        }
    }
}

// Program.cs - Wrap filters with telemetry
builder.Services.AddApplicationInsightsTelemetry();

builder.Services.Decorate<IToolFilter, TelemetryFilterWrapper>();
```

### Prometheus Metrics Integration

```csharp
// Services/MetricsCollectionService.cs
public class MetricsCollectionService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Counter _filterExecutionCounter;
    private readonly Histogram _filterExecutionDuration;
    private readonly Gauge _activeFiltersGauge;

    public MetricsCollectionService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        
        _filterExecutionCounter = Metrics.CreateCounter(
            "filter_executions_total",
            "Total number of filter executions",
            new[] { "filter_name", "operation", "result" });
        
        _filterExecutionDuration = Metrics.CreateHistogram(
            "filter_execution_duration_seconds",
            "Duration of filter executions",
            new[] { "filter_name", "operation" });
        
        _activeFiltersGauge = Metrics.CreateGauge(
            "active_filters_count",
            "Number of active filters");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Initialize metrics collection
        using var scope = _serviceProvider.CreateScope();
        var filters = scope.ServiceProvider.GetServices<IToolFilter>();
        _activeFiltersGauge.Set(filters.Count());
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void RecordFilterExecution(string filterName, string operation, string result, double durationSeconds)
    {
        _filterExecutionCounter.WithLabels(filterName, operation, result).Inc();
        _filterExecutionDuration.WithLabels(filterName, operation).Observe(durationSeconds);
    }
}

// Program.cs - Add Prometheus
builder.Services.AddSingleton<MetricsCollectionService>();
builder.Services.AddHostedService<MetricsCollectionService>();

// In the request pipeline
app.UseMetricServer(); // Expose /metrics endpoint
```

## Custom Filter Development

### Custom Business Logic Filter Example

```csharp
// Filters/GeographicRestrictionFilter.cs
public class GeographicRestrictionFilter : IToolFilter
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeographicRestrictionFilter> _logger;
    private readonly Dictionary<string, string[]> _toolRegionMapping;

    public GeographicRestrictionFilter(IConfiguration configuration, ILogger<GeographicRestrictionFilter> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        // Load region mappings from configuration
        _toolRegionMapping = configuration.GetSection("GeographicRestrictions:ToolRegionMapping")
            .Get<Dictionary<string, string[]>>() ?? new Dictionary<string, string[]>();
    }

    public int Priority => 125; // Between role-based and scope-based

    public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        // Geographic restrictions don't affect tool visibility
        return Task.FromResult(true);
    }

    public async Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        var userRegion = GetUserRegion(context);
        var allowedRegions = GetAllowedRegions(toolName);
        
        if (allowedRegions.Length == 0)
        {
            // No geographic restrictions for this tool
            return AuthorizationResult.Allow("No geographic restrictions");
        }

        if (string.IsNullOrEmpty(userRegion))
        {
            var reason = $"Tool '{toolName}' has geographic restrictions but user region could not be determined";
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Geographic",
                ("realm", "mcp-api"),
                ("tool_name", toolName),
                ("allowed_regions", string.Join(",", allowedRegions)));
            
            return AuthorizationResult.DenyWithChallenge(reason, challenge);
        }

        if (!allowedRegions.Contains(userRegion, StringComparer.OrdinalIgnoreCase))
        {
            var reason = $"Tool '{toolName}' is not available in region '{userRegion}'. Allowed regions: {string.Join(", ", allowedRegions)}";
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Geographic",
                ("realm", "mcp-api"),
                ("tool_name", toolName),
                ("user_region", userRegion),
                ("allowed_regions", string.Join(",", allowedRegions)));
            
            _logger.LogWarning("Geographic restriction denied: {ToolName} for region {UserRegion}", toolName, userRegion);
            
            return AuthorizationResult.DenyWithChallenge(reason, challenge);
        }

        return AuthorizationResult.Allow($"Tool '{toolName}' allowed in region '{userRegion}'");
    }

    private string? GetUserRegion(ToolAuthorizationContext context)
    {
        // Try to get region from claims
        var regionClaim = context.User?.FindFirst("region")?.Value 
                       ?? context.User?.FindFirst("geo_region")?.Value;
        
        if (!string.IsNullOrEmpty(regionClaim))
            return regionClaim;

        // Could also determine region from IP address using a geolocation service
        // This would require additional context data to be passed through
        
        return context.AdditionalData?.TryGetValue("UserRegion", out var region) == true 
            ? region?.ToString() 
            : null;
    }

    private string[] GetAllowedRegions(string toolName)
    {
        foreach (var mapping in _toolRegionMapping)
        {
            if (IsPatternMatch(mapping.Key, toolName))
            {
                return mapping.Value;
            }
        }
        
        return Array.Empty<string>();
    }

    private static bool IsPatternMatch(string pattern, string toolName)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(pattern, toolName, StringComparison.OrdinalIgnoreCase);
    }
}

// Configuration example
// appsettings.json
{
  "GeographicRestrictions": {
    "Enabled": true,
    "ToolRegionMapping": {
      "admin_*": ["US", "CA", "EU"],
      "premium_financial_*": ["US", "UK", "EU"],
      "compliance_*": ["US"]
    }
  }
}

// Register the filter
builder.Services.AddSingleton<IToolFilter, GeographicRestrictionFilter>();
```

This comprehensive integration guide shows how the Dynamic Tool Filtering system can be extended and integrated with real-world services and infrastructure. Each example demonstrates production-ready patterns and best practices for building scalable, secure MCP applications.