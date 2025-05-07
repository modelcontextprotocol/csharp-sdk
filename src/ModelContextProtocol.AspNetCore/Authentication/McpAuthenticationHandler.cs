using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Utils.Json;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Authentication;

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
    /// Gets the base URL from the current request, including scheme, host, and path base.
    /// </summary>
    private string GetBaseUrl() => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

    /// <summary>
    /// Gets the absolute URI for the resource metadata endpoint.
    /// </summary>
    private string GetAbsoluteResourceMetadataUri()
    {
        var options = _optionsMonitor.CurrentValue;
        var resourceMetadataUri = options.ResourceMetadataUri;
        
        if (resourceMetadataUri.IsAbsoluteUri)
        {
            return resourceMetadataUri.ToString();
        }
        
        // For relative URIs, combine with the base URL
        string baseUrl = GetBaseUrl();
        string resourceMetadataPath = resourceMetadataUri.ToString();
        
        if (!Uri.TryCreate(baseUrl + resourceMetadataPath, UriKind.Absolute, out var absoluteUri))
        {
            throw new InvalidOperationException("Could not create absolute URI for resource metadata.");
        }
        
        return absoluteUri.ToString();
    }

    /// <summary>
    /// Handles the resource metadata request.
    /// </summary>
    private async Task HandleResourceMetadataRequestAsync()
    {
        // Get resource metadata from options, using the dynamic provider if available
        var options = _optionsMonitor.CurrentValue;
        var resourceMetadata = options.GetResourceMetadata(Request.HttpContext);
        
        // Create a copy to avoid modifying the original
        var metadata = new ProtectedResourceMetadata
        {
            AuthorizationServers = [.. resourceMetadata.AuthorizationServers],
            BearerMethodsSupported = [.. resourceMetadata.BearerMethodsSupported],
            ScopesSupported = [.. resourceMetadata.ScopesSupported],
            ResourceDocumentation = resourceMetadata.ResourceDocumentation,
            Resource = resourceMetadata.Resource
        };
        
        // Set default resource if not set
        metadata.Resource ??= new Uri(GetBaseUrl());
        
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/json";
        
        var json = JsonSerializer.Serialize(
            metadata, 
            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ProtectedResourceMetadata)));
        
        await Response.WriteAsync(json);
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // If ForwardAuthenticate is set, forward the authentication to the specified scheme
        if (!string.IsNullOrEmpty(Options.ForwardAuthenticate) && 
            Options.ForwardAuthenticate != Scheme.Name)
        {
            // Simply forward the authentication request to the specified scheme and return its result
            // This ensures we don't interfere with the authentication process
            return await Context.AuthenticateAsync(Options.ForwardAuthenticate);
        }

        // If no forwarding is configured, this handler doesn't perform authentication
        return AuthenticateResult.NoResult();
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Get the absolute URI for the resource metadata
        string rawPrmDocumentUri = GetAbsoluteResourceMetadataUri();

        // Initialize properties if null
        properties ??= new AuthenticationProperties();
        
        // Store the resource_metadata in properties in case other handlers need it
        properties.Items["resource_metadata"] = rawPrmDocumentUri;
        
        // Add the WWW-Authenticate header with Bearer scheme and resource metadata
        string headerValue = $"Bearer realm=\"{Scheme.Name}\", resource_metadata=\"{rawPrmDocumentUri}\"";
        Response.Headers.Append("WWW-Authenticate", headerValue);
        
        return base.HandleChallengeAsync(properties);
    }
}
