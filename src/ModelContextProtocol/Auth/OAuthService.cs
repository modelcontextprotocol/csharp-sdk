using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Auth.Types;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;

namespace ModelContextProtocol.Auth;

/// <summary>
/// Provides functionality for OAuth authentication in MCP clients.
/// </summary>
public partial class OAuthService
{
    private static readonly HttpClient _httpClient = new();
    private readonly Func<Uri, Task<string>>? _authorizationHandler;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthService"/> class.
    /// </summary>
    public OAuthService()
    {
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthService"/> class with an authorization handler.
    /// </summary>
    /// <param name="authorizationHandler">A handler to invoke when authorization is required.</param>
    public OAuthService(Func<Uri, Task<string>> authorizationHandler)
    {
        _authorizationHandler = authorizationHandler ?? throw new ArgumentNullException(nameof(authorizationHandler));
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
    
    /// <summary>
    /// Generates a cryptographically random code verifier for PKCE.
    /// </summary>
    /// <returns>A base64url encoded string to be used as the code verifier.</returns>
    public static string GenerateCodeVerifier()
    {
        // Generate a cryptographically random code verifier
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        
        // Use the optimized Base64UrlHelpers for encoding
        return Base64UrlHelpers.Encode(bytes);
    }
    
    /// <summary>
    /// Generates a code challenge from a code verifier using the S256 method.
    /// </summary>
    /// <param name="codeVerifier">The code verifier to generate the challenge from.</param>
    /// <returns>A base64url encoded SHA256 hash of the code verifier.</returns>
    public static string GenerateCodeChallenge(string codeVerifier)
    {
        // Create code challenge using S256 method
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        
        // Use the optimized Base64UrlHelpers for encoding
        return Base64UrlHelpers.Encode(challengeBytes);
    }
    
    /// <summary>
    /// Handles the OAuth authentication flow when a 401 Unauthorized response is received.
    /// </summary>
    /// <param name="resourceUri">The URI of the resource being accessed.</param>
    /// <param name="wwwAuthenticateHeader">The WWW-Authenticate header from the 401 response.</param>
    /// <param name="redirectUri">The URI to redirect to after authentication.</param>
    /// <param name="clientId">The client ID to use for authentication, or null to register a new client.</param>
    /// <param name="clientName">The client name to use for registration.</param>
    /// <param name="scopes">The requested scopes.</param>
    /// <param name="authorizationHandler">A handler to invoke when authorization is required. If not provided, the handler from the constructor will be used.</param>
    /// <returns>The OAuth token response.</returns>
    public async Task<OAuthToken> HandleAuthenticationAsync(
        Uri resourceUri,
        string wwwAuthenticateHeader,
        Uri redirectUri,
        string? clientId = null,
        string? clientName = null,
        IEnumerable<string>? scopes = null,
        Func<Uri, Task<string>>? authorizationHandler = null)
    {
        // Use the provided authorization handler or fall back to the one from the constructor
        var effectiveAuthHandler = authorizationHandler ?? _authorizationHandler;
        
        // Extract resource metadata URL from WWW-Authenticate header
        var resourceMetadataUri = ExtractResourceMetadataUri(wwwAuthenticateHeader);
        if (resourceMetadataUri == null)
        {
            throw new InvalidOperationException("Resource metadata URI not found in WWW-Authenticate header.");
        }

        // Get resource metadata
        var resourceMetadata = await GetResourceMetadataAsync(resourceMetadataUri);
        
        // Verify that the resource in the metadata matches the server's FQDN
        VerifyResourceUri(resourceUri, resourceMetadata.Resource);
        
        // Get the first authorization server
        if (resourceMetadata.AuthorizationServers.Count == 0)
        {
            throw new InvalidOperationException("No authorization servers found in resource metadata.");
        }
        
        var authServerUri = resourceMetadata.AuthorizationServers[0];
        
        // Get authorization server metadata
        var authServerMetadata = await DiscoverAuthorizationServerMetadataAsync(authServerUri);
        
        // Register client if needed
        string effectiveClientId;
        string? clientSecret = null;
        
        if (string.IsNullOrEmpty(clientId) && authServerMetadata.RegistrationEndpoint != null)
        {
            var registrationResponse = await RegisterClientAsync(
                authServerMetadata.RegistrationEndpoint, 
                redirectUri,
                clientName ?? "MCP Client",
                scopes);
            
            effectiveClientId = registrationResponse.ClientId;
            clientSecret = registrationResponse.ClientSecret;
        }
        else if (string.IsNullOrEmpty(clientId))
        {
            throw new InvalidOperationException("Client ID not provided and registration endpoint not available.");
        }
        else
        {
            // We know clientId is not null or empty at this point, but the compiler doesn't
            // so we need to use the null-forgiving operator
            effectiveClientId = clientId!;
        }
        
        // Perform authorization code flow with PKCE
        var tokenResponse = await PerformAuthorizationCodeFlowAsync(
            authServerMetadata,
            effectiveClientId, // This is now guaranteed to be non-null
            clientSecret,
            redirectUri,
            scopes?.ToList() ?? resourceMetadata.ScopesSupported,
            effectiveAuthHandler);
        
        return tokenResponse;
    }
    
    private Uri? ExtractResourceMetadataUri(string wwwAuthenticateHeader)
    {
        if (string.IsNullOrEmpty(wwwAuthenticateHeader))
        {
            return null;
        }
        
        // Parse the WWW-Authenticate header to extract the resource_metadata parameter
        if (wwwAuthenticateHeader.Contains("resource_metadata="))
        {
            var resourceMetadataStart = wwwAuthenticateHeader.IndexOf("resource_metadata=") + "resource_metadata=".Length;
            var resourceMetadataEnd = wwwAuthenticateHeader.IndexOf("\"", resourceMetadataStart + 1);
            if (resourceMetadataEnd > resourceMetadataStart)
            {
                var resourceMetadataUri = wwwAuthenticateHeader.Substring(resourceMetadataStart + 1, resourceMetadataEnd - resourceMetadataStart - 1);
                return new Uri(resourceMetadataUri);
            }
        }
        
        return null;
    }
    
    private async Task<ProtectedResourceMetadata> GetResourceMetadataAsync(Uri resourceMetadataUri)
    {
        var response = await _httpClient.GetAsync(resourceMetadataUri);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        var resourceMetadata = JsonSerializer.Deserialize(json, McpJsonUtilities.DefaultOptions.GetTypeInfo<ProtectedResourceMetadata>());
        if (resourceMetadata == null)
        {
            throw new InvalidOperationException("Failed to parse resource metadata.");
        }
        
        return resourceMetadata;
    }
    
    private void VerifyResourceUri(Uri resourceUri, Uri metadataResourceUri)
    {
        // Verify that the resource in the metadata matches the server's FQDN
        if (!(Uri.Compare(resourceUri, metadataResourceUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0))
        {
            throw new InvalidOperationException($"Resource URI in metadata ({metadataResourceUri}) does not match the server URI ({resourceUri}).");
        }
    }
    
    private async Task<AuthorizationServerMetadata> DiscoverAuthorizationServerMetadataAsync(Uri authServerUri)
    {
        // Ensure the authServerUri ends with a trailing slash
        var baseUri = authServerUri.AbsoluteUri.EndsWith("/")
            ? authServerUri
            : new Uri(authServerUri.AbsoluteUri + "/");

        // Now combine with the well-known endpoints
        var openIdConfigUri = new Uri(baseUri, ".well-known/openid-configuration");
        var oauthConfigUri = new Uri(baseUri, ".well-known/oauth-authorization-server");

        // Try OpenID Connect configuration endpoint first
        try
        {
            var response = await _httpClient.GetAsync(openIdConfigUri);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var metadata = JsonSerializer.Deserialize(json, McpJsonUtilities.DefaultOptions.GetTypeInfo<AuthorizationServerMetadata>());
                if (metadata != null)
                {
                    return metadata;
                }
            }
        }
        catch (Exception)
        {
            // Try next endpoint
        }
        
        // Try OAuth 2.0 authorization server metadata endpoint
        try
        {
            var response = await _httpClient.GetAsync(oauthConfigUri);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var metadata = JsonSerializer.Deserialize(json, McpJsonUtilities.DefaultOptions.GetTypeInfo<AuthorizationServerMetadata>());
                if (metadata != null)
                {
                    return metadata;
                }
            }
        }
        catch (Exception)
        {
            // No more endpoints to try
        }
        
        throw new InvalidOperationException("Could not discover authorization server metadata. Neither OpenID Connect nor OAuth 2.0 well-known endpoints returned valid metadata.");
    }
    
    private async Task<ClientRegistrationResponse> RegisterClientAsync(Uri registrationEndpoint, Uri redirectUri, string clientName, IEnumerable<string>? scopes)
    {
        var request = new ClientRegistrationRequest
        {
            RedirectUris = new List<string> { redirectUri.ToString() },
            ClientName = clientName,
            TokenEndpointAuthMethod = "client_secret_basic",
            GrantTypes = new List<string> { "authorization_code", "refresh_token" },
            ResponseTypes = new List<string> { "code" },
            Scope = scopes != null ? string.Join(" ", scopes) : null
        };
        
        var json = JsonSerializer.Serialize(request, McpJsonUtilities.DefaultOptions.GetTypeInfo<ClientRegistrationRequest>());
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync(registrationEndpoint, content);
        response.EnsureSuccessStatusCode();
        
        var responseJson = await response.Content.ReadAsStringAsync();
        var registrationResponse = JsonSerializer.Deserialize(responseJson, McpJsonUtilities.DefaultOptions.GetTypeInfo<ClientRegistrationResponse>());
        if (registrationResponse == null)
        {
            throw new InvalidOperationException("Failed to parse client registration response.");
        }
        
        return registrationResponse;
    }
    
    private async Task<OAuthToken> PerformAuthorizationCodeFlowAsync(
        AuthorizationServerMetadata authServerMetadata,
        string clientId,
        string? clientSecret,
        Uri redirectUri,
        IEnumerable<string> scopes,
        Func<Uri, Task<string>>? authorizationHandler)
    {
        // Generate PKCE values using our public method
        var pkceValues = GeneratePkceValues();
        
        // Build authorization URL
        var authorizationUrl = BuildAuthorizationUrl(
            authServerMetadata.AuthorizationEndpoint,
            clientId,
            redirectUri,
            pkceValues.CodeChallenge,
            scopes);
        
        // Check if an authorization handler is available
        if (authorizationHandler != null)
        {
            try
            {
                // Get the authorization code using the provided handler
                string authorizationCode = await authorizationHandler(new Uri(authorizationUrl));
                
                // Exchange the authorization code for a token
                return await ExchangeAuthorizationCodeForTokenAsync(
                    authServerMetadata.TokenEndpoint,
                    clientId,
                    clientSecret,
                    redirectUri,
                    authorizationCode,
                    pkceValues.CodeVerifier);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to complete OAuth authorization flow: {ex.Message}", ex);
            }
        }
        
        // No authorization handler available, throw with instructions
        throw new NotImplementedException(
            $"Authorization requires user interaction. Please direct the user to: {authorizationUrl}\n" +
            $"After authorization, the user will be redirected to: {redirectUri}?code=[authorization_code]\n" +
            $"You need to handle this redirect and extract the authorization code to complete the flow.");
    }
    
    private string BuildAuthorizationUrl(
        Uri authorizationEndpoint,
        string clientId,
        Uri redirectUri,
        string codeChallenge,
        IEnumerable<string> scopes)
    {
        var scopeString = string.Join(" ", scopes);
        
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri.ToString(),
            ["scope"] = scopeString,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = GenerateRandomString(16) // Used for CSRF protection
        };
        
        var queryString = string.Join("&", queryParams.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
        return $"{authorizationEndpoint}?{queryString}";
    }
    
    private string GenerateRandomString(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "")
            .Substring(0, length);
    }
    
    // This method would be used in a real implementation after receiving the authorization code
    private async Task<OAuthToken> ExchangeAuthorizationCodeForTokenAsync(
        Uri tokenEndpoint,
        string clientId,
        string? clientSecret,
        Uri redirectUri,
        string authorizationCode,
        string codeVerifier)
    {
        var tokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = redirectUri.ToString(),
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier
        };
        
        var requestContent = new FormUrlEncodedContent(tokenRequest);
        
        HttpResponseMessage response;
        if (!string.IsNullOrEmpty(clientSecret))
        {
            // Add client authentication if secret is available
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            response = await _httpClient.PostAsync(tokenEndpoint, requestContent);
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
        else
        {
            response = await _httpClient.PostAsync(tokenEndpoint, requestContent);
        }
        
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize(json, McpJsonUtilities.DefaultOptions.GetTypeInfo<OAuthToken>());
        if (tokenResponse == null)
        {
            throw new InvalidOperationException("Failed to parse token response.");
        }
        
        return tokenResponse;
    }
}