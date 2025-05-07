using ModelContextProtocol.Authentication.Types;
using ModelContextProtocol.Utils;
using System.Net.Http.Headers;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// A delegating handler that adds authentication tokens to requests and handles 401 responses.
/// </summary>
public class AuthorizationDelegatingHandler : DelegatingHandler
{
    private readonly IMcpAuthorizationProvider _authorizationProvider;
    private string _currentScheme;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationDelegatingHandler"/> class.
    /// </summary>
    /// <param name="authorizationProvider">The provider that supplies authentication tokens.</param>
    public AuthorizationDelegatingHandler(IMcpAuthorizationProvider authorizationProvider)
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
            // Gather the schemes the server wants us to use from WWW-Authenticate headers
            var serverSchemes = ExtractServerSupportedSchemes(response);
            
            // Find the intersection between what the server supports and what our provider supports
            var supportedSchemes = _authorizationProvider.SupportedSchemes.ToList();
            string? bestSchemeMatch = null;
            
            // First try to find a direct match with the current scheme if it's still valid
            string schemeUsed = request.Headers.Authorization?.Scheme ?? _currentScheme ?? string.Empty;
            if (serverSchemes.Contains(schemeUsed) && supportedSchemes.Contains(schemeUsed))
            {
                bestSchemeMatch = schemeUsed;
            }
            else
            {
                // Try to find any matching scheme between server and provider using a HashSet for O(N) time complexity
                // Convert the supported schemes to a HashSet for O(1) lookups
                var supportedSchemesSet = new HashSet<string>(supportedSchemes, StringComparer.OrdinalIgnoreCase);
                
                // Find the first server scheme that's in our supported set
                bestSchemeMatch = serverSchemes.FirstOrDefault(scheme => supportedSchemesSet.Contains(scheme));
                
                // If no match was found, either throw an exception or use default
                if (bestSchemeMatch is null)
                {
                    if (serverSchemes.Count > 0)
                    {
                        throw new AuthenticationSchemeMismatchException(
                            $"No matching authentication scheme found. Server supports: [{string.Join(", ", serverSchemes)}], " +
                            $"Provider supports: [{string.Join(", ", supportedSchemes)}].",
                            serverSchemes,
                            supportedSchemes);
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

                if (handled)
                {
                    var retryRequest = await CloneHttpRequestMessageAsync(request).ConfigureAwait(false);
                    
                    _currentScheme = recommendedScheme ?? bestSchemeMatch;
                    
                    await AddAuthorizationHeaderAsync(retryRequest, _currentScheme, cancellationToken).ConfigureAwait(false);
                    return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw new McpException(
                        $"Failed to handle unauthorized response with scheme '{bestSchemeMatch}'. " +
                        "The authentication provider was unable to process the authentication challenge.");
                }
            }
        }

        return response;
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
                string scheme = authHeader.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)[0];
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

    /// <summary>
    /// Creates a clone of the HTTP request message.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        // Copy the request headers
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy the request content if present
        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var cloneContent = new ByteArrayContent(contentBytes);

            // Copy the content headers
            foreach (var header in request.Content.Headers)
            {
                cloneContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = cloneContent;
        }

        // Copy the request properties
#pragma warning disable CS0618 // Type or member is obsolete
        foreach (var property in request.Properties)
        {
            clone.Properties.Add(property);
        }
#pragma warning restore CS0618 // Type or member is obsolete

        // Copy the request version
        clone.Version = request.Version;

        return clone;
    }
}