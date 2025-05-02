using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        builder.Services.TryAddSingleton<ResourceMetadataService>();

        return builder.AddScheme<McpAuthenticationOptions, McpAuthenticationHandler>(
            "McpAuth", 
            "MCP Authentication", 
            configureOptions ?? (options => { }));
    }
}
