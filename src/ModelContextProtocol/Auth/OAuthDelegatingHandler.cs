// filepath: c:\Users\ddelimarsky\source\csharp-sdk-anm\src\ModelContextProtocol\Auth\OAuthDelegatingHandler.cs
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace ModelContextProtocol.Auth;

/// <summary>
/// A delegating handler that automatically handles OAuth authentication for MCP clients.
/// </summary>
public class OAuthDelegatingHandler : DelegatingHandler
{
    private readonly Uri _redirectUri;
    private readonly string? _clientId;
    private readonly string? _clientName;
    private readonly IEnumerable<string>? _scopes;
    private readonly Func<Uri, Task<string>>? _authorizationHandler;
    private OAuthTokenResponse? _tokenResponse;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthDelegatingHandler"/> class.
    /// </summary>
    /// <param name="redirectUri">The URI to redirect to after authentication.</param>
    /// <param name="clientId">The client ID to use for authentication, or null to register a new client.</param>
    /// <param name="clientName">The client name to use for registration.</param>
    /// <param name="scopes">The requested scopes.</param>
    /// <param name="authorizationHandler">A handler to invoke when authorization is required.</param>
    public OAuthDelegatingHandler(
        Uri redirectUri,
        string? clientId = null,
        string? clientName = null,
        IEnumerable<string>? scopes = null,
        Func<Uri, Task<string>>? authorizationHandler = null)
    {
        _redirectUri = redirectUri;
        _clientId = clientId;
        _clientName = clientName;
        _scopes = scopes;
        _authorizationHandler = authorizationHandler;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthDelegatingHandler"/> class with an inner handler.
    /// </summary>
    /// <param name="innerHandler">The inner handler which processes the HTTP response messages.</param>
    /// <param name="redirectUri">The URI to redirect to after authentication.</param>
    /// <param name="clientId">The client ID to use for authentication, or null to register a new client.</param>
    /// <param name="clientName">The client name to use for registration.</param>
    /// <param name="scopes">The requested scopes.</param>
    /// <param name="authorizationHandler">A handler to invoke when authorization is required.</param>
    public OAuthDelegatingHandler(
        HttpMessageHandler innerHandler,
        Uri redirectUri,
        string? clientId = null,
        string? clientName = null,
        IEnumerable<string>? scopes = null,
        Func<Uri, Task<string>>? authorizationHandler = null)
        : base(innerHandler)
    {
        _redirectUri = redirectUri;
        _clientId = clientId;
        _clientName = clientName;
        _scopes = scopes;
        _authorizationHandler = authorizationHandler;
    }

    /// <summary>
    /// Manually set an OAuth token to use for subsequent requests.
    /// </summary>
    /// <param name="tokenResponse">The OAuth token response.</param>
    public void SetToken(OAuthTokenResponse tokenResponse)
    {
        _tokenResponse = tokenResponse;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // If we have a token, attach it to the request
        if (_tokenResponse != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenResponse.AccessToken);
        }

        // Send the request
        var response = await base.SendAsync(request, cancellationToken);

        // If the response is 401 Unauthorized, try to authenticate
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Create a temporary HttpClient to handle the authentication
            // We need to use a new client to avoid infinite recursion
            using var httpClient = new HttpClient();
            httpClient.ConfigureAuthorizationHandler(
                _redirectUri,
                _clientId,
                _clientName,
                _scopes,
                _authorizationHandler);

            try
            {
                // Handle the 401 response
                var authResponse = await httpClient.HandleUnauthorizedResponseAsync(response);
                _tokenResponse = authResponse; // Now using a non-nullable intermediate variable

                // If we have a token, retry the original request with the token
                // Create a new request (the original request has already been sent)
                var newRequest = new HttpRequestMessage
                {
                    Method = request.Method,
                    RequestUri = request.RequestUri,
                    Content = request.Content,
                    Headers = {
                        Authorization = new AuthenticationHeaderValue("Bearer", _tokenResponse.AccessToken)
                    }
                };

                // Copy other headers
                foreach (var header in request.Headers)
                {
                    if (header.Key != "Authorization")
                    {
                        newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                // Send the request again
                return await base.SendAsync(newRequest, cancellationToken);
            }
            catch (Exception)
            {
                // If authentication fails, return the original 401 response
            }
        }

        return response;
    }
}