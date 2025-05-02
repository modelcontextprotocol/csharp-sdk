namespace ModelContextProtocol.Auth;

/// <summary>
/// Configuration for OAuth authorization.
/// </summary>
public class AuthorizationConfig
{
    /// <summary>
    /// The URI to redirect to after authentication.
    /// </summary>
    public Uri RedirectUri { get; set; } = null!;
    
    /// <summary>
    /// The client ID to use for authentication, or null to register a new client.
    /// </summary>
    public string? ClientId { get; set; }
    
    /// <summary>
    /// The client name to use for registration.
    /// </summary>
    public string? ClientName { get; set; }
    
    /// <summary>
    /// The requested scopes.
    /// </summary>
    public IEnumerable<string>? Scopes { get; set; }
    
    /// <summary>
    /// The handler to invoke when authorization is required.
    /// </summary>
    public Func<Uri, Task<string>>? AuthorizationHandler { get; set; }
}