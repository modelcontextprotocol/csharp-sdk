using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// A generic implementation of an OAuth authorization provider for MCP. This does not do any advanced token
/// protection or caching - it acquires a token and server metadata and holds it in memory.
/// This is suitable for demonstration and development purposes.
/// </summary>
internal sealed partial class ClientOAuthProvider
{
    /// <summary>
    /// The Bearer authentication scheme.
    /// </summary>
    private const string BearerScheme = "Bearer";

    private readonly Uri _serverUrl;
    private readonly Uri _redirectUri;
    private readonly string[]? _scopes;
    private readonly IDictionary<string, string> _additionalAuthorizationParameters;
    private readonly Func<IReadOnlyList<Uri>, Uri?> _authServerSelector;
    private readonly AuthorizationRedirectDelegate _authorizationRedirectDelegate;

    // _clientName and _client URI is used for dynamic client registration (RFC 7591)
    private readonly string? _clientName;
    private readonly Uri? _clientUri;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    private string? _clientId;
    private string? _clientSecret;

    private TokenContainer? _token;
    private AuthorizationServerMetadata? _authServerMetadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientOAuthProvider"/> class using the specified options.
    /// </summary>
    /// <param name="serverUrl">The MCP server URL.</param>
    /// <param name="options">The OAuth provider configuration options.</param>
    /// <param name="httpClient">The HTTP client to use for OAuth requests. If null, a default HttpClient will be used.</param>
    /// <param name="loggerFactory">A logger factory to handle diagnostic messages.</param>
    /// <exception cref="ArgumentNullException">Thrown when serverUrl or options are null.</exception>
    public ClientOAuthProvider(
        Uri serverUrl,
        ClientOAuthOptions options,
        HttpClient? httpClient = null,
        ILoggerFactory? loggerFactory = null)
    {
        _serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
        _httpClient = httpClient ?? new HttpClient();
        _logger = (ILogger?)loggerFactory?.CreateLogger<ClientOAuthProvider>() ?? NullLogger.Instance;

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _clientId = options.ClientId;
        _clientSecret = options.ClientSecret;
        _redirectUri = options.RedirectUri ?? throw new ArgumentException("ClientOAuthOptions.RedirectUri must configured.");
        _clientName = options.ClientName;
        _clientUri = options.ClientUri;
        _scopes = options.Scopes?.ToArray();
        _additionalAuthorizationParameters = options.AdditionalAuthorizationParameters;

        // Set up authorization server selection strategy
        _authServerSelector = options.AuthServerSelector ?? DefaultAuthServerSelector;

        // Set up authorization URL handler (use default if not provided)
        _authorizationRedirectDelegate = options.AuthorizationRedirectDelegate ?? DefaultAuthorizationUrlHandler;
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
    /// <param name="cancellationToken">The cancellation token.</param>
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

    /// <summary>
    /// Gets the collection of authentication schemes supported by this provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property returns all authentication schemes that this provider can handle,
    /// allowing clients to select the appropriate scheme based on server capabilities.
    /// </para>
    /// <para>
    /// Common values include "Bearer" for JWT tokens, "Basic" for username/password authentication,
    /// and "Negotiate" for integrated Windows authentication.
    /// </para>
    /// </remarks>
    public IEnumerable<string> SupportedSchemes => [BearerScheme];

    /// <summary>
    /// Gets an authentication token or credential for authenticating requests to a resource
    /// using the specified authentication scheme.
    /// </summary>
    /// <param name="scheme">The authentication scheme to use.</param>
    /// <param name="resourceUri">The URI of the resource requiring authentication.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An authentication token string or null if no token could be obtained for the specified scheme.</returns>
    public async Task<string?> GetCredentialAsync(string scheme, Uri resourceUri, CancellationToken cancellationToken = default)
    {
        ThrowIfNotBearerScheme(scheme);

        // Return the token if it's valid
        if (_token != null && _token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _token.AccessToken;
        }

        // Try to refresh the token if we have a refresh token
        if (_token?.RefreshToken != null && _authServerMetadata != null)
        {
            var newToken = await RefreshTokenAsync(_token.RefreshToken, resourceUri, _authServerMetadata, cancellationToken).ConfigureAwait(false);
            if (newToken != null)
            {
                _token = newToken;
                return _token.AccessToken;
            }
        }

        // No valid token - auth handler will trigger the 401 flow
        return null;
    }

    /// <summary>
    /// Handles a 401 Unauthorized response from a resource.
    /// </summary>
    /// <param name="scheme">The authentication scheme that was used when the unauthorized response was received.</param>
    /// <param name="response">The HTTP response that contained the 401 status code.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A result object indicating if the provider was able to handle the unauthorized response,
    /// and the authentication scheme that should be used for the next attempt, if any.
    /// </returns>
    public async Task HandleUnauthorizedResponseAsync(
        string scheme,
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        // This provider only supports Bearer scheme
        if (!string.Equals(scheme, BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This credential provider only supports the Bearer scheme");
        }

        await PerformOAuthAuthorizationAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs OAuth authorization by selecting an appropriate authorization server and completing the OAuth flow.
    /// </summary>
    /// <param name="response">The 401 Unauthorized response containing authentication challenge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether authorization was successful.</returns>
    private async Task PerformOAuthAuthorizationAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Get available authorization servers from the 401 response
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

        if (authServerMetadata is null)
        {
            ThrowFailedToHandleUnauthorizedResponse($"Failed to retrieve metadata for authorization server: '{selectedAuthServer}'");
        }

        // Store auth server metadata for future refresh operations
        _authServerMetadata = authServerMetadata;

        // Perform dynamic client registration if needed
        if (string.IsNullOrEmpty(_clientId))
        {
            await PerformDynamicClientRegistrationAsync(authServerMetadata, cancellationToken).ConfigureAwait(false);
        }

        // Perform the OAuth flow
        var token = await InitiateAuthorizationCodeFlowAsync(protectedResourceMetadata, authServerMetadata, cancellationToken).ConfigureAwait(false);

        if (token is null)
        {
            ThrowFailedToHandleUnauthorizedResponse($"The {nameof(AuthorizationRedirectDelegate)} returned a null or empty token.");
        }

        _token = token;
        LogOAuthAuthorizationCompleted();
    }

    private async Task<AuthorizationServerMetadata?> GetAuthServerMetadataAsync(Uri authServerUri, CancellationToken cancellationToken)
    {
        if (!authServerUri.OriginalString.EndsWith("/"))
        {
            authServerUri = new Uri(authServerUri.OriginalString + "/");
        }

        foreach (var path in new[] { ".well-known/openid-configuration", ".well-known/oauth-authorization-server" })
        {
            try
            {
                var response = await _httpClient.GetAsync(new Uri(authServerUri, path), cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var metadata = await JsonSerializer.DeserializeAsync(stream, McpJsonUtilities.JsonContext.Default.AuthorizationServerMetadata, cancellationToken).ConfigureAwait(false);

                if (metadata != null)
                {
                    metadata.ResponseTypesSupported ??= ["code"];
                    metadata.GrantTypesSupported ??= ["authorization_code", "refresh_token"];
                    metadata.TokenEndpointAuthMethodsSupported ??= ["client_secret_post"];
                    metadata.CodeChallengeMethodsSupported ??= ["S256"];

                    return metadata;
                }
            }
            catch (Exception ex)
            {
                LogErrorFetchingAuthServerMetadata(ex, path);
            }
        }

        return null;
    }

    private async Task<TokenContainer> RefreshTokenAsync(string refreshToken, Uri resourceUri, AuthorizationServerMetadata authServerMetadata, CancellationToken cancellationToken)
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

        return await FetchTokenAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TokenContainer?> InitiateAuthorizationCodeFlowAsync(
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
            return null;
        }

        return await ExchangeCodeForTokenAsync(protectedResourceMetadata, authServerMetadata, authCode!, codeVerifier, cancellationToken).ConfigureAwait(false);
    }

    private Uri BuildAuthorizationUrl(
        ProtectedResourceMetadata protectedResourceMetadata,
        AuthorizationServerMetadata authServerMetadata,
        string codeChallenge)
    {
        if (authServerMetadata.AuthorizationEndpoint.Scheme != Uri.UriSchemeHttp &&
            authServerMetadata.AuthorizationEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("AuthorizationEndpoint must use HTTP or HTTPS.", nameof(authServerMetadata));
        }

        var queryParamsDictionary = new Dictionary<string, string>
        {
            ["client_id"] = GetClientIdOrThrow(),
            ["redirect_uri"] = _redirectUri.ToString(),
            ["response_type"] = "code",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["resource"] = protectedResourceMetadata.Resource.ToString(),
        };

        var scopesSupported = protectedResourceMetadata.ScopesSupported;
        if (_scopes is not null || scopesSupported.Count > 0)
        {
            queryParamsDictionary["scope"] = string.Join(" ", _scopes ?? scopesSupported.ToArray());
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

    private async Task<TokenContainer> ExchangeCodeForTokenAsync(
        ProtectedResourceMetadata protectedResourceMetadata,
        AuthorizationServerMetadata authServerMetadata,
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = _redirectUri.ToString(),
            ["client_id"] = GetClientIdOrThrow(),
            ["code_verifier"] = codeVerifier,
            ["client_secret"] = _clientSecret ?? string.Empty,
            ["resource"] = protectedResourceMetadata.Resource.ToString(),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.TokenEndpoint)
        {
            Content = requestContent
        };

        return await FetchTokenAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TokenContainer> FetchTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var tokenResponse = await JsonSerializer.DeserializeAsync(stream, McpJsonUtilities.JsonContext.Default.TokenContainer, cancellationToken).ConfigureAwait(false);

        if (tokenResponse is null)
        {
            ThrowFailedToHandleUnauthorizedResponse($"The token endpoint '{request.RequestUri}' returned an empty response.");
        }

        tokenResponse.ObtainedAt = DateTimeOffset.UtcNow;
        return tokenResponse;
    }

    /// <summary>
    /// Fetches the protected resource metadata from the provided URL.
    /// </summary>
    /// <param name="metadataUrl">The URL to fetch the metadata from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
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
    /// <param name="authServerMetadata">The authorization server metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PerformDynamicClientRegistrationAsync(
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
            ClientName = _clientName,
            ClientUri = _clientUri?.ToString(),
            Scope = _scopes is not null ? string.Join(" ", _scopes) : null
        };

        var requestJson = JsonSerializer.Serialize(registrationRequest, McpJsonUtilities.JsonContext.Default.DynamicClientRegistrationRequest);
        using var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, authServerMetadata.RegistrationEndpoint)
        {
            Content = requestContent
        };

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
        if (protectedResourceMetadata.Resource == null || resourceLocation == null)
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
        var builder = new UriBuilder(uri)
        {
            Port = -1  // Always remove port
        };

        if (builder.Path == "/")
        {
            builder.Path = string.Empty;
        }
        else if (builder.Path.Length > 1 && builder.Path.EndsWith("/"))
        {
            builder.Path = builder.Path.TrimEnd('/');
        }

        return builder.Uri.ToString();
    }

