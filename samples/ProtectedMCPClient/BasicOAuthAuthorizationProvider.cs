using ModelContextProtocol.Authentication;
using ModelContextProtocol.Types.Authentication;
using ProtectedMCPClient.Types;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ProtectedMCPClient;

/// <summary>
/// A simple implementation of an OAuth authorization provider for MCP. This does not do any token
/// caching or any advanced token protection - it acquires a token and server metadata and holds it
/// in memory as-is. This is NOT PRODUCTION READY and MUST NOT BE USED IN PRODUCTION.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BasicOAuthAuthorizationProvider"/> class.
/// </remarks>
public class BasicOAuthAuthorizationProvider(
    Uri serverUrl,
    string clientId = "demo-client",
    string clientSecret = "",
    Uri? redirectUri = null,
    IEnumerable<string>? scopes = null) : IMcpAuthorizationProvider
{
    private readonly Uri _serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
    private readonly Uri _redirectUri = redirectUri ?? new Uri("http://localhost:8080/callback");
    private readonly List<string> _scopes = scopes?.ToList() ?? new List<string>();
    private readonly HttpClient _httpClient = new HttpClient();
    
    // Single token storage
    private TokenContainer? _token;

    // Store auth server metadata separately so token only stores token data
    private AuthorizationServerMetadata? _authServerMetadata;

    public string AuthorizationScheme => "Bearer";

    /// <inheritdoc />
    public async Task<string?> GetCredentialAsync(Uri resourceUri, CancellationToken cancellationToken = default)
    {
        // Return the token if it's valid
        if (_token != null && _token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _token.AccessToken;
        }
        
        // Try to refresh the token if we have a refresh token
        if (_token?.RefreshToken != null && _authServerMetadata != null)
        {
            var newToken = await RefreshTokenAsync(_token.RefreshToken, _authServerMetadata, cancellationToken);
            if (newToken != null)
            {
                _token = newToken;
                return _token.AccessToken;
            }
        }
        
        // No valid token - auth handler will trigger the 401 flow
        return null;
    }

    /// <inheritdoc />
    public async Task<bool> HandleUnauthorizedResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the metadata from the challenge
            var resourceMetadata = await AuthorizationHelpers.ExtractProtectedResourceMetadata(
                response, _serverUrl, cancellationToken);
            
            if (resourceMetadata?.AuthorizationServers?.Count > 0)
            {
                // Get auth server metadata
                var authServerMetadata = await GetAuthServerMetadataAsync(
                    resourceMetadata.AuthorizationServers[0], cancellationToken);
                
                if (authServerMetadata != null)
                {
                    // Store auth server metadata for future refresh operations
                    _authServerMetadata = authServerMetadata;
                    
                    // Do the OAuth flow
                    var token = await DoAuthorizationCodeFlowAsync(authServerMetadata, cancellationToken);
                    if (token != null)
                    {
                        _token = token;
                        return true;
                    }
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling auth challenge: {ex.Message}");
            return false;
        }
    }

    private async Task<AuthorizationServerMetadata?> GetAuthServerMetadataAsync(Uri authServerUri, CancellationToken cancellationToken)
    {
        // Ensure trailing slash
        var baseUrl = authServerUri.ToString();
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        
        // Try both well-known endpoints
        foreach (var path in new[] { ".well-known/openid-configuration", ".well-known/oauth-authorization-server" })
        {
            try
            {
                var response = await _httpClient.GetAsync(new Uri(baseUrl + path), cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var metadata = JsonSerializer.Deserialize<AuthorizationServerMetadata>(
                        json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (metadata != null) return metadata;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching auth server metadata from {path}: {ex.Message}");
            }
        }
        
        return null;
    }

    private async Task<TokenContainer?> RefreshTokenAsync(string refreshToken, AuthorizationServerMetadata authServerMetadata, CancellationToken cancellationToken)
    {
        var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId
        });
        
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint)
            {
                Content = requestContent
            };
            
            // Add client auth if we have a secret
            if (!string.IsNullOrEmpty(clientSecret))
            {
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            }
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var tokenResponse = JsonSerializer.Deserialize<TokenContainer>(
                    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (tokenResponse != null)
                {
                    // Set obtained time and preserve refresh token if needed
                    tokenResponse.ObtainedAt = DateTimeOffset.UtcNow;
                    if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                    {
                        tokenResponse.RefreshToken = refreshToken;
                    }
                    
                    return tokenResponse;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error refreshing token: {ex.Message}");
        }
        
        return null;
    }

    private async Task<TokenContainer?> DoAuthorizationCodeFlowAsync(
        AuthorizationServerMetadata authServerMetadata, 
        CancellationToken cancellationToken)
    {
        // Generate PKCE values
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        
        // Build the auth URL
        var authUrl = BuildAuthorizationUrl(authServerMetadata, codeChallenge);
        
        // Get auth code
        var authCode = await GetAuthorizationCodeAsync(authUrl, cancellationToken);
        if (string.IsNullOrEmpty(authCode)) return null;
        
        // Exchange for token
        return await ExchangeCodeForTokenAsync(authServerMetadata, authCode, codeVerifier, cancellationToken);
    }
    
    private Uri BuildAuthorizationUrl(AuthorizationServerMetadata authServerMetadata, string codeChallenge)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["client_id"] = clientId;
        queryParams["redirect_uri"] = _redirectUri.ToString();
        queryParams["response_type"] = "code";
        queryParams["code_challenge"] = codeChallenge;
        queryParams["code_challenge_method"] = "S256";
        
        if (_scopes.Any())
        {
            queryParams["scope"] = string.Join(" ", _scopes);
        }
        
        var uriBuilder = new UriBuilder(authServerMetadata.AuthorizationEndpoint);
        uriBuilder.Query = queryParams.ToString();
        return uriBuilder.Uri;
    }
    
    private async Task<string?> GetAuthorizationCodeAsync(Uri authorizationUrl, CancellationToken cancellationToken)
    {
        var listenerPrefix = _redirectUri.GetLeftPart(UriPartial.Authority);
        if (!listenerPrefix.EndsWith("/")) listenerPrefix += "/";
        
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        
        try
        {
            listener.Start();
            
            // Open browser to the authorization URL
            OpenBrowser(authorizationUrl);
            
            // Get the authorization code
            var context = await listener.GetContextAsync();
            
            // Parse the response
            var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
            var code = query["code"];
            var error = query["error"];
            
            // Send a response to the browser
            string responseHtml = "<html><body><h1>Authentication complete</h1><p>You can close this window now.</p></body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = "text/html";
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
            
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"Auth error: {error}");
                return null;
            }
            
            return code;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting auth code: {ex.Message}");
            return null;
        }
        finally
        {
            if (listener.IsListening) listener.Stop();
        }
    }
    
    private async Task<TokenContainer?> ExchangeCodeForTokenAsync(
        AuthorizationServerMetadata authServerMetadata,
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = _redirectUri.ToString(),
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier
        });
        
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint)
            {
                Content = requestContent
            };
            
            // Add client auth if we have a secret
            if (!string.IsNullOrEmpty(clientSecret))
            {
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            }
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var tokenResponse = JsonSerializer.Deserialize<TokenContainer>(
                    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (tokenResponse != null)
                {
                    // Set the time when the token was obtained
                    tokenResponse.ObtainedAt = DateTimeOffset.UtcNow;
                    return tokenResponse;
                }
            }
            else
            {
                Console.WriteLine($"Token exchange failed: {response.StatusCode}");
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Error: {error}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception during token exchange: {ex.Message}");
        }
        
        return null;
    }
    
    private void OpenBrowser(Uri url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url.ToString(),
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error opening browser: {ex.Message}");
            Console.WriteLine($"Please manually navigate to: {url}");
        }
    }
    
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