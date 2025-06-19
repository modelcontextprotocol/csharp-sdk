using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net.Http.Headers;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// A delegating handler that adds authentication tokens to requests and handles 401 responses.
/// </summary>
internal sealed class AuthenticatingMcpHttpClient(HttpClient httpClient, IMcpCredentialProvider credentialProvider) : McpHttpClient(httpClient)
{
    private static readonly char[] SchemeSplitDelimiters = [' ', ','];

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

        // Find the intersection between what the server supports and what our provider supports
        string? bestSchemeMatch = null;

        // First try to find a direct match with the current scheme if it's still valid
        string schemeUsed = originalRequest.Headers.Authorization?.Scheme ?? _currentScheme ?? string.Empty;
        if (!string.IsNullOrEmpty(schemeUsed) &&
            serverSchemes.Contains(schemeUsed) &&
            credentialProvider.SupportedSchemes.Contains(schemeUsed))
        {
            bestSchemeMatch = schemeUsed;
        }
        else
        {
            // Find the first server scheme that's in our supported set
            bestSchemeMatch = serverSchemes.Intersect(credentialProvider.SupportedSchemes, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

            // If no match was found, either throw an exception or use default
            if (bestSchemeMatch is null)
            {
                if (serverSchemes.Count > 0)
                {
                    throw new IOException(
                        $"The server does not support any of the provided authentication schemes." +
                        $"Server supports: [{string.Join(", ", serverSchemes)}], " +
                        $"Provider supports: [{string.Join(", ", credentialProvider.SupportedSchemes)}].");
                }

                // If the server didn't specify any schemes, use the provider's default
                bestSchemeMatch = credentialProvider.SupportedSchemes.FirstOrDefault();
            }
        }

        if (bestSchemeMatch != null)
        {
            try
            {
                // Try to handle the 401 response with the selected scheme
                var (handled, recommendedScheme) = await credentialProvider.HandleUnauthorizedResponseAsync(
                    response,
                    bestSchemeMatch,
                    cancellationToken).ConfigureAwait(false);

                if (!handled)
                {
                    throw new McpException(
                        $"Failed to handle unauthorized response with scheme '{bestSchemeMatch}'. " +
                        "The authentication provider was unable to process the authentication challenge.");
                }

                _currentScheme = recommendedScheme ?? bestSchemeMatch;
            }
            catch (Exception ex)
            {
                throw new McpException(
                    $"Failed to handle unauthorized response with scheme '{bestSchemeMatch}'. " +
                    "The authentication provider encountered an error while processing the authentication challenge.",
                    ex);
            }

            var retryRequest = new HttpRequestMessage(originalRequest.Method, originalRequest.RequestUri)
            {
                Version = originalRequest.Version,
#if NET
                VersionPolicy = originalRequest.VersionPolicy,
#endif
            };

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

        return response; // Return the original response if we couldn't handle it
    }

    /// <summary>
    /// Extracts the authentication schemes that the server supports from the WWW-Authenticate headers.
    /// </summary>
    private static HashSet<string> ExtractServerSupportedSchemes(HttpResponseMessage response)
    {
        var serverSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (response.Headers.Contains("WWW-Authenticate"))
        {
            foreach (var authHeader in response.Headers.GetValues("WWW-Authenticate"))
            {
                // Extract the scheme from the WWW-Authenticate header
                // Format is typically: "Scheme param1=value1, param2=value2"
                string scheme = authHeader.Split(SchemeSplitDelimiters, StringSplitOptions.RemoveEmptyEntries)[0];
                serverSchemes.Add(scheme);
            }
        }

        return serverSchemes;
    }

    /// <summary>
    /// Adds an authorization header to the request.
    /// </summary>
    private async Task AddAuthorizationHeaderAsync(HttpRequestMessage request, string scheme, CancellationToken cancellationToken)
    {
        if (request.RequestUri != null)
        {
            var token = await credentialProvider.GetCredentialAsync(scheme, request.RequestUri, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
            }
        }
    }
}