using Microsoft.AspNetCore.Authentication;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Options for the MCP authentication handler.
/// </summary>
public class McpAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The URI to the resource metadata document.
    /// </summary>
    public Uri? ResourceMetadataUri { get; set; }
}