using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#if NET9_0_OR_GREATER
using System.Buffers.Text;
#endif
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// A generic implementation of an OAuth authorization provider.
/// </summary>
internal sealed partial class ClientOAuthProvider : McpHttpClient
{
    /// <summary>
    /// The Bearer authentication scheme.
    /// </summary>
    private const string BearerScheme = "Bearer";
    private const string ProtectedResourceMetadataWellKnownPath = "/.well-known/oauth-protected-resource";

    private readonly Uri _serverUrl;
    private readonly Uri _redirectUri;
    private readonly string? _configuredScopes;
    private readonly IDictionary<string, string> _additionalAuthorizationParameters;
    private readonly Func<IReadOnlyList<Uri>, Uri?> _authServerSelector;
    private readonly AuthorizationRedirectDelegate _authorizationRedirectDelegate;
    private readonly Uri? _clientMetadataDocumentUri;

    // _dcrClientName, _dcrClientUri, _dcrInitialAccessToken and _dcrResponseDelegate are used for dynamic client registration (RFC 7591)
    private readonly string? _dcrClientName;
    private readonly Uri? _dcrClientUri;
    private readonly string? _dcrInitialAccessToken;
    private readonly Func<DynamicClientRegistrationResponse, CancellationToken, Task>? _dcrResponseDelegate;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    private string? _clientId;
    private string? _clientSecret;
    private ITokenCache _tokenCache;
    private AuthorizationServerMetadata? _authServerMetadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientOAuthProvider"/> class using the specified options.
    /// </summary>
    /// <param name="serverUrl">The MCP server URL.</param>
    /// <param name="options">The OAuth provider configuration options.</param>
    /// <param name="httpClient">The HTTP client to use for OAuth requests. If null, a default HttpClient is used.</param>
    /// <param name="loggerFactory">A logger factory to handle diagnostic messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverUrl"/> or <paramref name="options"/> is null.</exception>
    public ClientOAuthProvider(
        Uri serverUrl,
        ClientOAuthOptions options,
        HttpClient httpClient,
        ILoggerFactory? loggerFactory = null)
        : base(httpClient)
    {
        _serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
        _httpClient = httpClient;
        _logger = (ILogger?)loggerFactory?.CreateLogger<ClientOAuthProvider>() ?? NullLogger.Instance;

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _clientId = options.ClientId;
        _clientSecret = options.ClientSecret;
        _redirectUri = options.RedirectUri ?? throw new ArgumentException("ClientOAuthOptions.RedirectUri must configured.", nameof(options));
        _configuredScopes = options.Scopes is null ? null : string.Join(" ", options.Scopes);
        _additionalAuthorizationParameters = options.AdditionalAuthorizationParameters;
        _clientMetadataDocumentUri = options.ClientMetadataDocumentUri;

        // Set up authorization server selection strategy
        _authServerSelector = options.AuthServerSelector ?? DefaultAuthServerSelector;

        // Set up authorization URL handler (use default if not provided)
        _authorizationRedirectDelegate = options.AuthorizationRedirectDelegate ?? DefaultAuthorizationUrlHandler;

        _dcrClientName = options.DynamicClientRegistration?.ClientName;
        _dcrClientUri = options.DynamicClientRegistration?.ClientUri;
        _dcrInitialAccessToken = options.DynamicClientRegistration?.InitialAccessToken;
        _dcrResponseDelegate = options.DynamicClientRegistration?.ResponseDelegate;
        _tokenCache = options.TokenCache ?? new InMemoryTokenCache();
    }

    /// <summary>
    /// Default authorization server selection strategy that selects the first available server.
    /// </summary>
    /// <param name="availableServers">List of available authorization servers.</param>
    /// <returns>The selected authorization server, or null if none are available.</returns>
    private static Uri? DefaultAuthServerSelector(IReadOnlyList<Uri> availableServers) => availableServers.FirstOrDefault();

    /// <summary>
    /// Default authorization URL handler that displays the URL to the user for manual input.
    /// </summary>
    /// <param name="authorizationUrl">The authorization URL to handle.</param>
    /// <param name="redirectUri">The redirect URI where the authorization code will be sent.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The authorization code entered by the user, or null if none was provided.</returns>
    private static Task<string?> DefaultAuthorizationUrlHandler(Uri authorizationUrl, Uri redirectUri, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Please open the following URL in your browser to authorize the application:");
        Console.WriteLine($"{authorizationUrl}");
        Console.WriteLine();
        Console.Write("Enter the authorization code from the redirect URL: ");
        var authorizationCode = Console.ReadLine();
        return Task.FromResult<string?>(authorizationCode);
    }

