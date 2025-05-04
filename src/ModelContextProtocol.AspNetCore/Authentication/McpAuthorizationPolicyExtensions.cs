using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.AspNetCore.Authentication;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for adding MCP authorization policies to ASP.NET Core applications.
/// </summary>
public static class McpAuthorizationExtensions
{
    /// <summary>
    /// Adds a preconfigured MCP policy to the authorization options.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    /// <param name="policyName">The name of the policy to add. Default is <see cref="McpAuthenticationDefaults.AuthenticationScheme"/>.</param>
    /// <param name="configurePolicy">An optional action to further configure the policy builder.</param>
    /// <returns>The authorization options for chaining.</returns>
    public static AuthorizationOptions AddMcpPolicy(
        this AuthorizationOptions options,
        string policyName = McpAuthenticationDefaults.AuthenticationScheme,
        Action<AuthorizationPolicyBuilder>? configurePolicy = null)
    {
        // Create a policy builder with default MCP configuration
        var policyBuilder = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddAuthenticationSchemes(McpAuthenticationDefaults.AuthenticationScheme);

        // Allow additional configuration if provided
        configurePolicy?.Invoke(policyBuilder);

        // Add the configured policy
        options.AddPolicy(policyName, policyBuilder.Build());

        return options;
    }

    /// <summary>
    /// Adds a preconfigured MCP policy that includes additional authentication schemes.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    /// <param name="additionalSchemes">Additional authentication schemes to include in the policy.</param>
    /// <param name="policyName">The name of the policy to add. Default is <see cref="McpAuthenticationDefaults.AuthenticationScheme"/>.</param>
    /// <param name="configurePolicy">An optional action to further configure the policy builder.</param>
    /// <returns>The authorization options for chaining.</returns>
    public static AuthorizationOptions AddMcpPolicy(
        this AuthorizationOptions options,
        string[] additionalSchemes,
        string policyName = McpAuthenticationDefaults.AuthenticationScheme,
        Action<AuthorizationPolicyBuilder>? configurePolicy = null)
    {
        if (additionalSchemes == null || additionalSchemes.Length == 0)
        {
            return AddMcpPolicy(options, policyName, configurePolicy);
        }

        // Create a policy builder with MCP and additional authentication schemes
        var allSchemes = new[] { McpAuthenticationDefaults.AuthenticationScheme }.Concat(additionalSchemes).ToArray();
        
        var policyBuilder = new AuthorizationPolicyBuilder(allSchemes)
            .RequireAuthenticatedUser();

        // Allow additional configuration if provided
        configurePolicy?.Invoke(policyBuilder);

        // Add the configured policy
        options.AddPolicy(policyName, policyBuilder.Build());

        return options;
    }
}