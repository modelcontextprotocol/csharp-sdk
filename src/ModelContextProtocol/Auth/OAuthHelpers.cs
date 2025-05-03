using ModelContextProtocol.Auth.Types;
using ModelContextProtocol.Utils.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Auth;

/// <summary>
/// Provides helper methods for handling OAuth authorization.
/// </summary>
public static class OAuthHelpers
{
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// Creates an HTTP listener callback for handling OAuth 2.0 authorization code flow.
    /// </summary>
    /// <param name="openBrowser">A function that opens a browser with the given URL.</param>
    /// <param name="hostname">The hostname to listen on. Defaults to "localhost".</param>
    /// <param name="listenPort">The port to listen on. Defaults to 8888.</param>
    /// <param name="redirectPath">The redirect path for the HTTP listener. Defaults to "/callback".</param>
    /// <returns>
    /// A function that takes an authorization URI and returns a task that resolves to the authorization code.
    /// </returns>
    public static Func<Uri, Task<string>> CreateHttpListenerCallback(
        Func<string, Task> openBrowser,
        string hostname = "localhost",
        int listenPort = 8888,
        string redirectPath = "/callback")
    {
        return async authorizationUri =>
        {
            // Ensure the path has a trailing slash for the HttpListener prefix
            var listenerPrefix = $"http://{hostname}:{listenPort}{redirectPath.TrimEnd('/')}/";

            using var listener = new HttpListener();
            listener.Prefixes.Add(listenerPrefix);

            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                throw new InvalidOperationException($"Failed to start HTTP listener on {listenerPrefix}: {ex.Message}");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            
            await openBrowser(authorizationUri.ToString());

            try
            {
                var contextTask = listener.GetContextAsync();
                var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token));
                
                if (completedTask != contextTask)
                {
                    throw new InvalidOperationException("Authorization timed out after 5 minutes.");
                }

                var context = await contextTask;
                return ProcessCallback(context);
            }
            finally
            {
                listener.Stop();
            }
        };
    }

    /// <summary>
    /// Processes the HTTP callback and extracts the authorization code.
    /// </summary>
    private static string ProcessCallback(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        string? code = request.QueryString["code"];
        string? error = request.QueryString["error"];
        string html;

        if (!string.IsNullOrEmpty(error))
        {
            html = $"<html><body><h1>Authorization Failed</h1><p>Error: {WebUtility.HtmlEncode(error)}</p></body></html>";
            SendResponse(response, html);
            throw new InvalidOperationException($"Authorization failed: {error}");
        }
        
        if (string.IsNullOrEmpty(code))
        {
            html = "<html><body><h1>Authorization Failed</h1><p>No authorization code received.</p></body></html>";
            SendResponse(response, html);
            throw new InvalidOperationException("No authorization code received");
        }
        
        html = "<html><body><h1>Authorization Successful</h1><p>You may now close this window.</p></body></html>";
        SendResponse(response, html);
        return code;
    }

    /// <summary>
    /// Sends an HTML response to the browser.
    /// </summary>
    private static void SendResponse(HttpListenerResponse response, string html)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            
            // IMPORTANT: Explicitly close the response to ensure it's fully sent
            response.Close();
        }
        catch
        {
            // Silently handle errors - we're already in an error handling path
            // and can't throw further exceptions or log to the console in a library
            // TODO: Need a better implementation here.
        }
    }

    /// <summary>
    /// Exchanges an authorization code for an OAuth token.
    /// </summary>
    /// <param name="tokenEndpoint">The token endpoint URI.</param>
    /// <param name="clientId">The client ID.</param>
    /// <param name="clientSecret">The client secret, if any.</param>
    /// <param name="redirectUri">The redirect URI used in the authorization request.</param>
    /// <param name="authorizationCode">The authorization code received from the authorization server.</param>
    /// <param name="codeVerifier">The PKCE code verifier.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The OAuth token response.</returns>
    public static async Task<OAuthToken> ExchangeAuthorizationCodeForTokenAsync(
        Uri tokenEndpoint,
        string clientId,
        string? clientSecret,
        Uri redirectUri,
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken = default)
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
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = requestContent
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        else
        {
            response = await _httpClient.PostAsync(tokenEndpoint, requestContent, cancellationToken);
        }
        
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenResponse = JsonSerializer.Deserialize(json, McpJsonUtilities.DefaultOptions.GetTypeInfo<OAuthToken>());
        if (tokenResponse == null)
        {
            throw new InvalidOperationException("Failed to parse token response.");
        }
        
        return tokenResponse;
    }
    
    /// <summary>
    /// Creates a complete OAuth authorization code flow handler that automatically exchanges the code for a token.
    /// </summary>
    /// <param name="tokenEndpoint">The token endpoint URI.</param>
    /// <param name="clientId">The client ID.</param>
    /// <param name="clientSecret">The client secret, if any.</param>
    /// <param name="redirectUri">The redirect URI used in the authorization request.</param>
    /// <param name="codeVerifier">The PKCE code verifier.</param>
    /// <param name="openBrowser">A function that opens a browser with the given URL.</param>
    /// <param name="hostname">The hostname to listen on. Defaults to "localhost".</param>
    /// <param name="listenPort">The port to listen on. Defaults to 8888.</param>
    /// <param name="redirectPath">The redirect path for the HTTP listener. Defaults to "/callback".</param>
    /// <returns>A function that takes an authorization URI and returns a task that resolves to the OAuth token.</returns>
    public static Func<Uri, Task<OAuthToken>> CreateCompleteOAuthFlowHandler(
        Uri tokenEndpoint,
        string clientId,
        string? clientSecret,
        Uri redirectUri,
        string codeVerifier,
        Func<string, Task> openBrowser,
        string hostname = "localhost",
        int listenPort = 8888,
        string redirectPath = "/callback")
    {
        var codeHandler = CreateHttpListenerCallback(openBrowser, hostname, listenPort, redirectPath);
        
        return async (authorizationUri) =>
        {
            // First get the authorization code
            string authorizationCode = await codeHandler(authorizationUri);
            
            // Then exchange it for a token
            return await ExchangeAuthorizationCodeForTokenAsync(
                tokenEndpoint,
                clientId,
                clientSecret,
                redirectUri,
                authorizationCode,
                codeVerifier);
        };
    }
}