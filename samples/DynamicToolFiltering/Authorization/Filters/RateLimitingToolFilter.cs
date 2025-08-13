using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using DynamicToolFiltering.Configuration;
using DynamicToolFiltering.Services;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace DynamicToolFiltering.Authorization.Filters;

/// <summary>
/// Rate limiting tool filter that implements quota and rate limiting functionality.
/// Supports both role-based and tool-specific rate limits with sliding or fixed windows.
/// </summary>
public class RateLimitingToolFilter : IToolFilter
{
    private readonly RateLimitingOptions _options;
    private readonly IRateLimitingService _rateLimitingService;
    private readonly ILogger<RateLimitingToolFilter> _logger;

    public RateLimitingToolFilter(
        IOptions<FilteringOptions> options, 
        IRateLimitingService rateLimitingService,
        ILogger<RateLimitingToolFilter> logger)
    {
        _options = options.Value.RateLimiting;
        _rateLimitingService = rateLimitingService;
        _logger = logger;
    }

    public int Priority => _options.Priority;

    public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        // Rate limiting doesn't affect tool visibility, only execution
        return Task.FromResult(true);
    }

    public async Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return AuthorizationResult.Allow("Rate limiting disabled");
        }

        var userId = GetUserId(context);
        var userRole = GetUserRole(context);
        
        // Get applicable rate limit for this user/tool combination
        var rateLimit = GetApplicableRateLimit(toolName, userRole);
        
        if (rateLimit == -1)
        {
            // Unlimited access
            _logger.LogDebug("Tool execution authorized (unlimited): {ToolName} for user {UserId}", toolName, userId);
            return AuthorizationResult.Allow($"User has unlimited access to tool '{toolName}'");
        }

        // Check current usage
        var windowStart = GetWindowStart();
        var currentUsage = await _rateLimitingService.GetUsageCountAsync(userId, toolName, windowStart, cancellationToken);
        
        if (currentUsage >= rateLimit)
        {
            var reason = $"Rate limit exceeded for tool '{toolName}'. Limit: {rateLimit} requests per {_options.WindowMinutes} minutes. Current usage: {currentUsage}";
            
            _logger.LogWarning("Rate limit exceeded: {ToolName} for user {UserId}. Limit: {Limit}, Current: {Current}", 
                toolName, userId, rateLimit, currentUsage);
            
            // Calculate reset time
            var resetTime = windowStart.AddMinutes(_options.WindowMinutes);
            
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "RateLimit",
                ("realm", "mcp-api"),
                ("limit", rateLimit.ToString()),
                ("remaining", Math.Max(0, rateLimit - currentUsage).ToString()),
                ("reset_time", resetTime.ToString("O")),
                ("window_minutes", _options.WindowMinutes.ToString()));

            return AuthorizationResult.DenyWithChallenge(reason, challenge);
        }

        // Record the usage
        await _rateLimitingService.RecordUsageAsync(userId, toolName, DateTime.UtcNow, cancellationToken);
        
        var remaining = rateLimit - currentUsage - 1; // -1 for the current request
        
        _logger.LogDebug("Tool execution authorized: {ToolName} for user {UserId}. Remaining: {Remaining}/{Limit}", 
            toolName, userId, remaining, rateLimit);
        
        return AuthorizationResult.Allow($"Tool '{toolName}' execution authorized. Remaining: {remaining}/{rateLimit}");
    }

    private string GetUserId(ToolAuthorizationContext context)
    {
        // Try to get user ID from claims
        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? context.User?.FindFirst("sub")?.Value
                   ?? context.User?.FindFirst("user_id")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            return userId;
        }

        // For anonymous users, use a combination of IP and user agent as identifier
        var clientInfo = context.Session?.ClientInfo?.Name ?? "unknown";
        return $"anonymous_{clientInfo.GetHashCode():X8}";
    }

    private string GetUserRole(ToolAuthorizationContext context)
    {
        var role = context.User?.FindFirst(ClaimTypes.Role)?.Value
                ?? context.User?.FindFirst("role")?.Value;

        if (!string.IsNullOrEmpty(role))
        {
            return role;
        }

        // Default role based on authentication status
        return context.User?.Identity?.IsAuthenticated == true ? "user" : "guest";
    }

    private int GetApplicableRateLimit(string toolName, string userRole)
    {
        // Check for tool-specific limits first (these override role limits)
        foreach (var toolLimit in _options.ToolLimits)
        {
            if (IsPatternMatch(toolLimit.Key, toolName))
            {
                return toolLimit.Value;
            }
        }

        // Fall back to role-based limits
        if (_options.RoleLimits.TryGetValue(userRole, out var roleLimit))
        {
            return roleLimit;
        }

        // Default to guest limits if role not found
        return _options.RoleLimits.TryGetValue("guest", out var guestLimit) ? guestLimit : 10;
    }

    private DateTime GetWindowStart()
    {
        var now = DateTime.UtcNow;
        
        if (_options.UseSlidingWindow)
        {
            // Sliding window: go back WindowMinutes from now
            return now.AddMinutes(-_options.WindowMinutes);
        }
        else
        {
            // Fixed window: align to window boundaries
            var windowMinutes = _options.WindowMinutes;
            var minutesSinceEpoch = (long)(now - DateTime.UnixEpoch).TotalMinutes;
            var windowStart = minutesSinceEpoch - (minutesSinceEpoch % windowMinutes);
            return DateTime.UnixEpoch.AddMinutes(windowStart);
        }
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