    /// <summary>
    /// Responds to a 401 challenge by parsing the WWW-Authenticate header, fetching the resource metadata,
    /// verifying the resource match, and returning the metadata if valid.
    /// </summary>
    /// <param name="response">The HTTP response containing the WWW-Authenticate header.</param>
    /// <param name="serverUrl">The server URL to verify against the resource metadata.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resource metadata if the resource matches the server, otherwise throws an exception.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the response is not a 401, lacks a WWW-Authenticate header,
    /// lacks a resource_metadata parameter, the metadata can't be fetched, or the resource URI doesn't match the server URL.</exception>
    private async Task<ProtectedResourceMetadata> ExtractProtectedResourceMetadata(HttpResponseMessage response, Uri serverUrl, CancellationToken cancellationToken = default)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException($"Expected a 401 Unauthorized response, but received {(int)response.StatusCode} {response.StatusCode}");
        }

        // Extract the WWW-Authenticate header
        if (response.Headers.WwwAuthenticate.Count == 0)
        {
            throw new McpException("The 401 response does not contain a WWW-Authenticate header");
        }

        // Look for the Bearer authentication scheme with resource_metadata parameter
        string? resourceMetadataUrl = null;
        foreach (var header in response.Headers.WwwAuthenticate)
        {
            if (string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(header.Parameter))
            {
                resourceMetadataUrl = ParseWwwAuthenticateParameters(header.Parameter, "resource_metadata");
                if (resourceMetadataUrl != null)
                {
                    break;
                }
            }
        }

        if (resourceMetadataUrl == null)
        {
            throw new McpException("The WWW-Authenticate header does not contain a resource_metadata parameter");
        }

        Uri metadataUri = new(resourceMetadataUrl);
        var metadata = await FetchProtectedResourceMetadataAsync(metadataUri, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException($"Failed to fetch resource metadata from {resourceMetadataUrl}");

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
            string trimmedPart = part.Trim();
            int equalsIndex = trimmedPart.IndexOf('=');

            if (equalsIndex <= 0)
            {
                continue;
            }

            string key = trimmedPart.Substring(0, equalsIndex).Trim();

            if (string.Equals(key, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                string value = trimmedPart.Substring(equalsIndex + 1).Trim();

                if (value.StartsWith("\"") && value.EndsWith("\""))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                return value;
            }
        }

        return null;
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return Convert.ToBase64String(challengeBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private string GetClientIdOrThrow() => _clientId ?? throw new InvalidOperationException("Client ID is not available. This may indicate an issue with dynamic client registration.");

    private static void ThrowIfNotBearerScheme(string scheme)
    {
        if (!string.Equals(scheme, BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The '{scheme}' is not supported. This credential provider only supports the '{BearerScheme}' scheme");
        }
    }

    [DoesNotReturn]
    private static void ThrowFailedToHandleUnauthorizedResponse(string message) =>
        throw new McpException($"Failed to handle unauthorized response with 'Bearer' scheme. {message}");

    [LoggerMessage(Level = LogLevel.Information, Message = "Selected authorization server: {Server} from {Count} available servers")]
    partial void LogSelectedAuthorizationServer(Uri server, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "OAuth authorization completed successfully")]
    partial void LogOAuthAuthorizationCompleted();

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching auth server metadata from {Path}")]
    partial void LogErrorFetchingAuthServerMetadata(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Performing dynamic client registration with {RegistrationEndpoint}")]
    partial void LogPerformingDynamicClientRegistration(Uri registrationEndpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamic client registration successful. Client ID: {ClientId}")]
    partial void LogDynamicClientRegistrationSuccessful(string clientId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Validating resource metadata against original server URL: {ServerUrl}")]
    partial void LogValidatingResourceMetadata(Uri serverUrl);
}
