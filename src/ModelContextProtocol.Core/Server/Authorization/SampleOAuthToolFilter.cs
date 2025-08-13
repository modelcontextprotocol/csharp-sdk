using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Sample tool filter that demonstrates OAuth2 Bearer token authorization challenges.
/// This is an example implementation showing how to use the authorization challenge system.
/// </summary>
/// <remarks>
/// This filter demonstrates how to implement proper OAuth2 authorization challenges
/// with WWW-Authenticate headers when tools require specific scopes or valid tokens.
/// </remarks>
public sealed class SampleOAuthToolFilter : IToolFilter
{
    private readonly string _requiredScope;
    private readonly string? _realm;

    /// <summary>
    /// Initializes a new instance of the <see cref="SampleOAuthToolFilter"/> class.
    /// </summary>
    /// <param name="requiredScope">The OAuth2 scope required to access tools.</param>
    /// <param name="realm">Optional realm for the WWW-Authenticate header.</param>
    public SampleOAuthToolFilter(string requiredScope, string? realm = null)
    {
        _requiredScope = requiredScope ?? throw new ArgumentNullException(nameof(requiredScope));
        _realm = realm;
    }

    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public Task<AuthorizationResult> AuthorizeAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        // Simulate checking if the user has the required scope
        // In a real implementation, you would extract and validate the Bearer token
        // from the authorization context and check its scopes
        
        if (IsHighPrivilegeTool(tool.Name))
        {
            // For high-privilege tools, require specific scope
            if (!HasRequiredScope(context))
            {
                return Task.FromResult(AuthorizationResult.DenyInsufficientScope(_requiredScope, _realm));
            }
        }
        else if (RequiresAuthentication(tool.Name))
        {
            // For tools that require authentication, check for valid token
            if (!HasValidToken(context))
            {
                return Task.FromResult(AuthorizationResult.DenyInvalidToken(_realm));
            }
        }

        // Tool is authorized
        return Task.FromResult(AuthorizationResult.Allow("Valid credentials"));
    }

    /// <summary>
    /// Determines if a tool is considered high-privilege and requires specific scopes.
    /// </summary>
    /// <param name="toolName">The name of the tool to check.</param>
    /// <returns>True if the tool requires elevated privileges, false otherwise.</returns>
    private static bool IsHighPrivilegeTool(string toolName)
    {
        // Example: Tools that modify data or access sensitive information
        return toolName.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
               toolName.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
               toolName.Contains("private", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines if a tool requires authentication.
    /// </summary>
    /// <param name="toolName">The name of the tool to check.</param>
    /// <returns>True if the tool requires authentication, false otherwise.</returns>
    private static bool RequiresAuthentication(string toolName)
    {
        // Example: Most tools require authentication except public read-only ones
        return !toolName.Contains("public", StringComparison.OrdinalIgnoreCase) &&
               !toolName.Contains("read", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Simulates checking if the current context has the required OAuth2 scope.
    /// </summary>
    /// <param name="context">The authorization context.</param>
    /// <returns>True if the required scope is present, false otherwise.</returns>
    private bool HasRequiredScope(ToolAuthorizationContext context)
    {
        // In a real implementation, you would:
        // 1. Extract the Bearer token from the authorization context
        // 2. Validate the token with your OAuth2 provider
        // 3. Check if the token includes the required scope
        
        // For this sample, simulate scope checking
        return false; // Always deny for demonstration
    }

    /// <summary>
    /// Simulates checking if the current context has a valid authentication token.
    /// </summary>
    /// <param name="context">The authorization context.</param>
    /// <returns>True if a valid token is present, false otherwise.</returns>
    private bool HasValidToken(ToolAuthorizationContext context)
    {
        // In a real implementation, you would:
        // 1. Extract the Bearer token from the authorization context
        // 2. Validate the token signature and expiration
        // 3. Verify the token with your OAuth2 provider
        
        // For this sample, simulate token validation
        return false; // Always deny for demonstration
    }
}