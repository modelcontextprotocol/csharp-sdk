using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// A generic implementation of an OAuth authorization provider for MCP. This does not do any advanced token
/// protection or caching - it acquires a token and server metadata and holds it in memory. 
/// This is suitable for demonstration and development purposes.
/// </summary>
public class GenericOAuthProvider : IMcpCredentialProvider
{
    /// <summary>
    /// The Bearer authentication scheme.
    /// </summary>
    private const string BearerScheme = "Bearer";
    private readonly Uri _serverUrl;
    private readonly Uri _redirectUri;
    private readonly List<string> _scopes;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly HttpClient _httpClient;
    private readonly AuthorizationHelpers _authorizationHelpers;
    private readonly ILogger _logger;
    
    // Lazy-initialized shared HttpClient for when no client is provided
    private static readonly Lazy<HttpClient> _defaultHttpClient = new(() => new HttpClient());

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    
    private TokenContainer? _token;
    private AuthorizationServerMetadata? _authServerMetadata;    /// <summary>
    /// Initializes a new instance of the <see cref="GenericOAuthProvider"/> class.
    /// </summary>
    /// <param name="serverUrl">The MCP server URL.</param>
    /// <param name="httpClient">The HTTP client to use for OAuth requests. If null, a default HttpClient will be used.</param>
    /// <param name="authorizationHelpers">The authorization helpers.</param>
    /// <param name="clientId">OAuth client ID.</param>
    /// <param name="clientSecret">OAuth client secret.</param>
    /// <param name="redirectUri">OAuth redirect URI.</param>
    /// <param name="scopes">OAuth scopes.</param>
    /// <param name="logger">The logger instance. If null, a NullLogger will be used.</param>
    public GenericOAuthProvider(
        Uri serverUrl,
        HttpClient? httpClient = null,
        AuthorizationHelpers? authorizationHelpers = null,
        string clientId = "demo-client",
        string clientSecret = "",
        Uri? redirectUri = null,
        IEnumerable<string>? scopes = null,
        ILogger<GenericOAuthProvider>? logger = null)
    {
        if (serverUrl == null) throw new ArgumentNullException(nameof(serverUrl));
          _serverUrl = serverUrl;
        _httpClient = httpClient ?? _defaultHttpClient.Value;
        _authorizationHelpers = authorizationHelpers ?? new AuthorizationHelpers(_httpClient);
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        
        _redirectUri = redirectUri ?? new Uri("http://localhost:8080/callback");
        _scopes = scopes?.ToList() ?? [];
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    /// <inheritdoc />
    public IEnumerable<string> SupportedSchemes => new[] { BearerScheme };

    /// <inheritdoc />
    public Task<string?> GetCredentialAsync(string scheme, Uri resourceUri, CancellationToken cancellationToken = default)
    {
        // This provider only supports Bearer tokens
        if (scheme != BearerScheme)
        {
            return Task.FromResult<string?>(null);
        }

        return GetBearerTokenAsync(cancellationToken);
    }    /// <inheritdoc />

    public async Task<McpUnauthorizedResponseResult> HandleUnauthorizedResponseAsync(
        HttpResponseMessage response, 
        string scheme,
        CancellationToken cancellationToken = default)
    {
        // This provider only supports Bearer scheme
        if (scheme != BearerScheme)
        {
            return new McpUnauthorizedResponseResult(false, null);
        }        
          try
        {
            // Get available authorization servers from the 401 response
            var availableAuthorizationServers = await _authorizationHelpers.GetAvailableAuthorizationServersAsync(
                response, 
                _serverUrl,
                cancellationToken);
            
            // Select the first available authorization server (or implement your own selection logic)
            var selectedAuthServer = availableAuthorizationServers.FirstOrDefault();
            
            if (selectedAuthServer != null)
            {
                // Get auth server metadata
                var authServerMetadata = await GetAuthServerMetadataAsync(selectedAuthServer, cancellationToken);
                
                if (authServerMetadata != null)
                {
                    // Store auth server metadata for future refresh operations
                    _authServerMetadata = authServerMetadata;
                    
                    // Do the OAuth flow
                    var token = await InitiateAuthorizationCodeFlowAsync(authServerMetadata, cancellationToken);
                    if (token != null)
                    {
                        _token = token;
                        return new McpUnauthorizedResponseResult(true, BearerScheme);
                    }
                }
            }
            
            return new McpUnauthorizedResponseResult(false, null);        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth challenge");
            return new McpUnauthorizedResponseResult(false, null);
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
                    var metadata = JsonSerializer.Deserialize<AuthorizationServerMetadata>(json, McpJsonUtilities.JsonContext.Default.AuthorizationServerMetadata);
                    
                    if (metadata != null)
                    {
                        metadata.ResponseTypesSupported ??= ["code"];
                        metadata.GrantTypesSupported ??= ["authorization_code", "refresh_token"];
                        metadata.TokenEndpointAuthMethodsSupported ??= ["client_secret_basic"];
                        metadata.CodeChallengeMethodsSupported ??= ["S256"];
                        
                        return metadata;
                    }
                }            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching auth server metadata from {Path}", path);
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
                var tokenResponse = JsonSerializer.Deserialize<TokenContainer>(json, McpJsonUtilities.JsonContext.Default.TokenContainer);
                
                if (tokenResponse != null)
                {
                    tokenResponse.ObtainedAt = DateTimeOffset.UtcNow;
                    if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                    {
                        tokenResponse.RefreshToken = refreshToken;
                    }
                    
                    return tokenResponse;
                }
            }        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
        }
        
        return null;
    }    private async Task<TokenContainer?> InitiateAuthorizationCodeFlowAsync(
        AuthorizationServerMetadata authServerMetadata, 
        CancellationToken cancellationToken)
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        
        var authUrl = BuildAuthorizationUrl(authServerMetadata, codeChallenge);
          var authCode = await GetAuthorizationCodeAsync(authUrl, cancellationToken);
        if (string.IsNullOrEmpty(authCode)) 
            return null;
        
        return await ExchangeCodeForTokenAsync(authServerMetadata, authCode!, codeVerifier, cancellationToken);
    }
    
    private Uri BuildAuthorizationUrl(AuthorizationServerMetadata authServerMetadata, string codeChallenge)
    {
        if (authServerMetadata.AuthorizationEndpoint.Scheme != Uri.UriSchemeHttp &&
            authServerMetadata.AuthorizationEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("AuthorizationEndpoint must use HTTP or HTTPS.", nameof(authServerMetadata));
        }

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

        var uriBuilder = new UriBuilder(authServerMetadata.AuthorizationEndpoint)
        {
            Query = queryParams.ToString()
        };
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
                _logger.LogError("Auth error: {Error}", error);
                return null;
            }
            
            if (string.IsNullOrEmpty(code))
            {
                _logger.LogError("No authorization code received");
                return null;
            }
            
            return code;        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting auth code");
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
                var tokenResponse = JsonSerializer.Deserialize<TokenContainer>(json, McpJsonUtilities.JsonContext.Default.TokenContainer);
                
                if (tokenResponse != null)
                {
                    tokenResponse.ObtainedAt = DateTimeOffset.UtcNow;
                    return tokenResponse;
                }            }
            else
            {
                _logger.LogError("Token exchange failed: {StatusCode}", response.StatusCode);
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Error: {Error}", error);
            }        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during token exchange");
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
            System.Diagnostics.Process.Start(psi);        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening browser");
            _logger.LogInformation("Please manually navigate to: {Url}", url);
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
