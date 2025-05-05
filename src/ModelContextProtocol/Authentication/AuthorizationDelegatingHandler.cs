using System.Net.Http.Headers;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// A delegating handler that adds authentication tokens to requests and handles 401 responses.
/// </summary>
public class AuthorizationDelegatingHandler : DelegatingHandler
{
    private readonly IMcpAuthorizationProvider _authorizationProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationDelegatingHandler"/> class.
    /// </summary>
    /// <param name="authorizationProvider">The provider that supplies authentication tokens.</param>
    public AuthorizationDelegatingHandler(IMcpAuthorizationProvider authorizationProvider)
    {
        _authorizationProvider = authorizationProvider ?? throw new ArgumentNullException(nameof(authorizationProvider));
    }

    /// <summary>
    /// Sends an HTTP request with authentication handling.
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Add the authentication token to the request if not already present
        if (request.Headers.Authorization == null)
        {
            await AddAuthorizationHeaderAsync(request, cancellationToken);
        }

        // Send the request through the inner handler
        var response = await base.SendAsync(request, cancellationToken);

        // Handle unauthorized responses
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Try to handle the unauthorized response
            var handled = await _authorizationProvider.HandleUnauthorizedResponseAsync(
                response,
                cancellationToken);

            if (handled)
            {
                // If the unauthorized response was handled, retry the request
                var retryRequest = await CloneHttpRequestMessageAsync(request);

                // Get a new token
                await AddAuthorizationHeaderAsync(retryRequest, cancellationToken);

                // Send the retry request
                return await base.SendAsync(retryRequest, cancellationToken);
            }
        }

        return response;
    }

    /// <summary>
    /// Adds an authorization header to the request.
    /// </summary>
    private async Task AddAuthorizationHeaderAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri != null)
        {
            var token = await _authorizationProvider.GetCredentialAsync(request.RequestUri, cancellationToken);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(_authorizationProvider.AuthorizationScheme, token);
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
            var contentBytes = await request.Content.ReadAsByteArrayAsync();
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