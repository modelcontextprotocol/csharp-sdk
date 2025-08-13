namespace DynamicToolFiltering.Configuration;

/// <summary>
/// Configuration options for dynamic tool filtering system.
/// </summary>
public class FilteringOptions
{
    public const string SectionName = "Filtering";

    /// <summary>
    /// Gets or sets whether filtering is enabled globally.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the default behavior when no filters match (allow or deny).
    /// </summary>
    public string DefaultBehavior { get; set; } = "deny";

    /// <summary>
    /// Gets or sets role-based filtering configuration.
    /// </summary>
    public RoleBasedFilteringOptions RoleBased { get; set; } = new();

    /// <summary>
    /// Gets or sets time-based filtering configuration.
    /// </summary>
    public TimeBasedFilteringOptions TimeBased { get; set; } = new();

    /// <summary>
    /// Gets or sets scope-based filtering configuration.
    /// </summary>
    public ScopeBasedFilteringOptions ScopeBased { get; set; } = new();

    /// <summary>
    /// Gets or sets rate limiting configuration.
    /// </summary>
    public RateLimitingOptions RateLimiting { get; set; } = new();

    /// <summary>
    /// Gets or sets tenant isolation configuration.
    /// </summary>
    public TenantIsolationOptions TenantIsolation { get; set; } = new();

    /// <summary>
    /// Gets or sets business logic filtering configuration.
    /// </summary>
    public BusinessLogicFilteringOptions BusinessLogic { get; set; } = new();
}

/// <summary>
/// Configuration for role-based filtering.
/// </summary>
public class RoleBasedFilteringOptions
{
    /// <summary>
    /// Gets or sets whether role-based filtering is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the priority of the role-based filter.
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Gets or sets the claim type that contains user roles.
    /// </summary>
    public string RoleClaimType { get; set; } = "role";

    /// <summary>
    /// Gets or sets the mapping of tool patterns to required roles.
    /// </summary>
    public Dictionary<string, string[]> ToolRoleMapping { get; set; } = new()
    {
        { "admin_*", new[] { "admin", "super_admin" } },
        { "premium_*", new[] { "premium", "admin", "super_admin" } },
        { "*_user_*", new[] { "user", "premium", "admin", "super_admin" } },
        { "*", new[] { "guest", "user", "premium", "admin", "super_admin" } }
    };

    /// <summary>
    /// Gets or sets whether to use hierarchical roles (admin inherits user permissions).
    /// </summary>
    public bool UseHierarchicalRoles { get; set; } = true;

    /// <summary>
    /// Gets or sets the role hierarchy from highest to lowest privilege.
    /// </summary>
    public string[] RoleHierarchy { get; set; } = { "super_admin", "admin", "premium", "user", "guest" };
}

/// <summary>
/// Configuration for time-based filtering.
/// </summary>
public class TimeBasedFilteringOptions
{
    /// <summary>
    /// Gets or sets whether time-based filtering is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the priority of the time-based filter.
    /// </summary>
    public int Priority { get; set; } = 200;

    /// <summary>
    /// Gets or sets the timezone for time-based filtering.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// Gets or sets business hours when certain tools are available.
    /// </summary>
    public BusinessHoursOptions BusinessHours { get; set; } = new();

    /// <summary>
    /// Gets or sets maintenance windows when tools are restricted.
    /// </summary>
    public MaintenanceWindowOptions[] MaintenanceWindows { get; set; } = Array.Empty<MaintenanceWindowOptions>();
}

/// <summary>
/// Configuration for business hours.
/// </summary>
public class BusinessHoursOptions
{
    /// <summary>
    /// Gets or sets whether business hours restrictions are enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the start time for business hours (24-hour format).
    /// </summary>
    public string StartTime { get; set; } = "09:00";

    /// <summary>
    /// Gets or sets the end time for business hours (24-hour format).
    /// </summary>
    public string EndTime { get; set; } = "17:00";

    /// <summary>
    /// Gets or sets the days of week for business hours.
    /// </summary>
    public string[] BusinessDays { get; set; } = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };

    /// <summary>
    /// Gets or sets tool patterns that are restricted to business hours.
    /// </summary>
    public string[] RestrictedTools { get; set; } = { "admin_*" };
}

/// <summary>
/// Configuration for maintenance windows.
/// </summary>
public class MaintenanceWindowOptions
{
    /// <summary>
    /// Gets or sets the start time of the maintenance window.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the end time of the maintenance window.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Gets or sets tool patterns that are blocked during maintenance.
    /// </summary>
    public string[] BlockedTools { get; set; } = { "*" };

    /// <summary>
    /// Gets or sets whether this maintenance window is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the description of the maintenance window.
    /// </summary>
    public string Description { get; set; } = "";
}

/// <summary>
/// Configuration for scope-based filtering.
/// </summary>
public class ScopeBasedFilteringOptions
{
    /// <summary>
    /// Gets or sets whether scope-based filtering is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the priority of the scope-based filter.
    /// </summary>
    public int Priority { get; set; } = 150;

    /// <summary>
    /// Gets or sets the claim type that contains scopes.
    /// </summary>
    public string ScopeClaimType { get; set; } = "scope";

    /// <summary>
    /// Gets or sets the mapping of tool patterns to required scopes.
    /// </summary>
    public Dictionary<string, string[]> ToolScopeMapping { get; set; } = new()
    {
        { "admin_*", new[] { "admin:tools" } },
        { "premium_*", new[] { "premium:tools" } },
        { "*_user_*", new[] { "user:tools" } },
        { "get_*", new[] { "read:tools" } },
        { "*", new[] { "basic:tools" } }
    };
}

