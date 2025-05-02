using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Auth.Types;
using ModelContextProtocol.Utils.Json;

namespace ModelContextProtocol.Auth;

/// <summary>
/// Provides helper methods for handling OAuth authorization.
/// </summary>
public static class OAuthAuthorizationHelpers
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
        return async (Uri authorizationUri) =>
        {
            string redirectUri = $"http://{hostname}:{listenPort}{redirectPath}";
            
            // Add the redirect_uri parameter to the authorization URI if it's not already present
            string authUrl = authorizationUri.ToString();
            if (!authUrl.Contains("redirect_uri="))
            {
                var separator = authUrl.Contains("?") ? "&" : "?";
                authUrl = $"{authUrl}{separator}redirect_uri={WebUtility.UrlEncode(redirectUri)}";
            }
            
            var authCodeTcs = new TaskCompletionSource<string>();
            
            // Ensure the path has a trailing slash for the HttpListener prefix
            string listenerPrefix = $"http://{hostname}:{listenPort}{redirectPath}";
            if (!listenerPrefix.EndsWith("/"))
            {
                listenerPrefix += "/";
            }

            using var listener = new HttpListener();
            listener.Prefixes.Add(listenerPrefix);
            
            // Start the listener BEFORE opening the browser
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                throw new InvalidOperationException($"Failed to start HTTP listener on {listenerPrefix}: {ex.Message}");
            }

            // Create a cancellation token source with a timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            
            _ = Task.Run(async () =>
            {
                try
                {
                    // GetContextAsync doesn't accept a cancellation token, so we need to handle cancellation manually
                    var contextTask = listener.GetContextAsync();
                    var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token));
                    
                    if (completedTask == contextTask)
                    {
                        var context = await contextTask;
                        var request = context.Request;
                        var response = context.Response;

                        string? code = request.QueryString["code"];
                        string? error = request.QueryString["error"];
                        string html;
                        string? resultCode = null;

                        if (!string.IsNullOrEmpty(error))
                        {
                            html = $"<html><body><h1>Authorization Failed</h1><p>Error: {WebUtility.HtmlEncode(error)}</p></body></html>";
                        }
                        else if (string.IsNullOrEmpty(code))
                        {
                            html = "<html><body><h1>Authorization Failed</h1><p>No authorization code received.</p></body></html>";
                        }
                        else
                        {
                            html = "<html><body><h1>Authorization Successful</h1><p>You may now close this window.</p></body></html>";
                            resultCode = code;
                        }

                        try
                        {
                            // Send response to browser
                            byte[] buffer = Encoding.UTF8.GetBytes(html);
                            response.ContentType = "text/html";
                            response.ContentLength64 = buffer.Length;
                            response.OutputStream.Write(buffer, 0, buffer.Length);
                            
                            // IMPORTANT: Explicitly close the response to ensure it's fully sent
                            response.Close();
                            
                            // Now that we've finished processing the browser response,
                            // we can safely signal completion or failure with the auth code
                            if (resultCode != null)
                            {
                                authCodeTcs.TrySetResult(resultCode);
                            }
                            else if (!string.IsNullOrEmpty(error))
                            {
                                authCodeTcs.TrySetException(new InvalidOperationException($"Authorization failed: {error}"));
                            }
                            else
                            {
                                authCodeTcs.TrySetException(new InvalidOperationException("No authorization code received"));
                            }
                        }
                        catch (Exception ex)
                        {
                            authCodeTcs.TrySetException(new InvalidOperationException($"Error processing browser response: {ex.Message}"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    authCodeTcs.TrySetException(ex);
                }
            });

            // Now open the browser AFTER the listener is started
            await openBrowser(authUrl);

            try
            {
                // Use a timeout to avoid hanging indefinitely
                string authCode = await authCodeTcs.Task.WaitAsync(cts.Token);
                return authCode;
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("Authorization timed out after 5 minutes.");
            }
            finally
            {
                // Ensure the listener is stopped when we're done
                listener.Stop();
            }
        };
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