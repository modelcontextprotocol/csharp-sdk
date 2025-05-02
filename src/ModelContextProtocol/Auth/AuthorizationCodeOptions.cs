namespace ModelContextProtocol.Auth;

/// <summary>
/// Configuration options for the authorization code flow.
/// </summary>
public class AuthorizationCodeOptions
{
    /// <summary>
    /// The client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
    
    /// <summary>
    /// The client secret.
    /// </summary>
    public string? ClientSecret { get; set; }
    
    /// <summary>
    /// The redirect URI.
    /// </summary>
    public Uri RedirectUri { get; set; } = null!;
    
    /// <summary>
    /// The authorization endpoint.
    /// </summary>
    public Uri AuthorizationEndpoint { get; set; } = null!;
    
    /// <summary>
    /// The token endpoint.
    /// </summary>
    public Uri TokenEndpoint { get; set; } = null!;
    
    /// <summary>
    /// The scope to request.
    /// </summary>
    public string? Scope { get; set; }
    
    /// <summary>
    /// PKCE values for the authorization flow.
    /// </summary>
    public PkceUtility.PkceValues PkceValues { get; set; } = null!;
    
    /// <summary>
    /// A state value to protect against CSRF attacks.
    /// </summary>
    public string State { get; set; } = string.Empty;
}