    internal override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, JsonRpcMessage? message, CancellationToken cancellationToken)
    {
        bool didRefresh = false;

        if (request.Headers.Authorization is null && request.RequestUri is not null)
        {
            string? accessToken;
            (accessToken, didRefresh) = await GetAccessTokenSilentAsync(request.RequestUri, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
            }
        }

        var response = await base.SendAsync(request, message, cancellationToken).ConfigureAwait(false);

        if (ShouldRetryWithNewAccessToken(response))
        {
            return await HandleUnauthorizedResponseAsync(request, message, response, didRefresh, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<(string? AccessToken, bool DidRefresh)> GetAccessTokenSilentAsync(Uri resourceUri, CancellationToken cancellationToken)
    {
        var tokens = await _tokenCache.GetTokensAsync(cancellationToken).ConfigureAwait(false);

        // Return the token if it's valid
        if (tokens is not null && !tokens.IsExpired)
        {
            return (tokens.AccessToken, false);
        }

        // Try to refresh the access token if it is invalid and we have a refresh token.
        if (_authServerMetadata is not null && tokens?.RefreshToken is { Length: > 0 } refreshToken)
        {
            var newTokens = await RefreshAccessTokenAsync(refreshToken, resourceUri, _authServerMetadata, cancellationToken).ConfigureAwait(false);
            if (newTokens is not null)
            {
                return (newTokens.AccessToken, true);
            }
        }

        // No valid token - auth handler will trigger the 401 flow
        return (null, false);
    }

    private static bool ShouldRetryWithNewAccessToken(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return true;
        }

        // Only retry 403 Forbidden if it contains an insufficient_scope error as described in Section 10.1.1 of the MCP specification
        // https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization#runtime-insufficient-scope-errors
        if (response.StatusCode != System.Net.HttpStatusCode.Forbidden)
        {
            return false;
        }

        foreach (var header in response.Headers.WwwAuthenticate)
        {
            if (!string.Equals(header.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(header.Parameter))
            {
                continue;
            }

            var error = ParseWwwAuthenticateParameters(header.Parameter, "error");
            if (string.Equals(error, "insufficient_scope", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<HttpResponseMessage> HandleUnauthorizedResponseAsync(
        HttpRequestMessage originalRequest,
        JsonRpcMessage? originalJsonRpcMessage,
        HttpResponseMessage response,
        bool didRefresh,
        CancellationToken cancellationToken)
    {
        if (response.Headers.WwwAuthenticate.Count == 0)
        {
            LogMissingWwwAuthenticateHeader();
        }
        else if (!response.Headers.WwwAuthenticate.Any(static header => string.Equals(header.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase)))
        {
            var serverSchemes = string.Join(", ", response.Headers.WwwAuthenticate.Select(static header => header.Scheme));
            throw new McpException($"The server does not support the '{BearerScheme}' authentication scheme. Server supports: [{serverSchemes}].");
        }

        var accessToken = await GetAccessTokenAsync(response, didRefresh, cancellationToken).ConfigureAwait(false);
        LogOAuthAuthorizationCompleted();

        using var retryRequest = new HttpRequestMessage(originalRequest.Method, originalRequest.RequestUri);

        foreach (var header in originalRequest.Headers)
        {
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        retryRequest.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
        return await base.SendAsync(retryRequest, originalJsonRpcMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a 401 Unauthorized or 403 Forbidden response from a resource by completing any required OAuth flows.
    /// </summary>
    /// <param name="response">The HTTP response that triggered the authentication challenge.</param>
    /// <param name="didRefresh">Indicates whether a token refresh has already been attempted.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    private async Task<string> GetAccessTokenAsync(HttpResponseMessage response, bool didRefresh, CancellationToken cancellationToken = default)
    {
        // Get available authorization servers from the 401 or 403 response
        var protectedResourceMetadata = await ExtractProtectedResourceMetadata(response, _serverUrl, cancellationToken).ConfigureAwait(false);
        var availableAuthorizationServers = protectedResourceMetadata.AuthorizationServers;

        if (availableAuthorizationServers.Count == 0)
        {
            ThrowFailedToHandleUnauthorizedResponse("No authorization servers found in authentication challenge");
        }

        // Select authorization server using configured strategy
        var selectedAuthServer = _authServerSelector(availableAuthorizationServers);

        if (selectedAuthServer is null)
        {
            ThrowFailedToHandleUnauthorizedResponse($"Authorization server selection returned null. Available servers: {string.Join(", ", availableAuthorizationServers)}");
        }

        if (!availableAuthorizationServers.Contains(selectedAuthServer))
        {
            ThrowFailedToHandleUnauthorizedResponse($"Authorization server selector returned a server not in the available list: {selectedAuthServer}. Available servers: {string.Join(", ", availableAuthorizationServers)}");
        }

        LogSelectedAuthorizationServer(selectedAuthServer, availableAuthorizationServers.Count);

        // Get auth server metadata
        var authServerMetadata = await GetAuthServerMetadataAsync(selectedAuthServer, cancellationToken).ConfigureAwait(false);

        // Store auth server metadata for future refresh operations
        _authServerMetadata = authServerMetadata;

        // The existing access token must be invalid to have resulted in a 401 response, but refresh might still work.
        var resourceUri = GetRequiredResourceUri(protectedResourceMetadata);

        // Only attempt a token refresh if we haven't attempted to already for this request.
        // Also only attempt a token refresh for a 401 Unauthorized responses. Other response status codes
        // should not be used for expired access tokens. This is important because 403 forbiden responses can
        // be used for incremental consent which cannot be acheived with a simple refresh.
        if (!didRefresh &&
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await _tokenCache.GetTokensAsync(cancellationToken).ConfigureAwait(false) is { RefreshToken: { } refreshToken })
        {
            var refreshedTokens = await RefreshAccessTokenAsync(refreshToken, resourceUri, authServerMetadata, cancellationToken).ConfigureAwait(false);
            if (refreshedTokens is not null)
            {
                // A non-null result indicates the refresh succeeded and the new tokens have been stored.
                return refreshedTokens.AccessToken;
            }
        }

        // Assign a client ID if necessary
        if (string.IsNullOrEmpty(_clientId))
        {
            // Try using a client metadata document before falling back to dynamic client registration
            if (authServerMetadata.ClientIdMetadataDocumentSupported && _clientMetadataDocumentUri is not null)
            {
                ApplyClientIdMetadataDocument(_clientMetadataDocumentUri);
            }
            else
            {
                await PerformDynamicClientRegistrationAsync(protectedResourceMetadata, authServerMetadata, cancellationToken).ConfigureAwait(false);
            }
        }

        // Perform the OAuth flow
        return await InitiateAuthorizationCodeFlowAsync(protectedResourceMetadata, authServerMetadata, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyClientIdMetadataDocument(Uri metadataUri)
    {
        if (!IsValidClientMetadataDocumentUri(metadataUri))
        {
            ThrowFailedToHandleUnauthorizedResponse(
                $"{nameof(ClientOAuthOptions.ClientMetadataDocumentUri)} must be an HTTPS URL with a non-root absolute path. Value: '{metadataUri}'.");
        }

        _clientId = metadataUri.AbsoluteUri;

        // See: https://datatracker.ietf.org/doc/html/draft-ietf-oauth-client-id-metadata-document-00#section-3
        static bool IsValidClientMetadataDocumentUri(Uri uri)
            => uri.IsAbsoluteUri
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Length > 1; // AbsolutePath always starts with "/"
    }

    private async Task<AuthorizationServerMetadata> GetAuthServerMetadataAsync(Uri authServerUri, CancellationToken cancellationToken)
    {
        foreach (var wellKnownEndpoint in GetWellKnownMetadataUris(authServerUri))
        {
            try
            {
                var response = await _httpClient.GetAsync(wellKnownEndpoint, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var metadata = await JsonSerializer.DeserializeAsync(stream, McpJsonUtilities.JsonContext.Default.AuthorizationServerMetadata, cancellationToken).ConfigureAwait(false);

                if (metadata is null)
                {
                    continue;
                }

                if (metadata.AuthorizationEndpoint is null)
                {
                    ThrowFailedToHandleUnauthorizedResponse($"No authorization_endpoint was provided via '{wellKnownEndpoint}'.");
                }

                if (metadata.AuthorizationEndpoint.Scheme != Uri.UriSchemeHttp &&
                    metadata.AuthorizationEndpoint.Scheme != Uri.UriSchemeHttps)
                {
                    ThrowFailedToHandleUnauthorizedResponse($"AuthorizationEndpoint must use HTTP or HTTPS. '{metadata.AuthorizationEndpoint}' does not meet this requirement.");
                }

                metadata.ResponseTypesSupported ??= ["code"];
                metadata.GrantTypesSupported ??= ["authorization_code", "refresh_token"];
                metadata.TokenEndpointAuthMethodsSupported ??= ["client_secret_post"];
                metadata.CodeChallengeMethodsSupported ??= ["S256"];

                return metadata;
            }
            catch (Exception ex)
            {
                LogErrorFetchingAuthServerMetadata(ex, wellKnownEndpoint);
            }
        }

        throw new McpException($"Failed to find .well-known/openid-configuration or .well-known/oauth-authorization-server metadata for authorization server: '{authServerUri}'");
    }

    private static IEnumerable<Uri> GetWellKnownMetadataUris(Uri issuer)
    {
        var builder = new UriBuilder(issuer);
        var hostBase = builder.Uri.GetLeftPart(UriPartial.Authority);
        var trimmedPath = builder.Path?.Trim('/') ?? string.Empty;

        if (string.IsNullOrEmpty(trimmedPath))
        {
            yield return new Uri($"{hostBase}/.well-known/oauth-authorization-server");
            yield return new Uri($"{hostBase}/.well-known/openid-configuration");
        }
        else
        {
            yield return new Uri($"{hostBase}/.well-known/oauth-authorization-server/{trimmedPath}");
            yield return new Uri($"{hostBase}/.well-known/openid-configuration/{trimmedPath}");
            yield return new Uri($"{hostBase}/{trimmedPath}/.well-known/openid-configuration");
        }
    }

    private async Task<TokenContainer?> RefreshAccessTokenAsync(string refreshToken, Uri resourceUri, AuthorizationServerMetadata authServerMetadata, CancellationToken cancellationToken)
    {
        var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = GetClientIdOrThrow(),
            ["client_secret"] = _clientSecret ?? string.Empty,
            ["resource"] = resourceUri.ToString(),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint)
        {
            Content = requestContent
        };

        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            return null;
        }

        return await HandleSuccessfulTokenResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> InitiateAuthorizationCodeFlowAsync(
        ProtectedResourceMetadata protectedResourceMetadata,
        AuthorizationServerMetadata authServerMetadata,
        CancellationToken cancellationToken)
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        var authUrl = BuildAuthorizationUrl(protectedResourceMetadata, authServerMetadata, codeChallenge);
        var authCode = await _authorizationRedirectDelegate(authUrl, _redirectUri, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(authCode))
        {
            ThrowFailedToHandleUnauthorizedResponse($"The {nameof(AuthorizationRedirectDelegate)} returned a null or empty authorization code.");
        }

        return await ExchangeCodeForTokenAsync(protectedResourceMetadata, authServerMetadata, authCode!, codeVerifier, cancellationToken).ConfigureAwait(false);
    }

    private Uri BuildAuthorizationUrl(
        ProtectedResourceMetadata protectedResourceMetadata,
        AuthorizationServerMetadata authServerMetadata,
        string codeChallenge)
    {
        var resourceUri = GetRequiredResourceUri(protectedResourceMetadata);

        var queryParamsDictionary = new Dictionary<string, string>
        {
            ["client_id"] = GetClientIdOrThrow(),
            ["redirect_uri"] = _redirectUri.ToString(),
            ["response_type"] = "code",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["resource"] = resourceUri.ToString(),
        };

        var scope = GetScopeParameter(protectedResourceMetadata);
        if (!string.IsNullOrEmpty(scope))
        {
            queryParamsDictionary["scope"] = scope!;
        }

        // Add extra parameters if provided. Load into a dictionary before constructing to avoid overwiting values.
        foreach (var kvp in _additionalAuthorizationParameters)
        {
            queryParamsDictionary.Add(kvp.Key, kvp.Value);
        }

        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        foreach (var kvp in queryParamsDictionary)
        {
            queryParams[kvp.Key] = kvp.Value;
        }

        var uriBuilder = new UriBuilder(authServerMetadata.AuthorizationEndpoint)
        {
            Query = queryParams.ToString()
        };

        return uriBuilder.Uri;
    }

    private async Task<string> ExchangeCodeForTokenAsync(
        ProtectedResourceMetadata protectedResourceMetadata,
        AuthorizationServerMetadata authServerMetadata,
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var resourceUri = GetRequiredResourceUri(protectedResourceMetadata);

        var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = _redirectUri.ToString(),
            ["client_id"] = GetClientIdOrThrow(),
            ["code_verifier"] = codeVerifier,
            ["client_secret"] = _clientSecret ?? string.Empty,
            ["resource"] = resourceUri.ToString(),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint)
        {
            Content = requestContent
        };

        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();
        var tokenContainer = await HandleSuccessfulTokenResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
        return tokenContainer.AccessToken;
    }

    private async Task<TokenContainer> HandleSuccessfulTokenResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var tokenResponse = await JsonSerializer.DeserializeAsync(stream, McpJsonUtilities.JsonContext.Default.TokenResponse, cancellationToken).ConfigureAwait(false);

        if (tokenResponse is null)
        {
            ThrowFailedToHandleUnauthorizedResponse($"The token endpoint '{response.RequestMessage?.RequestUri}' returned an empty response.");
        }

        if (tokenResponse.TokenType is null || !string.Equals(tokenResponse.TokenType, BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            ThrowFailedToHandleUnauthorizedResponse($"The token endpoint '{response.RequestMessage?.RequestUri}' returned an unsupported token type: '{tokenResponse.TokenType ?? "<null>"}'. Only 'Bearer' tokens are supported.");
        }

        TokenContainer tokens = new()
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresIn = tokenResponse.ExpiresIn,
            TokenType = tokenResponse.TokenType,
            Scope = tokenResponse.Scope,
            ObtainedAt = DateTimeOffset.UtcNow,
        };

        await _tokenCache.StoreTokensAsync(tokens, cancellationToken).ConfigureAwait(false);

        return tokens;
    }

    private static Uri BuildProtectedResourceMetadataUri(Uri resourceUri)
    {
        var builder = new UriBuilder(resourceUri);
        var pathSuffix = resourceUri.AbsolutePath;
        if (pathSuffix.Length > 1)
        {
            builder.Path = $"{ProtectedResourceMetadataWellKnownPath}{pathSuffix.AsSpan().TrimEnd('/')}";
        }
        else
        {
            builder.Path = ProtectedResourceMetadataWellKnownPath;
        }

        return builder.Uri;
    }

    /// <summary>
    /// Fetches the protected resource metadata from the provided URL.
    /// </summary>
    /// <param name="metadataUrl">The URL to fetch the metadata from.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The fetched ProtectedResourceMetadata, or null if it couldn't be fetched.</returns>
    private async Task<ProtectedResourceMetadata?> FetchProtectedResourceMetadataAsync(Uri metadataUrl, CancellationToken cancellationToken = default)
    {
        using var httpResponse = await _httpClient.GetAsync(metadataUrl, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, McpJsonUtilities.JsonContext.Default.ProtectedResourceMetadata, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs dynamic client registration with the authorization server.
    /// </summary>
    private async Task PerformDynamicClientRegistrationAsync(
        ProtectedResourceMetadata protectedResourceMetadata,
        AuthorizationServerMetadata authServerMetadata,
        CancellationToken cancellationToken)
    {
        if (authServerMetadata.RegistrationEndpoint is null)
        {
            ThrowFailedToHandleUnauthorizedResponse("Authorization server does not support dynamic client registration");
        }

        LogPerformingDynamicClientRegistration(authServerMetadata.RegistrationEndpoint);

        var registrationRequest = new DynamicClientRegistrationRequest
        {
            RedirectUris = [_redirectUri.ToString()],
            GrantTypes = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"],
            TokenEndpointAuthMethod = "client_secret_post",
            ClientName = _dcrClientName,
            ClientUri = _dcrClientUri?.ToString(),
            Scope = GetScopeParameter(protectedResourceMetadata),
        };

        var requestJson = JsonSerializer.Serialize(registrationRequest, McpJsonUtilities.JsonContext.Default.DynamicClientRegistrationRequest);
        using var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.RegistrationEndpoint)
        {
            Content = requestContent
        };

        if (!string.IsNullOrEmpty(_dcrInitialAccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, _dcrInitialAccessToken);
        }

        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ThrowFailedToHandleUnauthorizedResponse($"Dynamic client registration failed with status {httpResponse.StatusCode}: {errorContent}");
        }

        using var responseStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var registrationResponse = await JsonSerializer.DeserializeAsync(
            responseStream,
            McpJsonUtilities.JsonContext.Default.DynamicClientRegistrationResponse,
            cancellationToken).ConfigureAwait(false);

        if (registrationResponse is null)
        {
            ThrowFailedToHandleUnauthorizedResponse("Dynamic client registration returned empty response");
        }

        // Update client credentials
        _clientId = registrationResponse.ClientId;
        if (!string.IsNullOrEmpty(registrationResponse.ClientSecret))
        {
            _clientSecret = registrationResponse.ClientSecret;
        }

        LogDynamicClientRegistrationSuccessful(_clientId!);

        if (_dcrResponseDelegate is not null)
        {
            await _dcrResponseDelegate(registrationResponse, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies that the resource URI in the metadata exactly matches the original request URL as required by the RFC.
    /// Per RFC: The resource value must be identical to the URL that the client used to make the request to the resource server.
    /// </summary>
    /// <param name="protectedResourceMetadata">The metadata to verify.</param>
    /// <param name="resourceLocation">The original URL the client used to make the request to the resource server.</param>
    /// <returns>True if the resource URI exactly matches the original request URL, otherwise false.</returns>
    private static bool VerifyResourceMatch(ProtectedResourceMetadata protectedResourceMetadata, Uri resourceLocation)
    {
        if (protectedResourceMetadata.Resource is null || resourceLocation is null)
        {
            return false;
        }

        // Per RFC: The resource value must be identical to the URL that the client used
        // to make the request to the resource server. Compare entire URIs, not just the host.

        // Normalize the URIs to ensure consistent comparison
        string normalizedMetadataResource = NormalizeUri(protectedResourceMetadata.Resource);
        string normalizedResourceLocation = NormalizeUri(resourceLocation);

        return string.Equals(normalizedMetadataResource, normalizedResourceLocation, StringComparison.OrdinalIgnoreCase);
    }

    private static Uri GetRequiredResourceUri(ProtectedResourceMetadata protectedResourceMetadata)
    {
        if (protectedResourceMetadata.Resource is null)
        {
            ThrowFailedToHandleUnauthorizedResponse("Protected resource metadata did not include a 'resource' value.");
        }

        return protectedResourceMetadata.Resource;
    }

    /// <summary>
    /// Normalizes a URI for consistent comparison.
    /// </summary>
    /// <param name="uri">The URI to normalize.</param>
    /// <returns>A normalized string representation of the URI.</returns>
    private static string NormalizeUri(Uri uri)
    {
        var builder = new StringBuilder();
        builder.Append(uri.Scheme);
        builder.Append("://");
        builder.Append(uri.Host);

        if (!uri.IsDefaultPort)
        {
            builder.Append(':');
            builder.Append(uri.Port);
        }

        builder.Append(uri.AbsolutePath.TrimEnd('/'));

        if (!string.IsNullOrEmpty(uri.Query))
        {
            builder.Append(uri.Query);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Responds to a 401 challenge by parsing the WWW-Authenticate header, fetching the resource metadata,
    /// verifying the resource match, and returning the metadata if valid.
    /// </summary>
    /// <param name="response">The HTTP response containing the WWW-Authenticate header.</param>
    /// <param name="serverUrl">The server URL to verify against the resource metadata.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The resource metadata if the resource matches the server, otherwise throws an exception.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the response is not a 401, the metadata can't be fetched, or the resource URI doesn't match the server URL.</exception>
    private async Task<ProtectedResourceMetadata> ExtractProtectedResourceMetadata(HttpResponseMessage response, Uri serverUrl, CancellationToken cancellationToken = default)
    {
        Uri metadataUri;
        string? wwwAuthenticateScope = null;
        string? resourceMetadataUrl = null;

        // Look for the Bearer authentication scheme with resource_metadata and/or scope parameters.
        foreach (var header in response.Headers.WwwAuthenticate)
        {
            if (string.Equals(header.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(header.Parameter))
            {
                resourceMetadataUrl = ParseWwwAuthenticateParameters(header.Parameter, "resource_metadata");

                // "Use scope parameter from the initial WWW-Authenticate header in the 401 response, if provided."
                // https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization#scope-selection-strategy
                //
                // We use the scope even if resource_metadata is not present so long as it's for the Bearer scheme,
                // since we do not require a resource_metadata parameter.
                wwwAuthenticateScope ??= ParseWwwAuthenticateParameters(header.Parameter, "scope");

                if (resourceMetadataUrl is not null)
                {
                    break;
                }
            }
        }

        if (resourceMetadataUrl is null)
        {
            metadataUri = BuildProtectedResourceMetadataUri(serverUrl);
            LogMissingResourceMetadataParameter(metadataUri);
        }
        else
        {
            metadataUri = new(resourceMetadataUrl);
        }

        var metadata = await FetchProtectedResourceMetadataAsync(metadataUri, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException($"Failed to fetch resource metadata from {metadataUri}");

        metadata.WwwAuthenticateScope = wwwAuthenticateScope;

        // Per RFC: The resource value must be identical to the URL that the client used
        // to make the request to the resource server
        LogValidatingResourceMetadata(serverUrl);

        if (!VerifyResourceMatch(metadata, serverUrl))
        {
            throw new McpException($"Resource URI in metadata ({metadata.Resource}) does not match the expected URI ({serverUrl})");
        }

        return metadata;
    }

    /// <summary>
    /// Parses the WWW-Authenticate header parameters to extract a specific parameter.
    /// </summary>
    /// <param name="parameters">The parameter string from the WWW-Authenticate header.</param>
    /// <param name="parameterName">The name of the parameter to extract.</param>
    /// <returns>The value of the parameter, or null if not found.</returns>
    private static string? ParseWwwAuthenticateParameters(string parameters, string parameterName)
    {
        if (parameters.IndexOf(parameterName, StringComparison.OrdinalIgnoreCase) == -1)
        {
            return null;
        }

        foreach (var part in parameters.Split(','))
        {
            var trimmedPart = part.AsSpan().Trim();
            int equalsIndex = trimmedPart.IndexOf('=');

            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = trimmedPart[..equalsIndex].Trim();

            if (key.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmedPart[(equalsIndex + 1)..].Trim();
                if (value.Length > 0 && value[0] == '"' && value[^1] == '"')
                {
                    value = value[1..^1];
                }

                return value.ToString();
            }
        }

        return null;
    }

    private string? GetScopeParameter(ProtectedResourceMetadata protectedResourceMetadata)
    {
        if (!string.IsNullOrEmpty(protectedResourceMetadata.WwwAuthenticateScope))
        {
            return protectedResourceMetadata.WwwAuthenticateScope;
        }
        else if (protectedResourceMetadata.ScopesSupported.Count > 0)
        {
            return string.Join(" ", protectedResourceMetadata.ScopesSupported);
        }

        return _configuredScopes;
    }

    private static string GenerateCodeVerifier()
    {
#if NET9_0_OR_GREATER
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.EncodeToString(bytes);
#else
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return ToBase64UrlString(bytes);
#endif
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
#if NET9_0_OR_GREATER
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier), hash);
        return Base64Url.EncodeToString(hash);
#else
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return ToBase64UrlString(challengeBytes);
#endif
    }

#if !NET9_0_OR_GREATER
    private static string ToBase64UrlString(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
#endif

    private string GetClientIdOrThrow() => _clientId ?? throw new InvalidOperationException("Client ID is not available. This may indicate an issue with dynamic client registration.");

    [DoesNotReturn]
    private static void ThrowFailedToHandleUnauthorizedResponse(string message) =>
        throw new McpException($"Failed to handle unauthorized response with 'Bearer' scheme. {message}");

    [LoggerMessage(Level = LogLevel.Information, Message = "Selected authorization server: {Server} from {Count} available servers")]
    partial void LogSelectedAuthorizationServer(Uri server, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "OAuth authorization completed successfully")]
    partial void LogOAuthAuthorizationCompleted();

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching auth server metadata from {Endpoint}")]
    partial void LogErrorFetchingAuthServerMetadata(Exception ex, Uri endpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Performing dynamic client registration with {RegistrationEndpoint}")]
    partial void LogPerformingDynamicClientRegistration(Uri registrationEndpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamic client registration successful. Client ID: {ClientId}")]
    partial void LogDynamicClientRegistrationSuccessful(string clientId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Validating resource metadata against original server URL: {ServerUrl}")]
    partial void LogValidatingResourceMetadata(Uri serverUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WWW-Authenticate header missing.")]
    partial void LogMissingWwwAuthenticateHeader();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Missing resource_metadata parameter from WWW-Authenticate header. Falling back to {MetadataUri}")]
    partial void LogMissingResourceMetadataParameter(Uri metadataUri);
}
