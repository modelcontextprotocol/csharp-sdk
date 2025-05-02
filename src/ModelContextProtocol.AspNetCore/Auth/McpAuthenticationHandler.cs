using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Authentication handler for MCP protocol that adds resource metadata to challenge responses.
/// </summary>
public class McpAuthenticationHandler : AuthenticationHandler<McpAuthenticationOptions>
{
    private readonly ResourceMetadataService _resourceMetadataService;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpAuthenticationHandler"/> class.
    /// </summary>
    public McpAuthenticationHandler(
        IOptionsMonitor<McpAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ResourceMetadataService resourceMetadataService)
        : base(options, logger, encoder)
    {
        _resourceMetadataService = resourceMetadataService;
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // This handler doesn't perform authentication - it only adds resource metadata to challenges
        // The actual authentication will be handled by the bearer token handler or other authentication handlers
        return Task.FromResult(AuthenticateResult.NoResult());
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Set the response status code
        Response.StatusCode = 401; // Unauthorized

        // Generate the full resource metadata URL based on the current request
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var metadataPath = Options.ResourceMetadataUri?.ToString() ?? "/.well-known/oauth-protected-resource";
        var metadataUrl = metadataPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? metadataPath
            : $"{baseUrl}{metadataPath}";

        // Initialize properties if null
        properties ??= new AuthenticationProperties();
        
        // Set up the resource URI if not already configured, using the current request as a fallback
        if (Options.ResourceMetadata.Resource == null)
        {
            Options.ResourceMetadata.Resource = new Uri($"{Request.Scheme}://{Request.Host}");
        }
        
        // Configure the resource metadata service with our metadata
        _resourceMetadataService.ConfigureMetadata(metadata => {
            metadata.Resource = Options.ResourceMetadata.Resource;
            metadata.AuthorizationServers = Options.ResourceMetadata.AuthorizationServers;
            metadata.BearerMethodsSupported = Options.ResourceMetadata.BearerMethodsSupported;
            metadata.ScopesSupported = Options.ResourceMetadata.ScopesSupported;
            metadata.ResourceDocumentation = Options.ResourceMetadata.ResourceDocumentation;
        });
        
        // Set the WWW-Authenticate header with the resource_metadata
        string headerValue = $"Bearer realm=\"{Scheme.Name}\"";
        headerValue += $", resource_metadata=\"{metadataUrl}\"";
        
        Response.Headers["WWW-Authenticate"] = headerValue;
        
        // Store the resource_metadata in properties in case other handlers need it
        properties.Items["resource_metadata"] = metadataUrl;
        
        return base.HandleChallengeAsync(properties);
    }
}
