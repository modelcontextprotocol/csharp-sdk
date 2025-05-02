using System.Security.Cryptography;
using System.Text;

namespace ModelContextProtocol.Auth;

/// <summary>
/// Provides utilities for PKCE (Proof Key for Code Exchange) in OAuth authorization flows.
/// </summary>
public static class PkceUtility
{
    /// <summary>
    /// Represents the PKCE code challenge and verifier for an authorization flow.
    /// </summary>
    public class PkceValues
    {
        /// <summary>
        /// The code verifier used to generate the code challenge.
        /// </summary>
        public string CodeVerifier { get; }
        
        /// <summary>
        /// The code challenge sent to the authorization server.
        /// </summary>
        public string CodeChallenge { get; }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="PkceValues"/> class.
        /// </summary>
        public PkceValues(string codeVerifier, string codeChallenge)
        {
            CodeVerifier = codeVerifier;
            CodeChallenge = codeChallenge;
        }
    }
    
    /// <summary>
    /// Generates new PKCE values.
    /// </summary>
    /// <returns>A <see cref="PkceValues"/> instance containing the code verifier and challenge.</returns>
    public static PkceValues GeneratePkceValues()
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        return new PkceValues(codeVerifier, codeChallenge);
    }
    
    private static string GenerateCodeVerifier()
    {
        // Generate a cryptographically random code verifier
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        
        // Base64url encode the random bytes
        var base64 = Convert.ToBase64String(bytes);
        var base64Url = base64
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "");
        
        return base64Url;
    }
    
    private static string GenerateCodeChallenge(string codeVerifier)
    {
        // Create code challenge using S256 method
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        
        // Base64url encode the hash
        var base64 = Convert.ToBase64String(challengeBytes);
        var base64Url = base64
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "");
        
        return base64Url;
    }
}

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