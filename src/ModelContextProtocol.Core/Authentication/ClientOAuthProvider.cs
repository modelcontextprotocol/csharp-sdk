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

    // JWT client assertion support (private_key_jwt)
    private readonly string? _jwtPrivateKeyPem;
    private readonly string? _jwtSigningAlgorithm;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    private string? _clientId;
    private string? _clientSecret;
    private ITokenCache _tokenCache;
    private AuthorizationServerMetadata? _authServerMetadata;
    private int _repeatedAuthFailureCount;

    /// <summary>
    /// Maximum number of repeated auth failure retries before failing.
    /// This prevents infinite loops when tokens are never accepted by the server.
    /// </summary>
    private const int MaxRepeatedAuthFailures = 3;

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

        // JWT client assertion support
        _jwtPrivateKeyPem = options.JwtPrivateKeyPem;
        _jwtSigningAlgorithm = options.JwtSigningAlgorithm;

        // Validate JWT signing algorithm if provided
        if (_jwtSigningAlgorithm is not null &&
            !s_jwtSigningAlgorithms.Contains(_jwtSigningAlgorithm))
        {
            throw new ArgumentException($"JWT signing algorithm '{_jwtSigningAlgorithm}' is not supported.", nameof(options));
        }
    }

    private static readonly HashSet<string> s_jwtSigningAlgorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        "ES256", "ES384", "ES512", 
        "RS256", "RS384", "RS512", 
        "PS256", "PS384", "PS512"
    };

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
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
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
        bool attemptedRefresh = false;

        if (request.Headers.Authorization is null && request.RequestUri is not null)
        {
            string? accessToken;
            (accessToken, attemptedRefresh) = await GetAccessTokenSilentAsync(request.RequestUri, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
            }
        }

        var response = await base.SendAsync(request, message, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            HasInsufficientScopeError(response))
        {
            return await HandleUnauthorizedResponseAsync(request, message, response, attemptedRefresh, cancellationToken).ConfigureAwait(false);
        }

        // Reset the auth failure counter on successful request
        _repeatedAuthFailureCount = 0;
        return response;
    }

    private async Task<(string? AccessToken, bool AttemptedRefresh)> GetAccessTokenSilentAsync(Uri resourceUri, CancellationToken cancellationToken)
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
            var accessToken = await RefreshTokensAsync(refreshToken, resourceUri, _authServerMetadata, cancellationToken).ConfigureAwait(false);
            return (accessToken, true);
        }

        // No valid token - auth handler will trigger the 401 flow
        return (null, false);
    }

    /// <summary>
    /// Checks if the response contains an insufficient_scope error (403 Forbidden with error=insufficient_scope).
    /// </summary>
    private static bool HasInsufficientScopeError(HttpResponseMessage response)
    {
        // Only 403 Forbidden responses can have insufficient_scope error
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
        bool attemptedRefresh,
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

        var accessToken = await GetAccessTokenAsync(response, attemptedRefresh, cancellationToken).ConfigureAwait(false);

        using var retryRequest = new HttpRequestMessage(originalRequest.Method, originalRequest.RequestUri);

        foreach (var header in originalRequest.Headers)
        {
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        retryRequest.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);

        // Use SendAsync (not base.SendAsync) to enable retry logic for scope step-up scenarios
        // where the server may respond with 403 (insufficient_scope) multiple times
        return await SendAsync(retryRequest, originalJsonRpcMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a 401 Unauthorized or 403 Forbidden response from a resource by completing any required OAuth flows.
    /// </summary>
    /// <param name="response">The HTTP response that triggered the authentication challenge.</param>
    /// <param name="attemptedRefresh">Indicates whether a token refresh has already been attempted.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    private async Task<string> GetAccessTokenAsync(HttpResponseMessage response, bool attemptedRefresh, CancellationToken cancellationToken)
    {
        // Track all auth failures to prevent infinite redirect loops.
        // This counter is only reset when a request succeeds (in SendAsync).
        if (++_repeatedAuthFailureCount > MaxRepeatedAuthFailures)
        {
            ThrowFailedToHandleUnauthorizedResponse($"Maximum repeated authentication failure limit ({MaxRepeatedAuthFailures}) exceeded. The server may be rejecting all tokens.");
        }

        // Get available authorization servers from the 401 or 403 response
        var protectedResourceMetadata = await ExtractProtectedResourceMetadata(response, cancellationToken).ConfigureAwait(false);
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
        if (!attemptedRefresh &&
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await _tokenCache.GetTokensAsync(cancellationToken).ConfigureAwait(false) is { RefreshToken: { Length: > 0 } refreshToken })
        {
            var accessToken = await RefreshTokensAsync(refreshToken, resourceUri, authServerMetadata, cancellationToken).ConfigureAwait(false);
            if (accessToken is not null)
            {
                // A non-null result indicates the refresh succeeded and the new tokens have been stored.
                return accessToken;
            }
        }

        // Skip dynamic registration if we have pre-registered credentials (ClientId + ClientSecret)
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

        // Check if client_credentials grant type should be used.
        // Use client_credentials when:
        // 1. The server supports client_credentials grant type.
        // 2. We have a client secret (confidential client).
        // 3. No AuthorizationRedirectDelegate was explicitly provided (machine-to-machine flow).
        if (ShouldUseClientCredentialsGrant(authServerMetadata))
        {
            return await InitiateClientCredentialsFlowAsync(protectedResourceMetadata, authServerMetadata, cancellationToken).ConfigureAwait(false);
        }

        // Perform the OAuth authorization code flow
        return await InitiateAuthorizationCodeFlowAsync(protectedResourceMetadata, authServerMetadata, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether to use the client_credentials grant type.
    /// </summary>
    private bool ShouldUseClientCredentialsGrant(AuthorizationServerMetadata authServerMetadata)
    {
        // Must have either client secret or JWT private key for client_credentials.
        if (string.IsNullOrEmpty(_clientSecret) && string.IsNullOrEmpty(_jwtPrivateKeyPem))
        {
            return false;
        }

        // Server must support client_credentials grant type.
        if (authServerMetadata.GrantTypesSupported?.Contains("client_credentials") != true)
        {
            return false;
        }

        // If an authorization redirect delegate was explicitly configured, use authorization code flow
        // Default delegate is fine to override with client_credentials.
        if (_authorizationRedirectDelegate != DefaultAuthorizationUrlHandler)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Initiates the OAuth client_credentials flow for machine-to-machine authentication.
    /// </summary>
    private async Task<string> InitiateClientCredentialsFlowAsync(
        ProtectedResourceMetadata protectedResourceMetadata,
        AuthorizationServerMetadata authServerMetadata,
        CancellationToken cancellationToken)
    {
        var resourceUri = GetRequiredResourceUri(protectedResourceMetadata);

        var formParams = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["resource"] = resourceUri.ToString(),
        };

        var scope = GetScopeParameter(protectedResourceMetadata);
        if (!string.IsNullOrEmpty(scope))
        {
            formParams["scope"] = scope!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint);

        // Add client authentication based on available credentials and server support
        AddClientAuthentication(request, formParams, authServerMetadata);

        request.Content = new FormUrlEncodedContent(formParams);

        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var tokens = await HandleSuccessfulTokenResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
        LogOAuthClientCredentialsCompleted();
        return tokens.AccessToken;
    }

    /// <summary>
    /// Adds client authentication to the token request based on available credentials.
    /// </summary>
    private void AddClientAuthentication(
        HttpRequestMessage request,
        Dictionary<string, string> formParams,
        AuthorizationServerMetadata authServerMetadata)
    {
        // If JWT private key is configured, use private_key_jwt.
        if (!string.IsNullOrEmpty(_jwtPrivateKeyPem) && !string.IsNullOrEmpty(_jwtSigningAlgorithm))
        {
            // Use the issuer as the audience if available, otherwise fall back to token endpoint
            var audience = authServerMetadata.Issuer ?? authServerMetadata.TokenEndpoint!;
            var assertion = CreateClientAssertion(audience);
            formParams["client_id"] = GetClientIdOrThrow();
            formParams["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
            formParams["client_assertion"] = assertion;
            return;
        }

        // Otherwise use client_secret authentication.
        var tokenEndpointAuthMethod = GetTokenEndpointAuthMethod(authServerMetadata);

        if (tokenEndpointAuthMethod == "client_secret_basic")
        {
            // Use HTTP Basic authentication
            var credentials = $"{Uri.EscapeDataString(GetClientIdOrThrow())}:{Uri.EscapeDataString(_clientSecret ?? string.Empty)}";
            var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
        }
        else
        {
            // Use client_secret_post (credentials in body)
            formParams["client_id"] = GetClientIdOrThrow();
            formParams["client_secret"] = _clientSecret ?? string.Empty;
        }
    }

    /// <summary>
    /// Creates a JWT client assertion for private_key_jwt authentication.
    /// </summary>
    private string CreateClientAssertion(Uri audience)
    {
        // JWT claims (payload)
        var now = DateTimeOffset.UtcNow;
        var clientId = GetClientIdOrThrow();
        var jti = Guid.NewGuid().ToString();
        var iat = now.ToUnixTimeSeconds();
        var exp = now.AddMinutes(5).ToUnixTimeSeconds();

        // Manually construct JSON to avoid AOT/trimming issues with Dictionary<string,object>
        // Algorithm is validated in constructor to be one of the known safe values, so no escaping needed
        var headerJson = $@"{{""alg"":""{_jwtSigningAlgorithm!.ToUpperInvariant()}"",""typ"":""JWT""}}";
        var claimsJson = $@"{{""iss"":""{JsonEncodedString(clientId)}"",""sub"":""{JsonEncodedString(clientId)}"",""aud"":""{JsonEncodedString(audience.ToString())}"",""jti"":""{jti}"",""iat"":{iat},""exp"":{exp}}}";

        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var claimsBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(claimsJson));

        var signingInput = $"{headerBase64}.{claimsBase64}";
        var signature = SignJwt(signingInput);

        return $"{signingInput}.{signature}";
    }

    /// <summary>
    /// Escapes a string for JSON encoding.
    /// </summary>
    private static string JsonEncodedString(string value) => JsonEncodedText.Encode(value).ToString();

    /// <summary>
    /// Signs the JWT using the configured private key and algorithm.
    /// </summary>
    private string SignJwt(string input)
    {
#if NETSTANDARD2_0
        throw new NotSupportedException(
            "JWT client assertion (private_key_jwt) is not supported on .NET Standard 2.0. " +
            "Use .NET 5.0 or later for this feature.");
#else
        var data = Encoding.UTF8.GetBytes(input);

        var pemContent = _jwtPrivateKeyPem!;
        using AsymmetricAlgorithm key = _jwtSigningAlgorithm!.StartsWith("ES", StringComparison.OrdinalIgnoreCase) ?
            LoadKeyWithDisposal(ECDsa.Create, ecdsa => ecdsa.ImportFromPem(pemContent)) :
            LoadKeyWithDisposal(RSA.Create, rsa => rsa.ImportFromPem(pemContent));

        byte[] signature;

        if (_jwtSigningAlgorithm!.StartsWith("ES", StringComparison.OrdinalIgnoreCase))
        {
            // ECDSA signature - JWT requires IEEE P1363 format (R||S concatenation), not DER
            var ecdsa = key as ECDsa ?? throw new InvalidOperationException("Private key is not an EC key, but ES* algorithm was specified.");
            var hashAlgorithm = GetHashAlgorithmName(_jwtSigningAlgorithm);
            signature = ecdsa.SignData(data, hashAlgorithm, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        else if (_jwtSigningAlgorithm.StartsWith("RS", StringComparison.OrdinalIgnoreCase) ||
                 _jwtSigningAlgorithm.StartsWith("PS", StringComparison.OrdinalIgnoreCase))
        {
            // RSA signature
            var rsa = key as RSA ?? throw new InvalidOperationException("Private key is not an RSA key, but RS*/PS* algorithm was specified.");
            var hashAlgorithm = GetHashAlgorithmName(_jwtSigningAlgorithm);
            var padding = _jwtSigningAlgorithm.StartsWith("PS", StringComparison.OrdinalIgnoreCase)
                ? RSASignaturePadding.Pss
                : RSASignaturePadding.Pkcs1;
            signature = rsa.SignData(data, hashAlgorithm, padding);
        }
        else
        {
            throw new NotSupportedException($"JWT signing algorithm '{_jwtSigningAlgorithm}' is not supported.");
        }

        return Base64UrlEncode(signature);
#endif
    }

    private static TAlgorithm LoadKeyWithDisposal<TAlgorithm>(
        Func<TAlgorithm> createAlgorithm,
        Action<TAlgorithm> importAction)
        where TAlgorithm : AsymmetricAlgorithm
    {
        var algorithm = createAlgorithm();
        try
        {
            importAction(algorithm);
            return algorithm;
        }
        catch
        {
            algorithm.Dispose();
            throw;
        }
    }

    private static HashAlgorithmName GetHashAlgorithmName(string algorithm) =>
        s_signingAlgorithms.TryGetValue(algorithm, out HashAlgorithmName alg) ? alg :
        throw new NotSupportedException($"JWT signing algorithm '{algorithm}' is not supported.");

    private static readonly Dictionary<string, HashAlgorithmName> s_signingAlgorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ES256"] = HashAlgorithmName.SHA256,
        ["RS256"] = HashAlgorithmName.SHA256,
        ["PS256"] = HashAlgorithmName.SHA256,

        ["ES384"] = HashAlgorithmName.SHA384,
        ["RS384"] = HashAlgorithmName.SHA384,
        ["PS384"] = HashAlgorithmName.SHA384,

        ["ES512"] = HashAlgorithmName.SHA512,
        ["RS512"] = HashAlgorithmName.SHA512,
        ["PS512"] = HashAlgorithmName.SHA512,
    };

    /// <summary>
    /// Base64url encodes data per RFC 7515.
    /// </summary>
    private static string Base64UrlEncode(byte[] data) =>
#if NET9_0_OR_GREATER
        Base64Url.EncodeToString(data);
#else
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
#endif

    private void ApplyClientIdMetadataDocument(Uri metadataUri)
    {
        if (!IsValidClientMetadataDocumentUri(metadataUri))
        {
            ThrowFailedToHandleUnauthorizedResponse(
                $"{nameof(ClientOAuthOptions.ClientMetadataDocumentUri)} must be an HTTPS URL with a non-root absolute path. Value: '{metadataUri}'.");
        }

        _clientId = metadataUri.AbsoluteUri;

        // See: https://datatracker.ietf.org/doc/html/draft-ietf-oauth-client-id-metadata-document-00#section-3
        static bool IsValidClientMetadataDocumentUri(Uri uri) =>
            uri.IsAbsoluteUri &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.Length > 1; // AbsolutePath always starts with "/"
    }

    private async Task<AuthorizationServerMetadata> GetAuthServerMetadataAsync(Uri authServerUri, CancellationToken cancellationToken)
    {
        foreach (var wellKnownEndpoint in GetWellKnownAuthorizationServerMetadataUris(authServerUri))
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

    private static IEnumerable<Uri> GetWellKnownAuthorizationServerMetadataUris(Uri issuer)
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

    private async Task<string?> RefreshTokensAsync(string refreshToken, Uri resourceUri, AuthorizationServerMetadata authServerMetadata, CancellationToken cancellationToken)
    {
        var formParams = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["resource"] = resourceUri.ToString(),
        };

        // Add client credentials based on token endpoint auth method
        var tokenEndpointAuthMethod = GetTokenEndpointAuthMethod(authServerMetadata);

        using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint);

        if (tokenEndpointAuthMethod == "client_secret_basic")
        {
            // Use HTTP Basic authentication
            var credentials = $"{Uri.EscapeDataString(GetClientIdOrThrow())}:{Uri.EscapeDataString(_clientSecret ?? string.Empty)}";
            var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
        }
        else
        {
            // Use client_secret_post (credentials in body)
            formParams["client_id"] = GetClientIdOrThrow();
            formParams["client_secret"] = _clientSecret ?? string.Empty;
        }

        request.Content = new FormUrlEncodedContent(formParams);

        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            return null;
        }

        var tokens = await HandleSuccessfulTokenResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
        LogOAuthTokenRefreshCompleted();
        return tokens.AccessToken;
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

        var formParams = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = _redirectUri.ToString(),
            ["code_verifier"] = codeVerifier,
            ["resource"] = resourceUri.ToString(),
        };

        // Add client credentials based on token endpoint auth method
        var tokenEndpointAuthMethod = GetTokenEndpointAuthMethod(authServerMetadata);

        using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint);

        if (tokenEndpointAuthMethod == "client_secret_basic")
        {
            // Use HTTP Basic authentication
            var credentials = $"{Uri.EscapeDataString(GetClientIdOrThrow())}:{Uri.EscapeDataString(_clientSecret ?? string.Empty)}";
            var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
        }
        else
        {
            // Use client_secret_post (credentials in body)
            formParams["client_id"] = GetClientIdOrThrow();
            formParams["client_secret"] = _clientSecret ?? string.Empty;
        }

        request.Content = new FormUrlEncodedContent(formParams);

        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var tokens = await HandleSuccessfulTokenResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
        LogOAuthAuthorizationCompleted();
        return tokens.AccessToken;
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

    /// <summary>
    /// Fetches the protected resource metadata from the provided URL.
    /// </summary>
    private async Task<ProtectedResourceMetadata?> FetchProtectedResourceMetadataAsync(Uri metadataUrl, bool requireSuccess, CancellationToken cancellationToken)
    {
        using var httpResponse = await _httpClient.GetAsync(metadataUrl, cancellationToken).ConfigureAwait(false);
        if (requireSuccess)
        {
            httpResponse.EnsureSuccessStatusCode();
        }
        else if (!httpResponse.IsSuccessStatusCode)
        {
            return null;
        }

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

    private static Uri GetRequiredResourceUri(ProtectedResourceMetadata protectedResourceMetadata)
    {
        if (protectedResourceMetadata.Resource is null)
        {
            ThrowFailedToHandleUnauthorizedResponse("Protected resource metadata did not include a 'resource' value.");
        }

        return protectedResourceMetadata.Resource;
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

    /// <summary>
    /// Verifies that the resource URI in the metadata exactly matches the original request URL as required by the RFC.
    /// Per RFC: The resource value must be identical to the URL that the client used to make the request to the resource server.
    /// </summary>
    /// <param name="protectedResourceMetadata">The metadata to verify.</param>
    /// <param name="resourceLocation">
    /// The original URL the client used to make the request to the resource server or the root Uri for the resource server
    /// if the metadata was automatically requested from the root well-known location.
    /// </param>
    /// <returns>True if the resource URI exactly matches the original request URL, otherwise false.</returns>
    private static bool VerifyResourceMatch(ProtectedResourceMetadata protectedResourceMetadata, Uri resourceLocation)
    {
        if (protectedResourceMetadata.Resource is null)
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
        return builder.ToString();
    }

    /// <summary>
    /// Responds to a 401 challenge by parsing the WWW-Authenticate header, fetching the resource metadata,
    /// verifying the resource match, and returning the metadata if valid.
    /// </summary>
    /// <param name="response">The HTTP response containing the WWW-Authenticate header.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The resource metadata if the resource matches the server, otherwise throws an exception.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the response is not a 401, the metadata can't be fetched, or the resource URI doesn't match the server URL.</exception>
    private async Task<ProtectedResourceMetadata> ExtractProtectedResourceMetadata(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Uri resourceUri = _serverUrl;
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

        ProtectedResourceMetadata? metadata = null;

        if (resourceMetadataUrl is not null)
        {
            metadata = await FetchProtectedResourceMetadataAsync(new(resourceMetadataUrl), requireSuccess: true, cancellationToken).ConfigureAwait(false)
                ?? throw new McpException($"Failed to fetch resource metadata from {resourceMetadataUrl}");
        }
        else
        {
            foreach (var (wellKnownUri, expectedResourceUri) in GetWellKnownResourceMetadataUris(_serverUrl))
            {
                LogMissingResourceMetadataParameter(wellKnownUri);
                metadata = await FetchProtectedResourceMetadataAsync(wellKnownUri, requireSuccess: false, cancellationToken).ConfigureAwait(false);
                if (metadata is not null)
                {
                    resourceUri = expectedResourceUri;
                    break;
                }
            }

            if (metadata is null)
            {
                throw new McpException($"Failed to find protected resource metadata at a well-known location for {_serverUrl}");
            }
        }

        // The WWW-Authenticate header parameter should be preferred over using the scopes_supported metadata property.
        // https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization#protected-resource-metadata-discovery-requirements
        metadata.WwwAuthenticateScope = wwwAuthenticateScope;

        // Per RFC: The resource value must be identical to the URL that the client used to make the request to the resource server
        LogValidatingResourceMetadata(resourceUri);

        if (!VerifyResourceMatch(metadata, resourceUri))
        {
            throw new McpException($"Resource URI in metadata ({metadata.Resource}) does not match the expected URI ({resourceUri})");
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

    private static IEnumerable<(Uri WellKnownUri, Uri ExpectedResourceUri)> GetWellKnownResourceMetadataUris(Uri resourceUri)
    {
        var builder = new UriBuilder(resourceUri);
        var hostBase = builder.Uri.GetLeftPart(UriPartial.Authority);
        var trimmedPath = builder.Path?.Trim('/') ?? string.Empty;

        if (!string.IsNullOrEmpty(trimmedPath))
        {
            yield return (new Uri($"{hostBase}{ProtectedResourceMetadataWellKnownPath}/{trimmedPath}"), resourceUri);
        }

        yield return (new Uri($"{hostBase}{ProtectedResourceMetadataWellKnownPath}"), new Uri(hostBase));
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

    /// <summary>
    /// Determines the token endpoint authentication method to use based on server metadata.
    /// </summary>
    /// <param name="authServerMetadata">The authorization server metadata.</param>
    /// <returns>The authentication method to use (client_secret_basic or client_secret_post).</returns>
    private static string GetTokenEndpointAuthMethod(AuthorizationServerMetadata authServerMetadata)
    {
        var supportedMethods = authServerMetadata.TokenEndpointAuthMethodsSupported;

        // If client_secret_basic is supported, prefer it
        if (supportedMethods?.Contains("client_secret_basic") == true)
        {
            return "client_secret_basic";
        }

        // Otherwise use client_secret_post (default per RFC)
        return "client_secret_post";
    }

    [DoesNotReturn]
    private static void ThrowFailedToHandleUnauthorizedResponse(string message) =>
        throw new McpException($"Failed to handle unauthorized response with 'Bearer' scheme. {message}");

    [LoggerMessage(Level = LogLevel.Information, Message = "Selected authorization server: {Server} from {Count} available servers")]
    partial void LogSelectedAuthorizationServer(Uri server, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "OAuth authorization completed successfully")]
    partial void LogOAuthAuthorizationCompleted();

    [LoggerMessage(Level = LogLevel.Information, Message = "OAuth client_credentials flow completed successfully")]
    partial void LogOAuthClientCredentialsCompleted();

    [LoggerMessage(Level = LogLevel.Information, Message = "OAuth token refresh completed successfully")]
    partial void LogOAuthTokenRefreshCompleted();

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
