using Microsoft.AspNetCore.Builder;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Extension methods for the McpAuthenticationResponseMiddleware.
/// </summary>
public static class McpAuthenticationResponseMiddlewareExtensions
{
    /// <summary>
    /// Adds the MCP authentication response middleware to the application pipeline.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseWwwAuthenticateHeaderMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<McpAuthenticationResponseMiddleware>();
    }
}