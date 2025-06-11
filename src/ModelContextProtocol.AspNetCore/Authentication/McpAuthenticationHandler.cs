using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;
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
    private string _resourceMetadataPath;

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

        // Note: this.Options is not fully available here.
        // _resourceMetadataPath will be correctly updated by GetAbsoluteResourceMetadataUri
        // or can be fetched from this.Options directly in HandleRequestAsync if needed.
        // For initial setup, if ResourceMetadataUri can be different per scheme,
        // this might need to be deferred or handled carefully.
        // However, GetAbsoluteResourceMetadataUri which is called by HandleChallengeAsync
        // will use this.Options and update _resourceMetadataPath.
        // And HandleResourceMetadataRequestAsync will also use this.Options.
        _resourceMetadataPath = options.CurrentValue.ResourceMetadataUri?.ToString() ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> HandleRequestAsync()
    {
        // Check if the request is for the resource metadata endpoint
        string requestPath = Request.Path.Value ?? string.Empty;
        
        string expectedMetadataPath = this.Options.ResourceMetadataUri?.ToString() ?? string.Empty;
        if (this.Options.ResourceMetadataUri != null && !this.Options.ResourceMetadataUri.IsAbsoluteUri)
        {
            // For relative URIs, it's just the path component.
            expectedMetadataPath = this.Options.ResourceMetadataUri.OriginalString;
        }

        // If the path doesn't match, let the request continue through the pipeline
        if (!string.Equals(requestPath, expectedMetadataPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cancellationToken = Request.HttpContext.RequestAborted;
        await HandleResourceMetadataRequestAsync(cancellationToken);
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
        var options = this.Options;
        var resourceMetadataUri = options.ResourceMetadataUri;
        
        // If the options have changed, update the cached path
        string currentPath = resourceMetadataUri?.ToString() ?? string.Empty;
        if (_resourceMetadataPath != currentPath && resourceMetadataUri != null)
        {
            _resourceMetadataPath = resourceMetadataUri.IsAbsoluteUri ? currentPath : resourceMetadataUri.OriginalString;
        }
        
        if (resourceMetadataUri != null && resourceMetadataUri.IsAbsoluteUri)
        {
            return currentPath;
        }
        
        // For relative URIs, combine with the base URL
        string baseUrl = GetBaseUrl();
        string relativePath = resourceMetadataUri?.OriginalString.TrimStart('/') ?? string.Empty;

        if (!Uri.TryCreate($"{baseUrl.TrimEnd('/')}/{relativePath}", UriKind.Absolute, out var absoluteUri))
        {
            throw new InvalidOperationException($"Could not create absolute URI for resource metadata. Base URL: {baseUrl}, Relative Path: {relativePath}");
        }
        
        return absoluteUri.ToString();
    }

    /// <summary>
    /// Handles the resource metadata request.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    private Task HandleResourceMetadataRequestAsync(CancellationToken cancellationToken = default)
    {
        var options = this.Options;
        var resourceMetadata = options.GetResourceMetadata(Request.HttpContext);
        
        // Create a copy to avoid modifying the original
        var metadata = new ProtectedResourceMetadata
        {
            Resource = resourceMetadata.Resource ?? new Uri(GetBaseUrl()),
            AuthorizationServers = [.. resourceMetadata.AuthorizationServers],
            BearerMethodsSupported = [.. resourceMetadata.BearerMethodsSupported],
            ScopesSupported = [.. resourceMetadata.ScopesSupported],
            ResourceDocumentation = resourceMetadata.ResourceDocumentation
        };
        
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/json";
        
        var json = JsonSerializer.Serialize(
            metadata, 
            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ProtectedResourceMetadata)));
        
        return Response.WriteAsync(json, cancellationToken);
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

        properties ??= new AuthenticationProperties();
        
        // Store the resource_metadata in properties in case other handlers need it
        properties.Items["resource_metadata"] = rawPrmDocumentUri;
        
        // Add the WWW-Authenticate header with Bearer scheme and resource metadata
        string headerValue = $"Bearer realm=\"{Scheme.Name}\", resource_metadata=\"{rawPrmDocumentUri}\"";
        Response.Headers.Append("WWW-Authenticate", headerValue);
        
        return base.HandleChallengeAsync(properties);
    }
}
