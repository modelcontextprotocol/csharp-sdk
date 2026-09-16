using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Authentication;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Represents the context for resource metadata request events.
/// </summary>
public class ResourceMetadataRequestContext : HandleRequestContext<McpAuthenticationOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceMetadataRequestContext"/> class.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="scheme">The authentication scheme.</param>
    /// <param name="options">The authentication options.</param>
    public ResourceMetadataRequestContext(
        HttpContext context,
        AuthenticationScheme scheme,
        McpAuthenticationOptions options)
        : base(context, scheme, options)
    {
    }

    /// <summary>
    /// Gets or sets the protected resource metadata for the current request.
    /// </summary>
    public ProtectedResourceMetadata? ResourceMetadata { get; set; }
}
