using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Auth;
using ModelContextProtocol.Auth.Types;
using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides methods for configuring HTTP MCP servers via dependency injection.
/// </summary>
public static class HttpMcpServerBuilderExtensions
{
    /// <summary>
    /// Adds the services necessary for <see cref="M:McpEndpointRouteBuilderExtensions.MapMcp"/>
    /// to handle MCP requests and sessions using the MCP Streamable HTTP transport. For more information on configuring the underlying HTTP server
    /// to control things like port binding custom TLS certificates, see the <see href="https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis">Minimal APIs quick reference</see>.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configureOptions">Configures options for the Streamable HTTP transport. This allows configuring per-session
    /// <see cref="McpServerOptions"/> and running logic before and after a session.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithHttpTransport(this IMcpServerBuilder builder, Action<HttpServerTransportOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<StreamableHttpHandler>();
        builder.Services.TryAddSingleton<SseHandler>();
        builder.Services.AddHostedService<IdleTrackingBackgroundService>();

        if (configureOptions is not null)
        {
            builder.Services.Configure(configureOptions);
        }

        return builder;
    }
    
    /// <summary>
    /// Adds OAuth authorization support to the MCP server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configureMetadata">An action to configure the resource metadata.</param>
    /// <param name="configureOptions">An action to configure authentication options.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithAuthorization(
        this IMcpServerBuilder builder, 
        Action<ProtectedResourceMetadata>? configureMetadata = null,
        Action<McpAuthenticationOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        
        // Create and register the resource metadata service
        var resourceMetadataService = new ResourceMetadataService();
        
        // Apply configuration directly to the instance
        if (configureMetadata != null)
        {
            resourceMetadataService.ConfigureMetadata(configureMetadata);
        }
        
        // Register the configured instance as a singleton
        builder.Services.AddSingleton(resourceMetadataService);
        
        // Mark the service as having authorization enabled
        builder.Services.AddSingleton<McpAuthorizationMarker>();
        
        // Add authentication with the MCP authentication handler
        builder.Services.AddAuthentication()
            .AddMcp(options => 
            {
                // Default to the standard OAuth protected resource endpoint
                options.ResourceMetadataUri = new Uri("/.well-known/oauth-protected-resource", UriKind.Relative);
                
                // Apply custom configuration if provided
                configureOptions?.Invoke(options);
            });
        
        // Add authorization services
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("McpAuth", policy =>
            {
                policy.RequireAuthenticatedUser();
            });
        });
        
        // Register the middleware for automatically adding WWW-Authenticate headers
        // Store in DI that we need to use the middleware
        builder.Services.AddSingleton<McpAuthenticationResponseMarker>();
        
        return builder;
    }
}
