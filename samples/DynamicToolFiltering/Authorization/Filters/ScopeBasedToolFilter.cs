using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using DynamicToolFiltering.Configuration;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace DynamicToolFiltering.Authorization.Filters;

/// <summary>
/// Scope-based tool filter that implements OAuth2-style scope checking.
/// Restricts tool access based on granted scopes in JWT tokens or claims.
/// </summary>
public class ScopeBasedToolFilter : IToolFilter
{
    private readonly ScopeBasedFilteringOptions _options;
    private readonly ILogger<ScopeBasedToolFilter> _logger;

    public ScopeBasedToolFilter(IOptions<FilteringOptions> options, ILogger<ScopeBasedToolFilter> logger)
    {
        _options = options.Value.ScopeBased;
        _logger = logger;
    }

    public int Priority => _options.Priority;

    public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(true);
        }

        var userScopes = GetUserScopes(context);
        var requiredScopes = GetRequiredScopes(tool.Name);
        
        var hasAccess = HasRequiredScope(userScopes, requiredScopes);
        
        _logger.LogDebug("Tool inclusion check for {ToolName}: User scopes [{UserScopes}], Required scopes [{RequiredScopes}], HasAccess: {HasAccess}",
            tool.Name, string.Join(", ", userScopes), string.Join(", ", requiredScopes), hasAccess);
        
        return Task.FromResult(hasAccess);
    }

    public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(AuthorizationResult.Allow("Scope-based filtering disabled"));
        }

        var userScopes = GetUserScopes(context);
        var requiredScopes = GetRequiredScopes(toolName);
        
        if (HasRequiredScope(userScopes, requiredScopes))
        {
            _logger.LogDebug("Tool execution authorized for {ToolName}: User has required scope", toolName);
            return Task.FromResult(AuthorizationResult.Allow($"User has required scope for tool '{toolName}'"));
        }

        var reason = $"Tool '{toolName}' requires scope(s): {string.Join(" or ", requiredScopes)}";
        
        _logger.LogWarning("Tool execution denied for {ToolName}: Insufficient scope. User scopes: [{UserScopes}], Required: [{RequiredScopes}]", 
            toolName, string.Join(", ", userScopes), string.Join(", ", requiredScopes));
        
        // Determine the most appropriate scope to request
        var suggestedScope = requiredScopes.FirstOrDefault() ?? "basic:tools";
        
        // Create OAuth2-style Bearer challenge with insufficient_scope error
        return Task.FromResult(AuthorizationResult.DenyInsufficientScope(suggestedScope, "mcp-api"));
    }

    private List<string> GetUserScopes(ToolAuthorizationContext context)
    {
        var scopes = new List<string>();
        
        // Try to get scopes from claims principal
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            // Check for scope claim (OAuth2 standard)
            var scopeClaims = context.User.Claims
                .Where(c => c.Type == _options.ScopeClaimType)
                .ToList();

            foreach (var scopeClaim in scopeClaims)
            {
                // OAuth2 scopes can be space-separated in a single claim
                var scopeValues = scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                scopes.AddRange(scopeValues);
            }

            // Also check for individual scope claims (some implementations use this pattern)
            scopes.AddRange(context.User.Claims
                .Where(c => c.Type.StartsWith("scope:", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Type["scope:".Length..]));
        }
        
        // If no scopes found and user is not authenticated, assign basic public scope
        if (scopes.Count == 0 && context.User?.Identity?.IsAuthenticated != true)
        {
            scopes.Add("basic:tools");
        }
        
        // If no scopes found but user is authenticated, assign basic authenticated scope
        if (scopes.Count == 0 && context.User?.Identity?.IsAuthenticated == true)
        {
            scopes.Add("user:tools");
        }

        return scopes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> GetRequiredScopes(string toolName)
    {
        foreach (var mapping in _options.ToolScopeMapping)
        {
            if (IsPatternMatch(mapping.Key, toolName))
            {
                return mapping.Value.ToList();
            }
        }
        
        // Default to requiring basic tools scope
        return new List<string> { "basic:tools" };
    }

    private bool HasRequiredScope(List<string> userScopes, List<string> requiredScopes)
    {
        if (requiredScopes.Count == 0)
        {
            return true; // No specific scope required
        }

        // User needs at least one of the required scopes
        return requiredScopes.Any(requiredScope => 
            userScopes.Any(userScope => 
                IsScopeMatch(userScope, requiredScope)));
    }

    private static bool IsScopeMatch(string userScope, string requiredScope)
    {
        // Exact match
        if (string.Equals(userScope, requiredScope, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Hierarchical scope matching (e.g., "admin:tools" implies "user:tools")
        // This implements a simple hierarchical model where broader scopes include narrower ones
        var scopeHierarchy = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "admin:tools", new[] { "admin:tools", "premium:tools", "user:tools", "read:tools", "basic:tools" } },
            { "premium:tools", new[] { "premium:tools", "user:tools", "read:tools", "basic:tools" } },
            { "user:tools", new[] { "user:tools", "read:tools", "basic:tools" } },
            { "read:tools", new[] { "read:tools", "basic:tools" } },
            { "basic:tools", new[] { "basic:tools" } }
        };

        if (scopeHierarchy.TryGetValue(userScope, out var impliedScopes))
        {
            return impliedScopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
        }

        // Wildcard matching for custom scopes (e.g., "tools:*" matches "tools:read")
        if (userScope.EndsWith(":*"))
        {
            var scopePrefix = userScope[..^1]; // Remove the "*"
            return requiredScope.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
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