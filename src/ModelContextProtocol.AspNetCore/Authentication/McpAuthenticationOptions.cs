using Microsoft.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Options for the MCP authentication handler.
/// </summary>
public class McpAuthenticationOptions : AuthenticationSchemeOptions
{
    private static readonly Uri DefaultResourceMetadataUri = new("/.well-known/oauth-protected-resource", UriKind.Relative);

    /// <summary>
    /// Initializes a new instance of the <see cref="McpAuthenticationOptions"/> class.
    /// </summary>
    public McpAuthenticationOptions()
    {
        // "Bearer" is JwtBearerDefaults.AuthenticationScheme, but we don't have a reference to the JwtBearer package here.
        ForwardAuthenticate = "Bearer";
        ResourceMetadataUri = DefaultResourceMetadataUri;
        Events = new McpAuthenticationEvents();
    }

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
    public Uri ResourceMetadataUri { get; set; }

    /// <summary>
    /// Gets or sets the protected resource metadata.
    /// </summary>
    /// <remarks>
    /// This contains the OAuth metadata for the protected resource, including authorization servers,
    /// supported scopes, and other information needed for clients to authenticate.
    /// </remarks>
    public ProtectedResourceMetadata? ResourceMetadata { get; set; }
}