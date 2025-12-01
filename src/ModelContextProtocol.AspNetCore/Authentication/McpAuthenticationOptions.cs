using Microsoft.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Represents options for the MCP authentication handler.
/// </summary>
public class McpAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpAuthenticationOptions"/> class.
    /// </summary>
    public McpAuthenticationOptions()
    {
        // "Bearer" is JwtBearerDefaults.AuthenticationScheme, but we don't have a reference to the JwtBearer package here.
        ForwardAuthenticate = "Bearer";
        Events = new McpAuthenticationEvents();
    }

    /// <summary>
    /// Gets or sets the events used to handle authentication events.
    /// </summary>
    public new McpAuthenticationEvents Events
    {
        get => (McpAuthenticationEvents)base.Events!;
        set => base.Events = value;
    }

    /// <summary>
    /// Gets or sets the URI to the resource metadata document.
    /// </summary>
    /// <remarks>
    /// This URI is included in the WWW-Authenticate header when a 401 response is returned.
    /// When <see langword="null"/>, the handler automatically uses the default
    /// <c>/.well-known/oauth-protected-resource/&lt;resource-path&gt;</c> endpoint that mirrors the requested resource path.
    /// </remarks>
    public Uri? ResourceMetadataUri { get; set; }

    /// <summary>
    /// Gets or sets the protected resource metadata.
    /// </summary>
    /// <remarks>
    /// This contains the OAuth metadata for the protected resource, including authorization servers,
    /// supported scopes, and other information needed for clients to authenticate.
    /// </remarks>
    public ProtectedResourceMetadata? ResourceMetadata { get; set; }
}
