using ModelContextProtocol.Authentication;
using ModelContextProtocol.Types.Authentication;
using ProtectedMCPClient.Types;
using ProtectedMCPClient.Utils;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ProtectedMCPClient;

/// <summary>
/// A simple implementation of IAccessTokenProvider that uses a fixed API key.
/// This is just for demonstration purposes.
/// </summary>
public partial class SimpleAccessTokenProvider : IMcpAuthorizationProvider
{
    private readonly ConcurrentDictionary<string, TokenContainer> _tokenCache = new();
    private readonly Uri _serverUrl;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly Uri _redirectUri;

    public SimpleAccessTokenProvider(Uri serverUrl, string clientId = "demo-client", string clientSecret = "", Uri? redirectUri = null)
    {
        _serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
        _clientId = clientId;
        _clientSecret = clientSecret;
        _redirectUri = redirectUri ?? new Uri("http://localhost:8080/callback");
    }

    /// <inheritdoc />
    public async Task<string?> GetCredentialAsync(Uri resourceUri, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Getting authentication token for {resourceUri}");
        
        // Check if we have a valid cached token
        string resourceKey = resourceUri.ToString();
        if (_tokenCache.TryGetValue(resourceKey, out var tokenInfo))
        {
            Console.WriteLine("Using cached token");
            return tokenInfo.AccessToken;
        }

        return string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> HandleUnauthorizedResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use AuthenticationUtils to handle the 401 challenge
            var resourceMetadata = await AuthorizationHelpers.ExtractProtectedResourceMetadata(
                response,
                _serverUrl,
                cancellationToken);
            
            // If we get here, the resource metadata is valid and matches our server
            Console.WriteLine($"Successfully validated resource metadata for: {resourceMetadata.Resource}");
            
            // Follow the authorization flow as described in the specs
            if (resourceMetadata.AuthorizationServers?.Count > 0) 
            {
                // Get the first authorization server
                var authServerUrl = resourceMetadata.AuthorizationServers[0];
                Console.WriteLine($"Using authorization server: {authServerUrl}");
                
                // Fetch authorization server metadata
                var authServerMetadata = await AuthorizationServerUtils.FetchAuthorizationServerMetadataAsync(
                    authServerUrl, cancellationToken);
                
                if (authServerMetadata != null)
                {
                    // Perform the OAuth authorization code flow with PKCE
                    var token = await PerformAuthorizationCodeFlowAsync(authServerMetadata, resourceMetadata, cancellationToken);
                    
                    if (token != null)
                    {
                        // Store the token in the cache
                        string resourceKey = resourceMetadata.Resource.ToString();
                        _tokenCache[resourceKey] = token;
                        Console.WriteLine("Successfully obtained a new token");
                        return true;
                    }
                }
                else
                {
                    Console.WriteLine("Failed to fetch authorization server metadata");
                }
            }
            
            Console.WriteLine("API key is valid, but might not have sufficient permissions.");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            // Log the specific error about why the challenge handling failed
            Console.WriteLine($"Authentication challenge failed: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            // Log any unexpected errors
            Console.WriteLine($"Unexpected error during authentication challenge: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Performs the OAuth authorization code flow with PKCE.
    /// </summary>
    /// <param name="authServerMetadata">The authorization server metadata.</param>
    /// <param name="resourceMetadata">The protected resource metadata.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The token information if successful, otherwise null.</returns>
    private async Task<TokenContainer?> PerformAuthorizationCodeFlowAsync(
        AuthorizationServerMetadata authServerMetadata, 
        ProtectedResourceMetadata resourceMetadata,
        CancellationToken cancellationToken)
    {
        // Generate PKCE code challenge
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        
        // In a real client, you would redirect the user to the authorization endpoint
        // For this sample, we'll simulate the authorization code grant
        Console.WriteLine("In a real app, the user would be redirected to the authorization URL:");
        
        // Build the authorization URL
        var authorizationUrl = BuildAuthorizationUrl(authServerMetadata, codeChallenge, resourceMetadata);
        Console.WriteLine($"Authorization URL: {authorizationUrl}");
        
        // In a real app, you would wait for the redirect with the authorization code
        // For this sample, we'll simulate it
        Console.WriteLine("Simulating authorization code grant (in a real app, user would interact with the auth server)");
        
        // Simulate getting an authorization code (this would come from the redirect in a real app)
        // NOTE: This is just for demonstration. In a real client, you'd parse the authorization code from the redirect
        string simulatedAuthCode = "simulated_auth_code_would_come_from_redirect";
        
        // Exchange the authorization code for tokens
        return await ExchangeCodeForTokenAsync(authServerMetadata, simulatedAuthCode, codeVerifier, cancellationToken);
    }

    /// <summary>
    /// Builds the authorization URL for the authorization code flow.
    /// </summary>
    private Uri BuildAuthorizationUrl(
        AuthorizationServerMetadata authServerMetadata, 
        string codeChallenge,
        ProtectedResourceMetadata resourceMetadata)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["client_id"] = _clientId;
        queryParams["redirect_uri"] = _redirectUri.ToString();
        queryParams["response_type"] = "code";
        queryParams["code_challenge"] = codeChallenge;
        queryParams["code_challenge_method"] = "S256";
        
        // Add scopes if available from resource metadata
        if (resourceMetadata.ScopesSupported.Count > 0)
        {
            queryParams["scope"] = string.Join(" ", resourceMetadata.ScopesSupported);
        }
        
        // Create the authorization URL
        var uriBuilder = new UriBuilder(authServerMetadata.AuthorizationEndpoint);
        uriBuilder.Query = queryParams.ToString();
        
        return uriBuilder.Uri;
    }

    /// <summary>
    /// Exchanges an authorization code for an access token.
    /// </summary>
    private async Task<TokenContainer?> ExchangeCodeForTokenAsync(
        AuthorizationServerMetadata authServerMetadata,
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        
        // Set up the request to the token endpoint
        var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = _redirectUri.ToString(),
            ["client_id"] = _clientId,
            ["code_verifier"] = codeVerifier
        });
        
        // Add client authentication if we have a client secret
        if (!string.IsNullOrEmpty(_clientSecret))
        {
            var authHeader = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
            httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Basic", authHeader);
        }
        
        try
        {
            // Make the token request
            var response = await httpClient.PostAsync(
                authServerMetadata.TokenEndpoint, 
                requestContent, 
                cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                // Parse the token response
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var tokenResponse = JsonSerializer.Deserialize<TokenContainer>(
                    responseJson, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (tokenResponse != null)
                {
                    // There was a valid token response
                }
            }
            else
            {
                Console.WriteLine($"Token request failed: {response.StatusCode}");
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Error: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception during token exchange: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>
    /// Generates a random code verifier for PKCE.
    /// </summary>
    private string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Generates a code challenge from a code verifier using SHA256.
    /// </summary>
    private string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return Convert.ToBase64String(challengeBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}