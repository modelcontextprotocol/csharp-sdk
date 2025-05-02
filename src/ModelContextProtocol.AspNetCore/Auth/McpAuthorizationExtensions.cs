// filepath: c:\Users\ddelimarsky\source\csharp-sdk-anm\src\ModelContextProtocol.AspNetCore\Auth\McpAuthorizationExtensions.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Auth;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Extension methods for adding MCP authorization support to ASP.NET Core applications.
/// </summary>
public static class McpAuthorizationExtensions
{
    /// <summary>
    /// Adds MCP authorization support to the application.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configureOptions">An action to configure MCP authentication options.</param>
    /// <returns>The authentication builder for chaining.</returns>
    public static AuthenticationBuilder AddMcpAuthorization(
        this AuthenticationBuilder builder,
        Action<McpAuthenticationOptions>? configureOptions = null)
    {
        builder.Services.TryAddSingleton<ResourceMetadataService>();

        return builder.AddScheme<McpAuthenticationOptions, McpAuthenticationHandler>(
            "McpAuth", 
            "MCP Authentication", 
            configureOptions ?? (options => { }));
    }

    /// <summary>
    /// Maps the resource metadata endpoint for MCP OAuth authorization.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <param name="configure">An action to configure the resource metadata.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapMcpResourceMetadata(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/.well-known/oauth-protected-resource",
        Action<ProtectedResourceMetadata>? configure = null)
    {
        var metadataService = endpoints.ServiceProvider.GetRequiredService<ResourceMetadataService>();
        
        if (configure != null)
        {
            metadataService.ConfigureMetadata(configure);
        }

        return endpoints.MapGet(pattern, async (HttpContext context) =>
        {
            var metadata = metadataService.GetMetadata();
            
            // Set default resource if not set
            if (metadata.Resource == null)
            {
                var request = context.Request;
                var hostString = request.Host.Value;
                var scheme = request.Scheme;
                metadata.Resource = new Uri($"{scheme}://{hostString}");
            }
            
            return Results.Json(metadata);
        })
        .AllowAnonymous()
        .WithDisplayName("MCP Resource Metadata");
    }
}

/// <summary>
/// Service for managing MCP resource metadata.
/// </summary>
public class ResourceMetadataService
{
    private readonly ProtectedResourceMetadata _metadata = new();

    /// <summary>
    /// Configures the resource metadata.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    public void ConfigureMetadata(Action<ProtectedResourceMetadata> configure)
    {
        configure(_metadata);
    }

    /// <summary>
    /// Gets the resource metadata.
    /// </summary>
    /// <returns>The resource metadata.</returns>
    public ProtectedResourceMetadata GetMetadata()
    {
        return _metadata;
    }
}