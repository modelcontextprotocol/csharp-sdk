using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;
using System.Text.Encodings.Web;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Authentication handler for MCP protocol that adds resource metadata to challenge responses
/// and handles resource metadata endpoint requests.
/// </summary>
public class McpAuthenticationHandler : AuthenticationHandler<McpAuthenticationOptions>, IAuthenticationRequestHandler
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
    public async Task<bool> HandleRequestAsync()
    {
        // Check if the request is for the resource metadata endpoint
        string requestPath = Request.Path.Value ?? string.Empty;

        string expectedMetadataPath = Options.ResourceMetadataUri?.ToString() ?? string.Empty;
        if (Options.ResourceMetadataUri != null && !Options.ResourceMetadataUri.IsAbsoluteUri)
        {
            // For relative URIs, it's just the path component.
            expectedMetadataPath = Options.ResourceMetadataUri.OriginalString;
        }

        // If the path doesn't match, let the request continue through the pipeline
        if (!string.Equals(requestPath, expectedMetadataPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

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
        var resourceMetadataUri = Options.ResourceMetadataUri;

        string currentPath = resourceMetadataUri?.ToString() ?? string.Empty;

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
    private async Task HandleResourceMetadataRequestAsync()
    {
        var resourceMetadata = Options.ResourceMetadata;

        if (Options.Events.OnResourceMetadataRequest is not null)
        {
            var context = new ResourceMetadataRequestContext(Request.HttpContext, Scheme, Options)
            {
                ResourceMetadata = CloneResourceMetadata(resourceMetadata),
            };

            await Options.Events.OnResourceMetadataRequest(context);
            resourceMetadata = context.ResourceMetadata;
        }

        if (resourceMetadata == null)
        {
            throw new InvalidOperationException(
                "ResourceMetadata has not been configured. Please set McpAuthenticationOptions.ResourceMetadata or ensure context.ResourceMetadata is set inside McpAuthenticationOptions.Events.OnResourceMetadataRequest."
            );
        }

        await Results.Json(resourceMetadata, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ProtectedResourceMetadata))).ExecuteAsync(Context);
    }

    /// <inheritdoc />
    // If no forwarding is configured, this handler doesn't perform authentication
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() => AuthenticateResult.NoResult();

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

    internal static ProtectedResourceMetadata? CloneResourceMetadata(ProtectedResourceMetadata? resourceMetadata)
    {
        if (resourceMetadata is null)
        {
            return null;
        }

        return new ProtectedResourceMetadata
        {
            Resource = resourceMetadata.Resource,
            AuthorizationServers = [.. resourceMetadata.AuthorizationServers],
            BearerMethodsSupported = [.. resourceMetadata.BearerMethodsSupported],
            ScopesSupported = [.. resourceMetadata.ScopesSupported],
            JwksUri = resourceMetadata.JwksUri,
            ResourceSigningAlgValuesSupported = resourceMetadata.ResourceSigningAlgValuesSupported is not null ? [.. resourceMetadata.ResourceSigningAlgValuesSupported] : null,
            ResourceName = resourceMetadata.ResourceName,
            ResourceDocumentation = resourceMetadata.ResourceDocumentation,
            ResourcePolicyUri = resourceMetadata.ResourcePolicyUri,
            ResourceTosUri = resourceMetadata.ResourceTosUri,
            TlsClientCertificateBoundAccessTokens = resourceMetadata.TlsClientCertificateBoundAccessTokens,
            AuthorizationDetailsTypesSupported = resourceMetadata.AuthorizationDetailsTypesSupported is not null ? [.. resourceMetadata.AuthorizationDetailsTypesSupported] : null,
            DpopSigningAlgValuesSupported = resourceMetadata.DpopSigningAlgValuesSupported is not null ? [.. resourceMetadata.DpopSigningAlgValuesSupported] : null,
            DpopBoundAccessTokensRequired = resourceMetadata.DpopBoundAccessTokensRequired
        };
    }

}
