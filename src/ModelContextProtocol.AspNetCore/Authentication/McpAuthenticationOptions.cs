using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Authentication;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Options for the MCP authentication handler.
/// </summary>
public class McpAuthenticationOptions : AuthenticationSchemeOptions
{
    private static readonly Uri DefaultResourceMetadataUri = new("/.well-known/oauth-protected-resource", UriKind.Relative);

    private Func<HttpContext, ProtectedResourceMetadata>? _resourceMetadataProvider;

    private ProtectedResourceMetadata? _resourceMetadata;

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
    /// Gets or sets the static protected resource metadata.
    /// </summary>
    /// <remarks>
    /// This contains the OAuth metadata for the protected resource, including authorization servers,
    /// supported scopes, and other information needed for clients to authenticate.
    /// Setting this property will automatically update the <see cref="ProtectedResourceMetadataProvider"/>
    /// to return this static instance.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when trying to set a null value.</exception>
    /// <exception cref="ArgumentException">Thrown when the Resource property of the metadata is null.</exception>
    public ProtectedResourceMetadata ResourceMetadata
    {
        get => _resourceMetadata ?? throw new InvalidOperationException(
            "ResourceMetadata has not been configured.");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Resource == null)
            {
                throw new ArgumentException("The Resource property of the metadata cannot be null. A valid resource URI is required.", nameof(value));
            }

            _resourceMetadata = value;
            // When static metadata is set, update the provider to use it
            _resourceMetadataProvider = _ => _resourceMetadata;
        }
    }

    /// <summary>
    /// Gets or sets a delegate that dynamically provides resource metadata based on the HTTP context.
    /// </summary>
    /// <remarks>
    /// When set, this delegate will be called to generate resource metadata for each request,
    /// allowing dynamic customization based on the caller or other contextual information.
    /// This takes precedence over the static <see cref="ResourceMetadata"/> property.
    /// </remarks>
    public Func<HttpContext, ProtectedResourceMetadata>? ProtectedResourceMetadataProvider
    {
        get => _resourceMetadataProvider;
        set => _resourceMetadataProvider = value;
    }

    /// <summary>
    /// Gets the resource metadata for the current request.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>The resource metadata to use for the current request.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no resource metadata has been configured.</exception>
    internal ProtectedResourceMetadata GetResourceMetadata(HttpContext context)
    {
        var provider = _resourceMetadataProvider;

        return provider != null
            ? provider(context)
            : _resourceMetadata ?? throw new InvalidOperationException(
                "ResourceMetadata has not been configured.");
    }
}