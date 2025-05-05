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
    /// This URI will be included in the WWW-Authenticate header when a 401 response is returned
    /// and Bearer authentication is supported.
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
    /// Gets or sets the authentication schemes supported by this server.
    /// </summary>
    /// <remarks>
    /// When set, these schemes will be included in WWW-Authenticate headers during an authentication challenge.
    /// By default, this is empty and must be populated with the authentication schemes your server supports.
    /// If Bearer is included, the resource metadata URI will be included in its parameters.
    /// </remarks>
    public List<string> SupportedAuthenticationSchemes { get; set; } = new List<string>();
    
    /// <summary>
    /// Gets or sets a delegate that dynamically provides authentication schemes based on the HTTP context.
    /// </summary>
    /// <remarks>
    /// When set, this delegate will be called to determine which authentication schemes to include
    /// in WWW-Authenticate headers during an authentication challenge. This takes precedence over the static
    /// <see cref="SupportedAuthenticationSchemes"/> property.
    /// </remarks>
    public Func<HttpContext, IEnumerable<string>>? SupportedAuthenticationSchemesProvider { get; set; }

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
    
    /// <summary>
    /// Gets the supported authentication schemes for the current request.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>The authentication schemes supported for the current request.</returns>
    internal IEnumerable<string> GetSupportedAuthenticationSchemes(HttpContext context)
    {
        if (SupportedAuthenticationSchemesProvider != null)
        {
            return SupportedAuthenticationSchemesProvider(context);
        }

        return SupportedAuthenticationSchemes;
    }
}