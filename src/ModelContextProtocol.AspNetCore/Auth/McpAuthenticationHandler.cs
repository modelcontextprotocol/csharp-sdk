using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Auth.Types;
using ModelContextProtocol.Utils.Json;
using System.Text.Encodings.Web;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Authentication handler for MCP protocol that adds resource metadata to challenge responses
/// and handles resource metadata endpoint requests.
/// </summary>
public class McpAuthenticationHandler : AuthenticationHandler<McpAuthenticationOptions>, IAuthenticationRequestHandler
{
    private readonly IOptionsMonitor<McpAuthenticationOptions> _optionsMonitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpAuthenticationHandler"/> class.
    /// </summary>
    public McpAuthenticationHandler(
        IOptionsMonitor<McpAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
        _optionsMonitor = options;
    }

    /// <inheritdoc />
    public async Task<bool> HandleRequestAsync()
    {
        // Check if the request is for the resource metadata endpoint
        string requestPath = Request.Path.Value ?? string.Empty;
        var options = _optionsMonitor.CurrentValue;
        string resourceMetadataPath = options.ResourceMetadataUri.ToString();
        
        // If the path doesn't match, let the request continue through the pipeline
        if (!string.Equals(requestPath, resourceMetadataPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // This is a request for resource metadata - handle it
        await HandleResourceMetadataRequestAsync();
        return true;
    }

    /// <summary>
    /// Handles the resource metadata request.
    /// </summary>
    private async Task HandleResourceMetadataRequestAsync()
    {
        // Get a copy of the resource metadata from options to avoid modifying the original
        var options = _optionsMonitor.CurrentValue;
        var metadata = new ProtectedResourceMetadata
        {
            AuthorizationServers = [.. options.ResourceMetadata.AuthorizationServers],
            BearerMethodsSupported = [.. options.ResourceMetadata.BearerMethodsSupported],
            ScopesSupported = [.. options.ResourceMetadata.ScopesSupported],
            ResourceDocumentation = options.ResourceMetadata.ResourceDocumentation
        };
        
        // Set default resource if not set
        if (metadata.Resource == null)
        {
            var request = Request;
            var hostString = request.Host.Value;
            var scheme = request.Scheme;
            metadata.Resource = new Uri($"{scheme}://{hostString}");
        }
        
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/json";
        
        var json = System.Text.Json.JsonSerializer.Serialize(
            metadata, 
            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ProtectedResourceMetadata)));
        
        await Response.WriteAsync(json);
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

        // Get the current options to ensure we have the latest values
        var options = _optionsMonitor.CurrentValue;
        
        // Generate the full resource metadata URL based on the current request
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        
        string resourceMetadataUriString = options.ResourceMetadataUri.ToString();
        string rawPrmDocumentUri;
        
        // Check if the URI is relative or absolute
        if (options.ResourceMetadataUri.IsAbsoluteUri)
        {
            rawPrmDocumentUri = resourceMetadataUriString;
        }
        else
        {
            // For relative URIs, combine with the base URL
            if (!Uri.TryCreate(baseUrl + resourceMetadataUriString, UriKind.Absolute, out var absoluteUri))
            {
                throw new InvalidOperationException("Could not create absolute URI for resource metadata.");
            }
            rawPrmDocumentUri = absoluteUri.ToString();
        }

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
