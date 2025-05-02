using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Auth;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Middleware that attaches WWW-Authenticate headers with resource metadata to 401 responses.
/// </summary>
public class McpAuthenticationResponseMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ResourceMetadataService _resourceMetadataService;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpAuthenticationResponseMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next request delegate in the pipeline.</param>
    /// <param name="resourceMetadataService">The resource metadata service.</param>
    public McpAuthenticationResponseMiddleware(RequestDelegate next, ResourceMetadataService resourceMetadataService)
    {
        _next = next;
        _resourceMetadataService = resourceMetadataService;
    }

    /// <summary>
    /// Processes the request and adds WWW-Authenticate headers to 401 responses.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // Add a callback to the OnStarting event which fires before the response headers are sent
        context.Response.OnStarting(() =>
        {
            // Check if the response is a 401 Unauthorized
            if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
            {
                // Get the base URL of the request
                var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
                var metadataPath = "/.well-known/oauth-protected-resource";
                var metadataUrl = $"{baseUrl}{metadataPath}";

                // Add or update the WWW-Authenticate header
                if (!context.Response.Headers.ContainsKey("WWW-Authenticate") ||
                    !context.Response.Headers["WWW-Authenticate"].ToString().Contains("resource_metadata"))
                {
                    context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{metadataUrl}\"";
                }
            }
            return Task.CompletedTask;
        });

        // Call the next delegate/middleware in the pipeline
        await _next(context);
    }
}
