using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Types.Authentication;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Options for the MCP authentication handler.
/// </summary>
public class McpAuthenticationOptions : AuthenticationSchemeOptions
{
    private static readonly Uri DefaultResourceMetadataUri = new("/.well-known/oauth-protected-resource", UriKind.Relative);

    /// <summary>
    /// Gets or sets the events used to handle authentication events.
    /// </summary>
    public new McpAuthenticationEvents Events
    {
        get { return (McpAuthenticationEvents)base.Events!; }
        set { base.Events = value; }
    }

    /// <summary>
    /// The URI to the resource metadata document.
    /// </summary>
    /// <remarks>
    /// This URI will be included in the WWW-Authenticate header when a 401 response is returned.
    /// </remarks>
    public Uri ResourceMetadataUri { get; set; } = DefaultResourceMetadataUri;

    /// <summary>
    /// Gets or sets the static protected resource metadata.
    /// </summary>
    /// <remarks>
    /// This contains the OAuth metadata for the protected resource, including authorization servers,
    /// supported scopes, and other information needed for clients to authenticate.
    /// This property is used when <see cref="ResourceMetadataProvider"/> is not set.
    /// </remarks>
    public ProtectedResourceMetadata ResourceMetadata { get; set; } = new ProtectedResourceMetadata();

    /// <summary>
    /// Gets or sets a delegate that dynamically provides resource metadata based on the HTTP context.
    /// </summary>
    /// <remarks>
    /// When set, this delegate will be called to generate resource metadata for each request,
    /// allowing dynamic customization based on the caller or other contextual information.
    /// This takes precedence over the static <see cref="ResourceMetadata"/> property.
    /// </remarks>
    public Func<HttpContext, ProtectedResourceMetadata>? ResourceMetadataProvider { get; set; }

    /// <summary>
    /// Gets the resource metadata for the current request.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>The resource metadata to use for the current request.</returns>
    internal ProtectedResourceMetadata GetResourceMetadata(HttpContext context)
    {
        if (ResourceMetadataProvider != null)
        {
            return ResourceMetadataProvider(context);
        }

        return ResourceMetadata;
    }
}