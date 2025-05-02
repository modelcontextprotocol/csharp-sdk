using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Extension methods for WebApplication to add MCP-specific middleware.
/// </summary>
public static class McpWebApplicationExtensions
{
    /// <summary>
    /// This method maintains compatibility with existing code that calls UseMcpAuthenticationResponse.
    /// The actual middleware functionality is now handled by the McpAuthenticationHandler as part of
    /// the standard ASP.NET Core authentication pipeline.
    /// </summary>
    /// <remarks>
    /// While this method is still required for backward compatibility, its functionality
    /// is now fully implemented by the McpAuthenticationHandler.
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
        
        // Return the app directly without adding middleware, as the functionality
        // is now provided by the McpAuthenticationHandler
        return app;
    }
}