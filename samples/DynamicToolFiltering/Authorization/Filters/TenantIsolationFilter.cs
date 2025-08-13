using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using DynamicToolFiltering.Configuration;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace DynamicToolFiltering.Authorization.Filters;

/// <summary>
/// Tenant isolation filter that provides multi-tenant tool access control.
/// Restricts tool access based on tenant membership and tenant-specific configurations.
/// </summary>
public class TenantIsolationFilter : IToolFilter
{
    private readonly TenantIsolationOptions _options;
    private readonly ILogger<TenantIsolationFilter> _logger;

    public TenantIsolationFilter(IOptions<FilteringOptions> options, ILogger<TenantIsolationFilter> logger)
    {
        _options = options.Value.TenantIsolation;
        _logger = logger;
    }

    public int Priority => _options.Priority;

    public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(true);
        }

        var tenantId = GetTenantId(context);
        var canAccess = CanAccessTool(tool.Name, tenantId);
        
        _logger.LogDebug("Tool inclusion check for {ToolName}: Tenant {TenantId}, CanAccess: {CanAccess}",
            tool.Name, tenantId ?? "none", canAccess);
        
        return Task.FromResult(canAccess);
    }

    public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(AuthorizationResult.Allow("Tenant isolation disabled"));
        }

        var tenantId = GetTenantId(context);
        
        if (string.IsNullOrEmpty(tenantId))
        {
            var reason = "Tenant ID is required for tool access";
            
            _logger.LogWarning("Tool execution denied: {ToolName} - No tenant ID provided", toolName);
            
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Tenant",
                ("realm", "mcp-api"),
                ("tenant_header", _options.TenantHeaderName),
                ("tenant_claim", _options.TenantClaimType));

            return Task.FromResult(AuthorizationResult.DenyWithChallenge(reason, challenge));
        }

        if (!_options.TenantConfigurations.TryGetValue(tenantId, out var tenantConfig))
        {
            var reason = $"Unknown tenant: {tenantId}";
            
            _logger.LogWarning("Tool execution denied: {ToolName} - Unknown tenant {TenantId}", toolName, tenantId);
            
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Tenant",
                ("realm", "mcp-api"),
                ("error", "unknown_tenant"),
                ("tenant_id", tenantId));

            return Task.FromResult(AuthorizationResult.DenyWithChallenge(reason, challenge));
        }

        if (!tenantConfig.IsActive)
        {
            var reason = $"Tenant {tenantId} is currently inactive";
            
            _logger.LogWarning("Tool execution denied: {ToolName} - Inactive tenant {TenantId}", toolName, tenantId);
            
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Tenant",
                ("realm", "mcp-api"),
                ("error", "tenant_inactive"),
                ("tenant_id", tenantId));

            return Task.FromResult(AuthorizationResult.DenyWithChallenge(reason, challenge));
        }

        // Check if tool is explicitly denied for this tenant
        if (IsToolDenied(toolName, tenantConfig.DeniedTools))
        {
            var reason = $"Tool '{toolName}' is not available for tenant {tenantId}";
            
            _logger.LogWarning("Tool execution denied: {ToolName} - Explicitly denied for tenant {TenantId}", toolName, tenantId);
            
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Tenant",
                ("realm", "mcp-api"),
                ("error", "tool_denied"),
                ("tenant_id", tenantId),
                ("tool_name", toolName));

            return Task.FromResult(AuthorizationResult.DenyWithChallenge(reason, challenge));
        }

        // Check if tool is allowed for this tenant
        if (!IsToolAllowed(toolName, tenantConfig.AllowedTools))
        {
            var reason = $"Tool '{toolName}' is not in the allowed tools list for tenant {tenantId}";
            
            _logger.LogWarning("Tool execution denied: {ToolName} - Not in allowed list for tenant {TenantId}", toolName, tenantId);
            
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Tenant",
                ("realm", "mcp-api"),
                ("error", "tool_not_allowed"),
                ("tenant_id", tenantId),
                ("tool_name", toolName));

            return Task.FromResult(AuthorizationResult.DenyWithChallenge(reason, challenge));
        }

        _logger.LogDebug("Tool execution authorized for tenant: {ToolName}, Tenant: {TenantId}", toolName, tenantId);
        
        return Task.FromResult(AuthorizationResult.Allow($"Tool '{toolName}' is available for tenant {tenantId}"));
    }

    private string? GetTenantId(ToolAuthorizationContext context)
    {
        // Try to get tenant ID from claims first
        var tenantId = context.User?.FindFirst(_options.TenantClaimType)?.Value;
        
        if (!string.IsNullOrEmpty(tenantId))
        {
            return tenantId;
        }

        // Try to get tenant ID from HTTP headers (if available in context)
        // Note: This would require extending ToolAuthorizationContext to include HTTP context
        // For now, we'll rely on claims-based approach
        
        return null;
    }

    private bool CanAccessTool(string toolName, string? tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            return false; // No tenant, no access
        }

        if (!_options.TenantConfigurations.TryGetValue(tenantId, out var tenantConfig))
        {
            return false; // Unknown tenant
        }

        if (!tenantConfig.IsActive)
        {
            return false; // Inactive tenant
        }

        // Check denied tools first
        if (IsToolDenied(toolName, tenantConfig.DeniedTools))
        {
            return false;
        }

        // Check allowed tools
        return IsToolAllowed(toolName, tenantConfig.AllowedTools);
    }

    private bool IsToolAllowed(string toolName, string[] allowedPatterns)
    {
        return allowedPatterns.Any(pattern => IsPatternMatch(pattern, toolName));
    }

    private bool IsToolDenied(string toolName, string[] deniedPatterns)
    {
        return deniedPatterns.Any(pattern => IsPatternMatch(pattern, toolName));
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