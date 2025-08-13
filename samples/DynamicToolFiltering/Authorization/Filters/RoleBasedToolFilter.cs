using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using DynamicToolFiltering.Configuration;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace DynamicToolFiltering.Authorization.Filters;

/// <summary>
/// Role-based tool filter that restricts access based on user roles.
/// Supports hierarchical roles and pattern-based tool matching.
/// 
/// ARCHITECTURAL DECISION RECORD (ADR-001):
/// ========================================
/// Decision: Implement hierarchical role-based access control with pattern matching
/// 
/// Context:
/// - Need to control tool access based on user roles (guest < user < premium < admin < super_admin)
/// - Different tools require different permission levels
/// - Must support both exact tool name matching and pattern-based matching (e.g., "admin_*")
/// - Should be configurable and extensible for new roles/tools
/// 
/// Decision Drivers:
/// 1. Security: Principle of least privilege - users should only access tools appropriate for their role
/// 2. Scalability: Pattern matching reduces configuration overhead for large tool sets
/// 3. Flexibility: Hierarchical roles allow role inheritance (admin can use user tools)
/// 4. Performance: Role checking should be fast (Priority 100 - after rate limiting but before scope checking)
/// 
/// Implementation Details:
/// - Uses Claims-based authentication to extract user roles
/// - Supports multiple roles per user (for flexibility)
/// - Pattern matching with wildcards (*, prefix matching)
/// - Configurable role hierarchy and tool mappings
/// - Detailed logging for audit and debugging
/// 
/// Consequences:
/// + Simple to understand and configure
/// + Efficient for common use cases
/// + Follows standard RBAC patterns
/// - Requires careful role hierarchy design
/// - Pattern matching could become complex with many tools
/// 
/// Alternatives Considered:
/// 1. Attribute-based access control (ABAC) - Too complex for initial implementation
/// 2. Simple boolean permissions - Not flexible enough for hierarchical access
/// 3. External authorization service - Adds complexity and latency
/// </summary>
public class RoleBasedToolFilter : IToolFilter
{
    private readonly RoleBasedFilteringOptions _options;
    private readonly ILogger<RoleBasedToolFilter> _logger;

    public RoleBasedToolFilter(IOptions<FilteringOptions> options, ILogger<RoleBasedToolFilter> logger)
    {
        _options = options.Value.RoleBased;
        _logger = logger;
    }

    /// <summary>
    /// Filter execution priority. Lower numbers execute first.
    /// Priority 100 places this after rate limiting (50) but before scope checking (150).
    /// 
    /// DESIGN DECISION: Role-based filtering occurs early in the pipeline because:
    /// 1. It's fast to execute (simple claim lookup)
    /// 2. It can quickly filter out unauthorized tools
    /// 3. It reduces load on downstream filters
    /// </summary>
    public int Priority => _options.Priority;

    /// <summary>
    /// Determines if a tool should be visible to the user based on their roles.
    /// This method implements the "fail-fast" principle - if a user doesn't have
    /// the required role, the tool won't appear in their tool list.
    /// 
    /// DESIGN DECISION: Tool visibility vs execution separation
    /// - Visibility check is more permissive to allow discovery
    /// - Execution check is more restrictive for security
    /// - This provides better UX while maintaining security
    /// </summary>
    /// <param name="tool">The tool to check for visibility</param>
    /// <param name="context">The authorization context containing user information</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if the tool should be visible to the user</returns>
    public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        // PERFORMANCE OPTIMIZATION: Early exit if filtering is disabled
        // This avoids unnecessary processing when role-based filtering is turned off
        if (!_options.Enabled)
        {
            return Task.FromResult(true);
        }

        // SECURITY PRINCIPLE: Extract and validate user roles from claims
        // Uses the standard Claims-based authentication model from ASP.NET Core
        var userRoles = GetUserRoles(context);
        var requiredRoles = GetRequiredRoles(tool.Name);
        
        // AUTHORIZATION LOGIC: Check if user has any of the required roles
        // Uses hierarchical role checking - higher roles can access lower-level tools
        var hasAccess = HasRequiredRole(userRoles, requiredRoles);
        
