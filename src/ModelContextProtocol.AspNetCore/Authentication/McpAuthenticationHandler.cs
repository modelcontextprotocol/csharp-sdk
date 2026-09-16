using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Authentication;
using System.Text.Encodings.Web;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Represents an authentication handler for MCP protocol that adds resource metadata to challenge responses
/// and handles resource metadata endpoint requests.
/// </summary>
public partial class McpAuthenticationHandler : AuthenticationHandler<McpAuthenticationOptions>, IAuthenticationRequestHandler
{
    private const string DefaultResourceMetadataPath = "/.well-known/oauth-protected-resource";
    private static readonly PathString DefaultResourceMetadataPrefix = new(DefaultResourceMetadataPath);

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
    public Task<bool> HandleRequestAsync()
    {
        if (Options.ResourceMetadataUri is Uri configuredUri)
        {
            return HandleConfiguredResourceMetadataRequestAsync(configuredUri);
        }

        return HandleDefaultResourceMetadataRequestAsync();
    }

    private async Task<bool> HandleConfiguredResourceMetadataRequestAsync(Uri resourceMetadataUri)
    {
        if (!IsConfiguredEndpointRequest(resourceMetadataUri))
        {
            return false;
        }

        return await HandleResourceMetadataRequestAsync();
    }

    private async Task<bool> HandleDefaultResourceMetadataRequestAsync()
    {
        if (!Request.Path.StartsWithSegments(DefaultResourceMetadataPrefix, out var resourceSuffix))
        {
            return false;
        }

        // Build the derived resource string directly without trailing slash
        var scheme = Request.Scheme;
        var host = Request.Host.Host;
        var port = Request.Host.Port;
        var path = $"{Request.PathBase}{resourceSuffix}".TrimEnd('/');
        
        string derivedResource;
        if (port.HasValue && !IsDefaultPort(scheme, port.Value))
        {
            derivedResource = $"{scheme}://{host}:{port.Value}{path}";
        }
        else
        {
            derivedResource = $"{scheme}://{host}{path}";
        }

        return await HandleResourceMetadataRequestAsync(derivedResource);
    }

    private static bool IsDefaultPort(string scheme, int port)
    {
        return (scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && port == 80) || 
               (scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && port == 443);
    }

    /// <summary>
    /// Gets the absolute URI for the resource metadata endpoint.
    /// </summary>
    private string GetAbsoluteResourceMetadataUri()
    {
        if (Options.ResourceMetadataUri is Uri resourceMetadataUri)
        {
            if (resourceMetadataUri.IsAbsoluteUri)
            {
                return resourceMetadataUri.ToString();
            }

            var separator = resourceMetadataUri.OriginalString.StartsWith('/') ? "" : "/";
            return $"{Request.Scheme}://{Request.Host.ToUriComponent()}{Request.PathBase}{separator}{resourceMetadataUri.OriginalString}";
        }

        return $"{Request.Scheme}://{Request.Host.ToUriComponent()}{Request.PathBase}{DefaultResourceMetadataPath}{Request.Path}";
    }

    private bool IsConfiguredEndpointRequest(Uri resourceMetadataUri)
    {
        var expectedPath = GetConfiguredResourceMetadataPath(resourceMetadataUri);

        if (!string.Equals(Request.Path.Value, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!resourceMetadataUri.IsAbsoluteUri)
        {
            return true;
        }

        if (!string.Equals(Request.Host.Host, resourceMetadataUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            LogResourceMetadataHostMismatch(Logger, resourceMetadataUri.Host);
            return false;
        }

        if (!string.Equals(Request.Scheme, resourceMetadataUri.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            LogResourceMetadataSchemeMismatch(Logger, resourceMetadataUri.Scheme);
            return false;
        }

        return true;
    }

    private static string GetConfiguredResourceMetadataPath(Uri resourceMetadataUri)
    {
        if (resourceMetadataUri.IsAbsoluteUri)
        {
            return resourceMetadataUri.AbsolutePath;
        }

        var path = resourceMetadataUri.OriginalString;
        return path.StartsWith('/') ? path : $"/{path}";
    }

    private async Task<bool> HandleResourceMetadataRequestAsync(string? derivedResource = null)
    {
        var resourceMetadata = Options.ResourceMetadata?.Clone(derivedResource);

        if (Options.Events.OnResourceMetadataRequest is not null)
        {
            var context = new ResourceMetadataRequestContext(Request.HttpContext, Scheme, Options)
            {
                ResourceMetadata = resourceMetadata,
            };

            await Options.Events.OnResourceMetadataRequest(context);

            if (context.Result is not null)
            {
                if (context.Result.Handled)
                {
                    return true;
                }
                else if (context.Result.Skipped)
                {
                    return false;
                }
                else if (context.Result.Failure is not null)
                {
                    throw new AuthenticationFailureException("An error occurred from the OnResourceMetadataRequest event.", context.Result.Failure);
                }
            }

            resourceMetadata = context.ResourceMetadata;
        }

        if (resourceMetadata is null)
        {
            throw new InvalidOperationException("ResourceMetadata has not been configured. Please set McpAuthenticationOptions.ResourceMetadata or ensure context.ResourceMetadata is set inside McpAuthenticationOptions.Events.OnResourceMetadataRequest.");
        }

        resourceMetadata.Resource ??= derivedResource;

        if (resourceMetadata.Resource is null)
        {
            throw new InvalidOperationException("ResourceMetadata.Resource could not be determined. Please set McpAuthenticationOptions.ResourceMetadata.Resource or avoid setting a custom McpAuthenticationOptions.ResourceMetadataUri.");
        }

        await Results.Json(resourceMetadata, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ProtectedResourceMetadata))).ExecuteAsync(Context);
        return true;
    }

    /// <inheritdoc />
    // If no forwarding is configured, this handler doesn't perform authentication
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() => AuthenticateResult.NoResult();

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Get the absolute URI for the resource metadata
        string rawPrmDocumentUri = GetAbsoluteResourceMetadataUri();

        // Add the WWW-Authenticate header with Bearer scheme and resource metadata
        string headerValue = $"Bearer resource_metadata=\"{rawPrmDocumentUri}\"";
        Response.Headers.Append(HeaderNames.WWWAuthenticate, headerValue);
        return base.HandleChallengeAsync(properties);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resource metadata request host did not match configured host '{ConfiguredHost}'.")]
    private static partial void LogResourceMetadataHostMismatch(ILogger logger, string configuredHost);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resource metadata request scheme did not match configured scheme '{ConfiguredScheme}'.")]
    private static partial void LogResourceMetadataSchemeMismatch(ILogger logger, string configuredScheme);
}
