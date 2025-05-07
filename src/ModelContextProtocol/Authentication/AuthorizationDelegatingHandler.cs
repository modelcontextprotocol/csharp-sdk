using ModelContextProtocol.Utils;
using System.Net.Http.Headers;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// A delegating handler that adds authentication tokens to requests and handles 401 responses.
/// </summary>
public class AuthorizationDelegatingHandler : DelegatingHandler
{
    private readonly ITokenProvider _authorizationProvider;
    private string _currentScheme;
    private static readonly char[] SchemeSplitDelimiters = { ' ', ',' };

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationDelegatingHandler"/> class.
    /// </summary>
    /// <param name="authorizationProvider">The provider that supplies authentication tokens.</param>
    public AuthorizationDelegatingHandler(ITokenProvider authorizationProvider)
    {
        Throw.IfNull(authorizationProvider);

        _authorizationProvider = authorizationProvider;
        
        // Select first supported scheme as the default
        _currentScheme = _authorizationProvider.SupportedSchemes.FirstOrDefault() ??
            throw new ArgumentException("Authorization provider must support at least one authentication scheme.", nameof(authorizationProvider));
    }

    /// <summary>
    /// Sends an HTTP request with authentication handling.
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization == null)
        {
            await AddAuthorizationHeaderAsync(request, _currentScheme, cancellationToken).ConfigureAwait(false);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return await HandleUnauthorizedResponseAsync(request, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>
    /// Handles a 401 Unauthorized response by attempting to authenticate and retry the request.
    /// </summary>
    private async Task<HttpResponseMessage> HandleUnauthorizedResponseAsync(
        HttpRequestMessage originalRequest, 
        HttpResponseMessage response, 
        CancellationToken cancellationToken)
    {
        // Gather the schemes the server wants us to use from WWW-Authenticate headers
        var serverSchemes = ExtractServerSupportedSchemes(response);

        // Find the intersection between what the server supports and what our provider supports
        var supportedSchemes = _authorizationProvider.SupportedSchemes.ToList();
        string? bestSchemeMatch = null;

        // First try to find a direct match with the current scheme if it's still valid
        string schemeUsed = originalRequest.Headers.Authorization?.Scheme ?? _currentScheme ?? string.Empty;
        if (serverSchemes.Contains(schemeUsed) && supportedSchemes.Contains(schemeUsed))
        {
            bestSchemeMatch = schemeUsed;
        }
        else
        {
            // Find the first server scheme that's in our supported set
            bestSchemeMatch = serverSchemes.Intersect(supportedSchemes, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

            // If no match was found, either throw an exception or use default
            if (bestSchemeMatch is null)
            {
                if (serverSchemes.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"No matching authentication scheme found. Server supports: [{string.Join(", ", serverSchemes)}], " +
                        $"Provider supports: [{string.Join(", ", supportedSchemes)}].");
                }

                // If the server didn't specify any schemes, use the provider's default
                bestSchemeMatch = supportedSchemes.FirstOrDefault();
            }
        }

        // If we have a scheme to try, use it
        if (bestSchemeMatch != null)
        {
            // Try to handle the 401 response with the selected scheme
            var (handled, recommendedScheme) = await _authorizationProvider.HandleUnauthorizedResponseAsync(
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
            
            var retryRequest = new HttpRequestMessage(originalRequest.Method, originalRequest.RequestUri)
            {
                Version = originalRequest.Version,
#if NET
                VersionPolicy = originalRequest.VersionPolicy,
#endif
                Content = originalRequest.Content
            };
            
            // Copy headers except Authorization which we'll set separately
            foreach (var header in originalRequest.Headers)
            {
                if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
#if NET
            foreach (var property in originalRequest.Options)
            {
                retryRequest.Options.Set(new HttpRequestOptionsKey<object?>(property.Key), property.Value);
            }
#else
            foreach (var property in originalRequest.Properties)
            {
                retryRequest.Properties.Add(property);
            }
#endif

            // Add the new authorization header
            await AddAuthorizationHeaderAsync(retryRequest, _currentScheme, cancellationToken).ConfigureAwait(false);
            
            // Send the retry request
            return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        }
        
        return response; // Return the original response if we couldn't handle it
    }

    /// <summary>
    /// Extracts the authentication schemes that the server supports from the WWW-Authenticate headers.
    /// </summary>
    private static List<string> ExtractServerSupportedSchemes(HttpResponseMessage response)
    {
        var serverSchemes = new List<string>();
        
        if (response.Headers.Contains("WWW-Authenticate"))
        {
            foreach (var authHeader in response.Headers.GetValues("WWW-Authenticate"))
            {
                // Extract the scheme from the WWW-Authenticate header
                // Format is typically: "Scheme param1=value1, param2=value2"
                string scheme = authHeader.Split(SchemeSplitDelimiters, StringSplitOptions.RemoveEmptyEntries)[0];
                if (!string.IsNullOrEmpty(scheme))
                {
                    serverSchemes.Add(scheme);
                }
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
            var token = await _authorizationProvider.GetCredentialAsync(scheme, request.RequestUri, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
            }
        }
    }
}