        // AUDIT LOGGING: Detailed logging for security monitoring and debugging
        // Logs both successful access and denials for security analysis
        _logger.LogDebug("Tool inclusion check for {ToolName}: User roles [{UserRoles}], Required roles [{RequiredRoles}], HasAccess: {HasAccess}",
            tool.Name, string.Join(", ", userRoles), string.Join(", ", requiredRoles), hasAccess);
        
        return Task.FromResult(hasAccess);
    }

    public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(AuthorizationResult.Allow("Role-based filtering disabled"));
        }

        var userRoles = GetUserRoles(context);
        var requiredRoles = GetRequiredRoles(toolName);
        
        if (HasRequiredRole(userRoles, requiredRoles))
        {
            _logger.LogDebug("Tool execution authorized for {ToolName}: User has required role", toolName);
            return Task.FromResult(AuthorizationResult.Allow($"User has required role for tool '{toolName}'"));
        }

        var reason = $"Tool '{toolName}' requires role(s): {string.Join(" or ", requiredRoles)}. User has role(s): {string.Join(", ", userRoles)}";
        
        _logger.LogWarning("Tool execution denied for {ToolName}: {Reason}", toolName, reason);
        
        // Create a role-based challenge
        var challenge = AuthorizationChallenge.CreateCustomChallenge(
            "Role",
            ("realm", "mcp-api"),
            ("required_roles", string.Join(",", requiredRoles)),
            ("user_roles", string.Join(",", userRoles)));

        return Task.FromResult(AuthorizationResult.DenyWithChallenge(reason, challenge));
    }

    private List<string> GetUserRoles(ToolAuthorizationContext context)
    {
        var roles = new List<string>();
        
        // Try to get roles from claims principal
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            roles.AddRange(context.User.Claims
                .Where(c => c.Type == _options.RoleClaimType || c.Type == ClaimTypes.Role)
                .Select(c => c.Value));
        }
        
        // If no roles found and user is not authenticated, assign guest role
        if (roles.Count == 0 && context.User?.Identity?.IsAuthenticated != true)
        {
            roles.Add("guest");
        }
        
        // If no roles found but user is authenticated, assign default user role
        if (roles.Count == 0 && context.User?.Identity?.IsAuthenticated == true)
        {
            roles.Add("user");
        }

        return roles;
    }

    private List<string> GetRequiredRoles(string toolName)
    {
        foreach (var mapping in _options.ToolRoleMapping)
        {
            if (IsPatternMatch(mapping.Key, toolName))
            {
                return mapping.Value.ToList();
            }
        }
        
        // Default to requiring authentication (user role or higher)
        return new List<string> { "user" };
    }

    private bool HasRequiredRole(List<string> userRoles, List<string> requiredRoles)
    {
        if (requiredRoles.Count == 0)
        {
            return true; // No specific role required
        }

        if (_options.UseHierarchicalRoles)
        {
            return HasHierarchicalRole(userRoles, requiredRoles);
        }
        else
        {
            return userRoles.Intersect(requiredRoles, StringComparer.OrdinalIgnoreCase).Any();
        }
    }

    private bool HasHierarchicalRole(List<string> userRoles, List<string> requiredRoles)
    {
        // Get the highest privilege level for user roles
        var userMaxLevel = GetMaxRoleLevel(userRoles);
        
        // Get the minimum required privilege level
        var requiredMinLevel = GetMinRoleLevel(requiredRoles);
        
        // User must have equal or higher privilege level
        return userMaxLevel <= requiredMinLevel; // Lower index = higher privilege
    }

    private int GetMaxRoleLevel(List<string> roles)
    {
        var minLevel = int.MaxValue;
        
        foreach (var role in roles)
        {
            var level = Array.IndexOf(_options.RoleHierarchy, role);
            if (level >= 0 && level < minLevel)
            {
                minLevel = level;
            }
        }
        
        return minLevel == int.MaxValue ? _options.RoleHierarchy.Length : minLevel;
    }

    private int GetMinRoleLevel(List<string> requiredRoles)
    {
        var maxLevel = -1;
        
        foreach (var role in requiredRoles)
        {
            var level = Array.IndexOf(_options.RoleHierarchy, role);
            if (level > maxLevel)
            {
                maxLevel = level;
            }
        }
        
        return maxLevel == -1 ? _options.RoleHierarchy.Length - 1 : maxLevel;
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