using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using DynamicToolFiltering.Configuration;
using DynamicToolFiltering.Services;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace DynamicToolFiltering.Authorization.Filters;

/// <summary>
/// Business logic filter that implements complex business rules including feature flags,
/// quota management, and environment-based restrictions.
/// </summary>
public class BusinessLogicFilter : IToolFilter
{
    private readonly BusinessLogicFilteringOptions _options;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly IQuotaService _quotaService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<BusinessLogicFilter> _logger;

    public BusinessLogicFilter(
        IOptions<FilteringOptions> options,
        IFeatureFlagService featureFlagService,
        IQuotaService quotaService,
        IWebHostEnvironment environment,
        ILogger<BusinessLogicFilter> logger)
    {
        _options = options.Value.BusinessLogic;
        _featureFlagService = featureFlagService;
        _quotaService = quotaService;
        _environment = environment;
        _logger = logger;
    }

    public int Priority => _options.Priority;

    public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return true;
        }

        var canAccess = await CanAccessToolAsync(tool.Name, context, cancellationToken);
        
        _logger.LogDebug("Tool inclusion check for {ToolName}: CanAccess: {CanAccess}", tool.Name, canAccess);
        
        return canAccess;
    }

    public async Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return AuthorizationResult.Allow("Business logic filtering disabled");
        }

        // Check environment restrictions first
        var environmentCheck = CheckEnvironmentRestrictions(toolName);
        if (!environmentCheck.IsAuthorized)
        {
            return environmentCheck;
        }

        // Check feature flags
        var featureFlagCheck = await CheckFeatureFlagsAsync(toolName, context, cancellationToken);
        if (!featureFlagCheck.IsAuthorized)
        {
            return featureFlagCheck;
        }

        // Check quota limits
        var quotaCheck = await CheckQuotaLimitsAsync(toolName, context, cancellationToken);
        if (!quotaCheck.IsAuthorized)
        {
            return quotaCheck;
        }

        _logger.LogDebug("Tool execution authorized by business logic filter: {ToolName}", toolName);
        return AuthorizationResult.Allow($"Tool '{toolName}' passes all business logic checks");
    }

    private async Task<bool> CanAccessToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken)
    {
        // Check environment restrictions
        if (!CheckEnvironmentRestrictions(toolName).IsAuthorized)
        {
            return false;
        }

        // Check feature flags
        if (_options.FeatureFlags.Enabled)
        {
            var featureFlag = GetFeatureFlagForTool(toolName);
            if (featureFlag != null)
            {
                var userId = GetUserId(context);
                var isEnabled = await _featureFlagService.IsEnabledAsync(featureFlag, userId, cancellationToken);
                if (!isEnabled)
                {
                    return false;
                }
            }
        }

        // Check quota availability (basic check for visibility)
        if (_options.QuotaManagement.Enabled)
        {
            var userId = GetUserId(context);
            var userRole = GetUserRole(context);
            var hasQuota = await _quotaService.HasAvailableQuotaAsync(userId, userRole, toolName, cancellationToken);
            if (!hasQuota)
            {
                return false;
            }
        }

        return true;
    }

    private AuthorizationResult CheckEnvironmentRestrictions(string toolName)
    {
        if (!_options.EnvironmentRestrictions.Enabled)
        {
            return AuthorizationResult.Allow("Environment restrictions disabled");
        }

        var environmentName = _environment.EnvironmentName;

        // Check production restrictions
        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            if (IsToolMatched(toolName, _options.EnvironmentRestrictions.ProductionRestrictedTools))
            {
                var reason = $"Tool '{toolName}' is restricted in production environment";
                
                _logger.LogWarning("Tool execution denied in production: {ToolName}", toolName);
                
                var challenge = AuthorizationChallenge.CreateCustomChallenge(
                    "Environment",
                    ("realm", "mcp-api"),
                    ("environment", environmentName),
                    ("restriction", "production_restricted"));

                return AuthorizationResult.DenyWithChallenge(reason, challenge);
            }
        }

        // Check development-only tools
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            if (IsToolMatched(toolName, _options.EnvironmentRestrictions.DevelopmentOnlyTools))
            {
                var reason = $"Tool '{toolName}' is only available in development environment";
                
                _logger.LogWarning("Tool execution denied - development only: {ToolName}", toolName);
                
                var challenge = AuthorizationChallenge.CreateCustomChallenge(
                    "Environment",
                    ("realm", "mcp-api"),
                    ("environment", environmentName),
                    ("restriction", "development_only"));

                return AuthorizationResult.DenyWithChallenge(reason, challenge);
            }
        }

        return AuthorizationResult.Allow("Environment restrictions passed");
    }

    private async Task<AuthorizationResult> CheckFeatureFlagsAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken)
    {
        if (!_options.FeatureFlags.Enabled)
        {
            return AuthorizationResult.Allow("Feature flags disabled");
        }

        var featureFlag = GetFeatureFlagForTool(toolName);
        if (featureFlag == null)
        {
            return AuthorizationResult.Allow("No feature flag required");
        }

        var userId = GetUserId(context);
        var isEnabled = await _featureFlagService.IsEnabledAsync(featureFlag, userId, cancellationToken);
        
        if (!isEnabled)
        {
            var reason = $"Tool '{toolName}' is disabled by feature flag '{featureFlag}'";
            
            _logger.LogWarning("Tool execution denied by feature flag: {ToolName}, Flag: {FeatureFlag}", toolName, featureFlag);
            
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "FeatureFlag",
                ("realm", "mcp-api"),
                ("feature_flag", featureFlag),
                ("tool_name", toolName));

            return AuthorizationResult.DenyWithChallenge(reason, challenge);
        }

        return AuthorizationResult.Allow($"Feature flag '{featureFlag}' enabled");
    }

    private async Task<AuthorizationResult> CheckQuotaLimitsAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken)
    {
        if (!_options.QuotaManagement.Enabled)
        {
            return AuthorizationResult.Allow("Quota management disabled");
        }

        var userId = GetUserId(context);
        var userRole = GetUserRole(context);
        
        // Check if user has available quota
        var hasQuota = await _quotaService.HasAvailableQuotaAsync(userId, userRole, toolName, cancellationToken);
        if (!hasQuota)
        {
            var currentUsage = await _quotaService.GetCurrentUsageAsync(userId, cancellationToken);
            var quotaLimit = await _quotaService.GetQuotaLimitAsync(userId, userRole, cancellationToken);
            
            var reason = $"Quota exceeded for tool '{toolName}'. Usage: {currentUsage}/{quotaLimit}";
            
            _logger.LogWarning("Tool execution denied - quota exceeded: {ToolName}, User: {UserId}, Usage: {Usage}/{Limit}", 
                toolName, userId, currentUsage, quotaLimit);
            
            var resetDate = await _quotaService.GetQuotaResetDateAsync(userId, cancellationToken);
            
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Quota",
                ("realm", "mcp-api"),
                ("current_usage", currentUsage.ToString()),
                ("quota_limit", quotaLimit.ToString()),
                ("reset_date", resetDate.ToString("O")),
                ("tool_name", toolName));

            return AuthorizationResult.DenyWithChallenge(reason, challenge);
        }

        // Consume quota for this operation
        var quotaCost = GetQuotaCost(toolName);
        await _quotaService.ConsumeQuotaAsync(userId, toolName, quotaCost, cancellationToken);
        
        var remainingQuota = await _quotaService.GetRemainingQuotaAsync(userId, userRole, cancellationToken);
        
        _logger.LogDebug("Quota consumed for tool: {ToolName}, User: {UserId}, Cost: {Cost}, Remaining: {Remaining}", 
            toolName, userId, quotaCost, remainingQuota);

        return AuthorizationResult.Allow($"Quota available. Cost: {quotaCost}, Remaining: {remainingQuota}");
    }

    private string GetUserId(ToolAuthorizationContext context)
    {
        return context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User?.FindFirst("sub")?.Value
            ?? context.User?.FindFirst("user_id")?.Value
            ?? "anonymous";
    }

    private string GetUserRole(ToolAuthorizationContext context)
    {
        return context.User?.FindFirst(ClaimTypes.Role)?.Value
            ?? context.User?.FindFirst("role")?.Value
            ?? (context.User?.Identity?.IsAuthenticated == true ? "user" : "guest");
    }

    private string? GetFeatureFlagForTool(string toolName)
    {
        foreach (var mapping in _options.FeatureFlags.ToolFeatureMapping)
        {
            if (IsPatternMatch(mapping.Key, toolName))
            {
                return mapping.Value;
            }
        }
        
        return null;
    }

    private int GetQuotaCost(string toolName)
    {
        foreach (var mapping in _options.QuotaManagement.ToolQuotaCosts)
        {
            if (IsPatternMatch(mapping.Key, toolName))
            {
                return mapping.Value;
            }
        }
        
        return 1; // Default cost
    }

    private bool IsToolMatched(string toolName, string[] patterns)
    {
        return patterns.Any(pattern => IsPatternMatch(pattern, toolName));
    }

    private static bool IsPatternMatch(string pattern, string toolName)
    {
        if (pattern == "*")
        {
            return true;
        }
        
        // Convert glob pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(toolName, regexPattern, RegexOptions.IgnoreCase);
    }
}