/// <summary>
/// Configuration for rate limiting.
/// </summary>
public class RateLimitingOptions
{
    /// <summary>
    /// Gets or sets whether rate limiting is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the priority of the rate limiting filter.
    /// </summary>
    public int Priority { get; set; } = 50;

    /// <summary>
    /// Gets or sets the time window for rate limiting in minutes.
    /// </summary>
    public int WindowMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets rate limits per user role.
    /// </summary>
    public Dictionary<string, int> RoleLimits { get; set; } = new()
    {
        { "guest", 10 },
        { "user", 100 },
        { "premium", 500 },
        { "admin", 1000 },
        { "super_admin", -1 } // -1 means unlimited
    };

    /// <summary>
    /// Gets or sets per-tool rate limits that override role limits.
    /// </summary>
    public Dictionary<string, int> ToolLimits { get; set; } = new()
    {
        { "premium_performance_benchmark", 5 },
        { "admin_*", 50 }
    };

    /// <summary>
    /// Gets or sets whether to use sliding window (true) or fixed window (false).
    /// </summary>
    public bool UseSlidingWindow { get; set; } = true;
}

/// <summary>
/// Configuration for tenant isolation.
/// </summary>
public class TenantIsolationOptions
{
    /// <summary>
    /// Gets or sets whether tenant isolation is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the priority of the tenant isolation filter.
    /// </summary>
    public int Priority { get; set; } = 75;

    /// <summary>
    /// Gets or sets the claim type that contains tenant ID.
    /// </summary>
    public string TenantClaimType { get; set; } = "tenant_id";

    /// <summary>
    /// Gets or sets the header name for tenant ID (alternative to claims).
    /// </summary>
    public string TenantHeaderName { get; set; } = "X-Tenant-ID";

    /// <summary>
    /// Gets or sets tenant-specific tool access configuration.
    /// </summary>
    public Dictionary<string, TenantConfiguration> TenantConfigurations { get; set; } = new();
}

/// <summary>
/// Configuration for a specific tenant.
/// </summary>
public class TenantConfiguration
{
    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this tenant is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets tool patterns allowed for this tenant.
    /// </summary>
    public string[] AllowedTools { get; set; } = { "*" };

    /// <summary>
    /// Gets or sets tool patterns explicitly denied for this tenant.
    /// </summary>
    public string[] DeniedTools { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets custom rate limits for this tenant.
    /// </summary>
    public Dictionary<string, int> CustomRateLimits { get; set; } = new();
}

/// <summary>
/// Configuration for business logic filtering.
/// </summary>
public class BusinessLogicFilteringOptions
{
    /// <summary>
    /// Gets or sets whether business logic filtering is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the priority of the business logic filter.
    /// </summary>
    public int Priority { get; set; } = 300;

    /// <summary>
    /// Gets or sets feature flag configuration.
    /// </summary>
    public FeatureFlagOptions FeatureFlags { get; set; } = new();

    /// <summary>
    /// Gets or sets quota management configuration.
    /// </summary>
    public QuotaManagementOptions QuotaManagement { get; set; } = new();

    /// <summary>
    /// Gets or sets environment-based restrictions.
    /// </summary>
    public EnvironmentRestrictionOptions EnvironmentRestrictions { get; set; } = new();
}

/// <summary>
/// Configuration for feature flags.
/// </summary>
public class FeatureFlagOptions
{
    /// <summary>
    /// Gets or sets whether feature flag filtering is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets feature flag mappings for tools.
    /// </summary>
    public Dictionary<string, string> ToolFeatureMapping { get; set; } = new()
    {
        { "premium_*", "premium_features" },
        { "admin_performance_*", "admin_performance_tools" }
    };

    /// <summary>
    /// Gets or sets the default state for unknown feature flags.
    /// </summary>
    public bool DefaultFeatureFlagState { get; set; } = false;
}

/// <summary>
/// Configuration for quota management.
/// </summary>
public class QuotaManagementOptions
{
    /// <summary>
    /// Gets or sets whether quota management is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the quota period in days.
    /// </summary>
    public int QuotaPeriodDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets quota limits per user role.
    /// </summary>
    public Dictionary<string, int> RoleQuotas { get; set; } = new()
    {
        { "user", 1000 },
        { "premium", 10000 },
        { "admin", -1 } // -1 means unlimited
    };

    /// <summary>
    /// Gets or sets quota costs per tool pattern.
    /// </summary>
    public Dictionary<string, int> ToolQuotaCosts { get; set; } = new()
    {
        { "premium_performance_benchmark", 10 },
        { "premium_*", 2 },
        { "*", 1 }
    };
}

/// <summary>
/// Configuration for environment-based restrictions.
/// </summary>
public class EnvironmentRestrictionOptions
{
    /// <summary>
    /// Gets or sets whether environment restrictions are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets tool patterns restricted in production.
    /// </summary>
    public string[] ProductionRestrictedTools { get; set; } = 
    {
        "admin_force_gc",
        "admin_list_processes"
    };

    /// <summary>
    /// Gets or sets tool patterns only available in development.
    /// </summary>
    public string[] DevelopmentOnlyTools { get; set; } = Array.Empty<string>();
}