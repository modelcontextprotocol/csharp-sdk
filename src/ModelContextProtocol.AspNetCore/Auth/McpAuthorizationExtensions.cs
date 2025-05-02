using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.AspNetCore.Auth;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for adding MCP authorization support to ASP.NET Core applications.
/// </summary>
public static class McpAuthenticationExtensions
{
    /// <summary>
    /// Adds MCP authorization support to the application.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configureOptions">An action to configure MCP authentication options.</param>
    /// <returns>The authentication builder for chaining.</returns>
    public static AuthenticationBuilder AddMcp(
        this AuthenticationBuilder builder,
        Action<McpAuthenticationOptions>? configureOptions = null)
    {
        return AddMcp(builder, "McpAuth", "MCP Authentication", configureOptions);
    }

    /// <summary>
    /// Adds MCP authorization support to the application with a custom scheme name.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="authenticationScheme">The authentication scheme name to use.</param>
    /// <param name="displayName">The display name for the authentication scheme.</param>
    /// <param name="configureOptions">An action to configure MCP authentication options.</param>
    /// <returns>The authentication builder for chaining.</returns>
    public static AuthenticationBuilder AddMcp(
        this AuthenticationBuilder builder,
        string authenticationScheme,
        string displayName,
        Action<McpAuthenticationOptions>? configureOptions = null)
    {
        builder.Services.TryAddSingleton<ResourceMetadataService>();

        return builder.AddScheme<McpAuthenticationOptions, McpAuthenticationHandler>(
            authenticationScheme, 
            displayName, 
            configureOptions ?? (options => { }));
    }
}
