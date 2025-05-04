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
/// A simple implementation of an OAuth authorization provider for MCP.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BasicOAuthAuthorizationProvider"/> class.
/// </remarks>
/// <param name="serverUrl">The server URL.</param>
/// <param name="clientId">The OAuth client ID.</param>
/// <param name="clientSecret">The OAuth client secret.</param>
/// <param name="redirectUri">The OAuth redirect URI.</param>
/// <param name="scopes">The OAuth scopes required by the application.</param>
public partial class BasicOAuthAuthorizationProvider(
    Uri serverUrl,
    string clientId = "demo-client",
    string clientSecret = "",
    Uri? redirectUri = null,
    IEnumerable<string>? scopes = null) : IMcpAuthorizationProvider
{
    private readonly ConcurrentDictionary<string, TokenContainer> _tokenCache = new();
    private readonly Uri _serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
    private readonly Uri _redirectUri = redirectUri ?? new Uri("http://localhost:8080/callback");
    private readonly IEnumerable<string> _scopes = scopes ?? Array.Empty<string>();

    /// <inheritdoc />
    public async Task<string?> GetCredentialAsync(Uri resourceUri, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Getting authentication token for {resourceUri}");
        
        // Check if we have a valid cached token
        string resourceKey = resourceUri.ToString();
        if (_tokenCache.TryGetValue(resourceKey, out var tokenInfo))
        {
            // Check if the token is still valid or needs to be refreshed
            if (tokenInfo.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5)) // 5-minute buffer
            {
                Console.WriteLine("Using cached token");
                return tokenInfo.AccessToken;
            }
            else if (!string.IsNullOrEmpty(tokenInfo.RefreshToken))
            {
                Console.WriteLine("Token expired, attempting to refresh");
                
                // Get the authorization server metadata for the resource
                var resourceMetadata = await GetResourceMetadataAsync(resourceUri, cancellationToken);
                if (resourceMetadata?.AuthorizationServers?.Count > 0)
                {
                    var authServerUrl = resourceMetadata.AuthorizationServers[0];
                    var authServerMetadata = await AuthorizationServerUtils.FetchAuthorizationServerMetadataAsync(
                        authServerUrl, cancellationToken);
                        
                    if (authServerMetadata != null)
                    {
                        // Refresh the token
                        var refreshedToken = await RefreshTokenAsync(
                            authServerMetadata, 
                            tokenInfo.RefreshToken, 
                            cancellationToken);
                            
                        if (refreshedToken != null)
                        {
                            _tokenCache[resourceKey] = refreshedToken;
                            Console.WriteLine("Token refreshed successfully");
                            return refreshedToken.AccessToken;
                        }
                        else
                        {
                            Console.WriteLine("Token refresh failed, will need to re-authenticate");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("Token expired and no refresh token available");
            }
            
            // Remove expired token from cache
            _tokenCache.TryRemove(resourceKey, out _);
        }

        // We don't have a valid token and need to get a new one
        Console.WriteLine("No valid token available");
        return null;
    }
    
    /// <summary>
    /// Refreshes an OAuth token using the refresh token.
    /// </summary>
    /// <param name="authServerMetadata">The authorization server metadata.</param>
    /// <param name="refreshToken">The refresh token to use.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The new token information if successful, otherwise null.</returns>
    private async Task<TokenContainer?> RefreshTokenAsync(
        AuthorizationServerMetadata authServerMetadata,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        
        // Set up the request to the token endpoint
        var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId
        });
        
        // Add client authentication if we have a client secret
        if (!string.IsNullOrEmpty(clientSecret))
        {
            var authHeader = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Basic", authHeader);
        }
        
        try
        {
            // Make the token refresh request
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
                    // Set the time when the token was obtained
                    tokenResponse.ObtainedAt = DateTimeOffset.UtcNow;
                    
                    // Calculate expiration time if not set
                    if (tokenResponse.ExpiresIn > 0 && tokenResponse.ExpiresAt == default)
                    {
                        tokenResponse.ExpiresAt = tokenResponse.ObtainedAt.AddSeconds(tokenResponse.ExpiresIn);
                    }
                    
                    // Preserve the refresh token if the response doesn't include a new one
                    if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                    {
                        tokenResponse.RefreshToken = refreshToken;
                    }
                    
                    return tokenResponse;
                }
            }
            else
            {
                Console.WriteLine($"Token refresh failed: {response.StatusCode}");
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Error: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception during token refresh: {ex.Message}");
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the metadata for a protected resource.
    /// </summary>
    /// <param name="resourceUri">The URI of the protected resource.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The protected resource metadata.</returns>
    private async Task<ProtectedResourceMetadata?> GetResourceMetadataAsync(Uri resourceUri, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient();
            
            // Make a HEAD request to the resource to get the WWW-Authenticate header
            var request = new HttpRequestMessage(HttpMethod.Head, resourceUri);
            var response = await httpClient.SendAsync(request, cancellationToken);
            
            // Handle 401 Unauthorized response, which should contain the challenge
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return await AuthorizationHelpers.ExtractProtectedResourceMetadata(
                    response,
                    _serverUrl,
                    cancellationToken);
            }
            else
            {
                Console.WriteLine($"Resource request did not return expected 401 status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting resource metadata: {ex.Message}");
        }
        
        return null;
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
        
        // Build the authorization URL
        var authorizationUrl = BuildAuthorizationUrl(authServerMetadata, codeChallenge, resourceMetadata);
        Console.WriteLine($"Authorization URL: {authorizationUrl}");
        
        // Start a local HTTP listener to receive the authorization code callback
        var authorizationCode = await StartLocalAuthorizationServerAsync(authorizationUrl, _redirectUri, cancellationToken);
        
        if (string.IsNullOrEmpty(authorizationCode))
        {
            Console.WriteLine("Failed to get authorization code from server");
            return null;
        }
        
        Console.WriteLine($"Received authorization code: {authorizationCode[..Math.Min(6, authorizationCode.Length)]}...");
        
        // Exchange the authorization code for tokens
        return await ExchangeCodeForTokenAsync(authServerMetadata, authorizationCode, codeVerifier, cancellationToken);
    }
    
    /// <summary>
    /// Starts a local HTTP server to receive the authorization code from the OAuth redirect.
    /// </summary>
    /// <param name="authorizationUrl">The authorization URL to redirect the user to.</param>
    /// <param name="redirectUri">The redirect URI where the authorization code will be sent.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The authorization code if successful, otherwise null.</returns>
    private async Task<string?> StartLocalAuthorizationServerAsync(Uri authorizationUrl, Uri redirectUri, CancellationToken cancellationToken)
    {
        // Extract the redirect URI path including the query string
        var redirectUriWithPath = redirectUri.AbsoluteUri;
        
        // For the listener prefix, we want just the scheme, host, and port part
        var listenerPrefix = redirectUri.GetLeftPart(UriPartial.Authority);
        
        // Make sure the listener prefix has a trailing slash
        if (!listenerPrefix.EndsWith("/"))
        {
            listenerPrefix += "/";
        }
        
        Console.WriteLine($"Setting up HTTP listener with prefix: {listenerPrefix}");
        
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        
        try
        {
            // Start the listener first
            listener.Start();
            Console.WriteLine("HTTP listener started");
            
            // Create a cancellation token source with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
            
            // Open the browser to the authorization URL
            Console.WriteLine($"Opening browser to: {authorizationUrl}");
            OpenBrowser(authorizationUrl);
            
            // Race the HTTP callback against the timeout token
            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, linkedCts.Token));
            
            if (completedTask != contextTask)
            {
                Console.WriteLine("Authorization timed out");
                return null;
            }
            
            // Get the completed HTTP context
            var context = await contextTask;
            
            // Process the callback response
            return ProcessAuthorizationCallback(context);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Authorization was canceled");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during authorization: {ex.Message}");
            return null;
        }
        finally
        {
            // Ensure the listener is stopped
            if (listener.IsListening)
            {
                listener.Stop();
                Console.WriteLine("HTTP listener stopped");
            }
        }
    }
    
    /// <summary>
    /// Process the callback from the authorization server.
    /// </summary>
    /// <param name="context">The HTTP context from the callback.</param>
    /// <returns>The authorization code if present, otherwise null.</returns>
    private string? ProcessAuthorizationCallback(System.Net.HttpListenerContext context)
    {
        try
        {
            // Parse the query string to get the authorization code
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
            
            // Check for errors
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"Authorization error: {error}");
                return null;
            }
            
            // Return the authorization code
            if (!string.IsNullOrEmpty(code))
            {
                Console.WriteLine($"Received authorization code: {code[..Math.Min(6, code.Length)]}...");
                return code;
            }
            else
            {
                Console.WriteLine("No authorization code received");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing callback: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Opens the system browser to the specified URL.
    /// </summary>
    /// <param name="url">The URL to open in the browser.</param>
    private void OpenBrowser(Uri url)
    {
        try
        {
            // Use the default system browser to open the URL
            Console.WriteLine($"Opening browser to {url}");
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
            Console.WriteLine($"Please manually browse to: {url}");
        }
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
        queryParams["client_id"] = clientId;
        queryParams["redirect_uri"] = _redirectUri.ToString();
        queryParams["response_type"] = "code";
        queryParams["code_challenge"] = codeChallenge;
        queryParams["code_challenge_method"] = "S256";
        
        // Use the scopes provided in the constructor
        if (_scopes.Any())
        {
            queryParams["scope"] = string.Join(" ", _scopes);
        }
        // If no scopes were provided, fall back to the resource metadata scopes
        else if (resourceMetadata.ScopesSupported.Count > 0)
        {
            queryParams["scope"] = string.Join(" ", resourceMetadata.ScopesSupported);
            Console.WriteLine("Warning: Using scopes from resource metadata. It's recommended to provide scopes in the constructor instead.");
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
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier
        });
        
        // Add client authentication if we have a client secret
        if (!string.IsNullOrEmpty(clientSecret))
        {
            var authHeader = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
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
                    // Set the time when the token was obtained
                    tokenResponse.ObtainedAt = DateTimeOffset.UtcNow;
                    
                    // Calculate expiration time if not set
                    if (tokenResponse.ExpiresIn > 0 && tokenResponse.ExpiresAt == default)
                    {
                        tokenResponse.ExpiresAt = tokenResponse.ObtainedAt.AddSeconds(tokenResponse.ExpiresIn);
                    }
                    
                    Console.WriteLine("Token exchange successful");
                    return tokenResponse;
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