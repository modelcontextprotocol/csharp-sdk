using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net.Http.Headers;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// A delegating handler that adds authentication tokens to requests and handles 401 responses.
/// </summary>
internal sealed class AuthenticatingMcpHttpClient(HttpClient httpClient, ClientOAuthProvider credentialProvider) : McpHttpClient(httpClient)
{
    // Select first supported scheme as the default
    private string _currentScheme = credentialProvider.SupportedSchemes.FirstOrDefault() ??
        throw new ArgumentException("Authorization provider must support at least one authentication scheme.", nameof(credentialProvider));

    /// <summary>
    /// Sends an HTTP request with authentication handling.
    /// </summary>
    internal override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, JsonRpcMessage? message, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization == null)
        {
            await AddAuthorizationHeaderAsync(request, _currentScheme, cancellationToken).ConfigureAwait(false);
        }

        var response = await base.SendAsync(request, message, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return await HandleUnauthorizedResponseAsync(request, message, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>
    /// Handles a 401 Unauthorized response by attempting to authenticate and retry the request.
    /// </summary>
    private async Task<HttpResponseMessage> HandleUnauthorizedResponseAsync(
        HttpRequestMessage originalRequest,
        JsonRpcMessage? originalJsonRpcMessage,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Gather the schemes the server wants us to use from WWW-Authenticate headers
        var serverSchemes = ExtractServerSupportedSchemes(response);

        if (!serverSchemes.Contains(_currentScheme))
        {
            // Find the first server scheme that's in our supported set
            var bestSchemeMatch = serverSchemes.Intersect(credentialProvider.SupportedSchemes, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

            if (bestSchemeMatch is not null)
            {
                _currentScheme = bestSchemeMatch;
            }
            else if (serverSchemes.Count > 0)
            {
                // If no match was found, either throw an exception or use default
                throw new McpException(
                    $"The server does not support any of the provided authentication schemes." +
                    $"Server supports: [{string.Join(", ", serverSchemes)}], " +
                    $"Provider supports: [{string.Join(", ", credentialProvider.SupportedSchemes)}].");
            }
        }

        // Try to handle the 401 response with the selected scheme
        await credentialProvider.HandleUnauthorizedResponseAsync(_currentScheme, response, cancellationToken).ConfigureAwait(false);

        using var retryRequest = new HttpRequestMessage(originalRequest.Method, originalRequest.RequestUri);

        // Copy headers except Authorization which we'll set separately
        foreach (var header in originalRequest.Headers)
        {
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        await AddAuthorizationHeaderAsync(retryRequest, _currentScheme, cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(retryRequest, originalJsonRpcMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts the authentication schemes that the server supports from the WWW-Authenticate headers.
    /// </summary>
    private static HashSet<string> ExtractServerSupportedSchemes(HttpResponseMessage response)
    {
        var serverSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers.WwwAuthenticate)
        {
            serverSchemes.Add(header.Scheme);
        }

        return serverSchemes;
    }

    /// <summary>
    /// Adds an authorization header to the request.
    /// </summary>
    private async Task AddAuthorizationHeaderAsync(HttpRequestMessage request, string scheme, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
        {
            return;
        }

        var token = await credentialProvider.GetCredentialAsync(scheme, request.RequestUri, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
    }
}