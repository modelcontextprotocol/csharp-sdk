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
public class BasicOAuthAuthorizationProvider : ITokenProvider
{
    private readonly Uri _serverUrl;
    private readonly Uri _redirectUri;
    private readonly List<string> _scopes;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly HttpClient _httpClient;
    private readonly AuthorizationHelpers _authorizationHelpers;
    
    // Client name for IHttpClientFactory used by the BasicOAuthAuthorizationProvider
    public const string HttpClientName = "ProtectedMCPClient.OAuth";
    
    private TokenContainer? _token;
    private AuthorizationServerMetadata? _authServerMetadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasicOAuthAuthorizationProvider"/> class.
    /// </summary>
    /// <param name="serverUrl">The MCP server URL.</param>
    /// <param name="httpClientFactory">The HTTP client factory to use for creating HTTP clients.</param>
    /// <param name="authorizationHelpers">The authorization helpers.</param>
    /// <param name="clientId">OAuth client ID.</param>
    /// <param name="clientSecret">OAuth client secret.</param>
    /// <param name="redirectUri">OAuth redirect URI.</param>
    /// <param name="scopes">OAuth scopes.</param>
    public BasicOAuthAuthorizationProvider(
        Uri serverUrl,
        IHttpClientFactory httpClientFactory,
        AuthorizationHelpers authorizationHelpers,
        string clientId = "demo-client",
        string clientSecret = "",
        Uri? redirectUri = null,
        IEnumerable<string>? scopes = null)
    {
        _serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
        if (httpClientFactory == null) throw new ArgumentNullException(nameof(httpClientFactory));
        _authorizationHelpers = authorizationHelpers ?? throw new ArgumentNullException(nameof(authorizationHelpers));
        
        // Get the HttpClient once during construction instead of for each request
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        
        _redirectUri = redirectUri ?? new Uri("http://localhost:8080/callback");
        _scopes = scopes?.ToList() ?? [];
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    /// <inheritdoc />
    public IEnumerable<string> SupportedSchemes => new[] { "Bearer" };

    /// <inheritdoc />
    public Task<string?> GetCredentialAsync(string scheme, Uri resourceUri, CancellationToken cancellationToken = default)
    {
        // This provider only supports Bearer tokens
        if (scheme != "Bearer")
        {
            return Task.FromResult<string?>(null);
        }

        return GetBearerTokenAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? RecommendedScheme)> HandleUnauthorizedResponseAsync(
        HttpResponseMessage response, 
        string scheme,
        CancellationToken cancellationToken = default)
    {
        // This provider only supports Bearer scheme
        if (scheme != "Bearer")
        {
            return (false, null);
        }

        try
        {
            // Get the metadata from the challenge using the instance-based AuthorizationHelpers
            var resourceMetadata = await _authorizationHelpers.ExtractProtectedResourceMetadata(
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
                    var token = await InitiateAuthorizationCodeFlowAsync(authServerMetadata, cancellationToken);
                    if (token != null)
                    {
                        _token = token;
                        return (true, "Bearer");
                    }
                }
            }
            
            return (false, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling auth challenge: {ex.Message}");
            return (false, null);
        }
    }

    private async Task<string?> GetBearerTokenAsync(CancellationToken cancellationToken = default)
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
    
    private async Task<AuthorizationServerMetadata?> GetAuthServerMetadataAsync(Uri authServerUri, CancellationToken cancellationToken)
    {
        var baseUrl = authServerUri.ToString();
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        
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
            ["client_id"] = _clientId
        });
        
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint)
            {
                Content = requestContent
            };

            if (!string.IsNullOrEmpty(_clientSecret))
            {
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
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

    private async Task<TokenContainer?> InitiateAuthorizationCodeFlowAsync(
        AuthorizationServerMetadata authServerMetadata, 
        CancellationToken cancellationToken)
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        
        var authUrl = BuildAuthorizationUrl(authServerMetadata, codeChallenge);
        
        var authCode = await GetAuthorizationCodeAsync(authUrl, cancellationToken);
        if (string.IsNullOrEmpty(authCode)) return null;
        
        return await ExchangeCodeForTokenAsync(authServerMetadata, authCode, codeVerifier, cancellationToken);
    }
    
    private Uri BuildAuthorizationUrl(AuthorizationServerMetadata authServerMetadata, string codeChallenge)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["client_id"] = _clientId;
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
            
            OpenBrowser(authorizationUrl);
            
            var context = await listener.GetContextAsync();
            
            var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
            var code = query["code"];
            var error = query["error"];
            
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
            ["client_id"] = _clientId,
            ["code_verifier"] = codeVerifier
        });
        
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint)
            {
                Content = requestContent
            };
            
            if (!string.IsNullOrEmpty(_clientSecret))
            {
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
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