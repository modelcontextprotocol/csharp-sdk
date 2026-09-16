namespace ModelContextProtocol.Authentication;

/// <summary>
/// Context provided to the <see cref="IdentityAssertionGrantIdTokenCallback"/> for a Cross-Application Access
/// authorization flow. Contains the URLs discovered during the OAuth flow needed for the token exchange step.
/// </summary>
public sealed class IdentityAssertionGrantContext
{
    /// <summary>
    /// Gets the MCP resource server URL (i.e., the <c>resource</c> parameter for token exchange).
    /// This is the URL of the MCP server being accessed.
    /// </summary>
    public required Uri ResourceUrl { get; init; }

    /// <summary>
    /// Gets the MCP authorization server URL (i.e., the <c>audience</c> parameter for token exchange).
    /// This is the URL of the authorization server protecting the MCP resource.
    /// </summary>
    public required Uri AuthorizationServerUrl { get; init; }
}
