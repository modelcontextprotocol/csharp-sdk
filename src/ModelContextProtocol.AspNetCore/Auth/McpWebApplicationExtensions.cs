using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Extension methods for WebApplication to add MCP-specific middleware.
/// </summary>
public static class McpWebApplicationExtensions
{
    /// <summary>
    /// Adds the MCP authentication response middleware to the application pipeline.
    /// This middleware automatically adds the resource_metadata field to WWW-Authenticate headers in 401 responses.
    /// </summary>
    /// <remarks>
    /// This middleware should be registered AFTER UseAuthentication() but BEFORE UseAuthorization().
    /// </remarks>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static IApplicationBuilder UseMcpAuthenticationResponse(this IApplicationBuilder app)
    {
        if (app.ApplicationServices.GetService<McpAuthenticationResponseMarker>() == null)
        {
            throw new InvalidOperationException(
                "McpAuthenticationResponseMarker service is not registered. " +
                "Make sure you call AddMcpServer().WithAuthorization() first.");
        }
        
        return app.UseMiddleware<McpAuthenticationResponseMiddleware>();
    }
}