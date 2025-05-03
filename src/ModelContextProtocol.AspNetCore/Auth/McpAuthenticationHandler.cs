using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Authentication handler for MCP protocol that adds resource metadata to challenge responses.
/// </summary>
public class McpAuthenticationHandler : AuthenticationHandler<McpAuthenticationOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpAuthenticationHandler"/> class.
    /// </summary>
    public McpAuthenticationHandler(
        IOptionsMonitor<McpAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
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
        Response.StatusCode = StatusCodes.Status401Unauthorized;

        // Generate the full resource metadata URL based on the current request
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        
        // Properly parse and validate the ResourceMetadataUri
        if (!Uri.TryCreate(Options.ResourceMetadataUri.ToString(), UriKind.Absolute, out var prmDocumentUri))
            throw new InvalidOperationException("Invalid ResourceMetadataUri in options.");

        // Verify that the URI scheme starts with "http"
        if (!prmDocumentUri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ResourceMetadataUri must use HTTP or HTTPS scheme.");

        var rawPrmDocumentUri = prmDocumentUri.ToString();

        // Initialize properties if null
        properties ??= new AuthenticationProperties();
        
        // Set the WWW-Authenticate header with the resource_metadata
        string headerValue = $"Bearer realm=\"{Scheme.Name}\"";
        headerValue += $", resource_metadata=\"{rawPrmDocumentUri}\"";
        
        Response.Headers["WWW-Authenticate"] = headerValue;
        
        // Store the resource_metadata in properties in case other handlers need it
        properties.Items["resource_metadata"] = rawPrmDocumentUri;
        
        return base.HandleChallengeAsync(properties);
    }
}
