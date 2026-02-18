using System.Net.Http.Headers;
using System.Text.Json;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides Enterprise Managed Authorization utilities for the Identity Assertion Authorization Grant flow (SEP-990).
/// </summary>
/// <remarks>
/// <para>
/// This class provides standalone functions for:
/// </para>
/// <list type="bullet">
/// <item><description>RFC 8693 Token Exchange: Exchange an ID token for a JWT Authorization Grant (JAG) at an Identity Provider</description></item>
/// <item><description>RFC 7523 JWT Bearer Grant: Exchange a JAG for an access token at an MCP Server's authorization server</description></item>
/// </list>
/// <para>
/// These utilities can be used directly or through the <see cref="EnterpriseAuthProvider"/> for full integration
/// with the MCP client's OAuth infrastructure.
/// </para>
/// </remarks>
public static class EnterpriseAuth
{
    #region Constants

    /// <summary>
    /// Grant type URN for RFC 8693 token exchange.
    /// </summary>
    public const string GrantTypeTokenExchange = "urn:ietf:params:oauth:grant-type:token-exchange";

    /// <summary>
    /// Grant type URN for RFC 7523 JWT Bearer authorization grant.
    /// </summary>
    public const string GrantTypeJwtBearer = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    /// <summary>
    /// Token type URN for OpenID Connect ID Tokens (RFC 8693).
    /// </summary>
    public const string TokenTypeIdToken = "urn:ietf:params:oauth:token-type:id_token";

    /// <summary>
    /// Token type URN for SAML 2.0 assertions (RFC 8693).
    /// </summary>
    public const string TokenTypeSaml2 = "urn:ietf:params:oauth:token-type:saml2";

    /// <summary>
    /// Token type URN for Identity Assertion JWT Authorization Grants (SEP-990).
    /// </summary>
    public const string TokenTypeIdJag = "urn:ietf:params:oauth:token-type:id-jag";

    /// <summary>
    /// The expected value for <c>token_type</c> in a JAG token exchange response per RFC 8693 §2.2.1.
    /// The issued token is not an OAuth access token, so its type is "N_A".
    /// </summary>
    public const string TokenTypeNotApplicable = "N_A";

    #endregion

    #region Layer 2: Token Exchange (RFC 8693)

    /// <summary>
    /// Requests a JWT Authorization Grant (JAG) from an Identity Provider via RFC 8693 Token Exchange.
    /// Returns the JAG string to be used as a JWT Bearer assertion (RFC 7523) against the MCP authorization server.
    /// </summary>
    /// <param name="options">Options for the token exchange request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The JAG JWT string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when required option values are missing.</exception>
    /// <exception cref="EnterpriseAuthException">Thrown when the token exchange request fails.</exception>
    public static async Task<string> RequestJwtAuthorizationGrantAsync(
        RequestJwtAuthGrantOptions options,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(options);
        Throw.IfNullOrEmpty(options.TokenEndpoint, "TokenEndpoint is required.");
        Throw.IfNullOrEmpty(options.Audience, "Audience is required.");
        Throw.IfNullOrEmpty(options.Resource, "Resource is required.");
        Throw.IfNullOrEmpty(options.IdToken, "IdToken is required.");
        Throw.IfNullOrEmpty(options.ClientId, "ClientId is required.");

        var httpClient = options.HttpClient ?? new HttpClient();

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = GrantTypeTokenExchange,
            ["requested_token_type"] = TokenTypeIdJag,
            ["subject_token"] = options.IdToken,
            ["subject_token_type"] = TokenTypeIdToken,
            ["audience"] = options.Audience,
            ["resource"] = options.Resource,
            ["client_id"] = options.ClientId,
        };

        if (!string.IsNullOrEmpty(options.ClientSecret))
        {
            formData["client_secret"] = options.ClientSecret!;
        }

        if (!string.IsNullOrEmpty(options.Scope))
        {
            formData["scope"] = options.Scope!;
        }

        using var requestContent = new FormUrlEncodedContent(formData);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint)
        {
            Content = requestContent
        };

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var httpResponse = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            OAuthErrorResponse? errorResponse = null;
            try
            {
                errorResponse = JsonSerializer.Deserialize(responseBody, McpJsonUtilities.JsonContext.Default.OAuthErrorResponse);
            }
            catch
            {
                // Could not parse error response
            }

            throw new EnterpriseAuthException(
                $"Token exchange failed with status {(int)httpResponse.StatusCode}.",
                errorResponse?.Error,
                errorResponse?.ErrorDescription,
                errorResponse?.ErrorUri);
        }

        var response = JsonSerializer.Deserialize(responseBody, McpJsonUtilities.JsonContext.Default.JagTokenExchangeResponse);

        if (response is null)
        {
            throw new EnterpriseAuthException($"Failed to parse token exchange response: {responseBody}");
        }

        if (string.IsNullOrEmpty(response.AccessToken))
        {
            throw new EnterpriseAuthException("Token exchange response missing required field: access_token");
        }

        if (!string.Equals(response.IssuedTokenType, TokenTypeIdJag, StringComparison.Ordinal))
        {
            throw new EnterpriseAuthException(
                $"Token exchange response issued_token_type must be '{TokenTypeIdJag}', got '{response.IssuedTokenType}'.");
        }

        if (!string.Equals(response.TokenType, TokenTypeNotApplicable, StringComparison.OrdinalIgnoreCase))
        {
            throw new EnterpriseAuthException(
                $"Token exchange response token_type must be '{TokenTypeNotApplicable}' per RFC 8693 §2.2.1, got '{response.TokenType}'.");
        }

        return response.AccessToken;
    }

    /// <summary>
    /// Discovers the IDP's token endpoint via OAuth/OIDC metadata, then requests a JWT Authorization Grant.
    /// Convenience wrapper over <see cref="RequestJwtAuthorizationGrantAsync"/>.
    /// </summary>
    /// <param name="options">Options for discovery and token exchange. Provides <c>IdpUrl</c> instead of <c>TokenEndpoint</c>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The JAG JWT string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="EnterpriseAuthException">Thrown when IDP discovery or token exchange fails.</exception>
    public static async Task<string> DiscoverAndRequestJwtAuthorizationGrantAsync(
        DiscoverAndRequestJwtAuthGrantOptions options,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(options);

        var tokenEndpoint = options.IdpTokenEndpoint;

        if (string.IsNullOrEmpty(tokenEndpoint))
        {
            Throw.IfNullOrEmpty(options.IdpUrl, "Either IdpUrl or IdpTokenEndpoint is required.");

            var httpClient = options.HttpClient ?? new HttpClient();
            var idpMetadata = await DiscoverAuthServerMetadataAsync(
                new Uri(options.IdpUrl!), httpClient, cancellationToken).ConfigureAwait(false);

            tokenEndpoint = idpMetadata.TokenEndpoint?.ToString()
                ?? throw new EnterpriseAuthException($"IDP metadata discovery for {options.IdpUrl} did not return a token_endpoint.");
        }

        return await RequestJwtAuthorizationGrantAsync(new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = tokenEndpoint!,
            Audience = options.Audience,
            Resource = options.Resource,
            IdToken = options.IdToken,
            ClientId = options.ClientId,
            ClientSecret = options.ClientSecret,
            Scope = options.Scope,
            HttpClient = options.HttpClient,
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Layer 2: JWT Bearer Grant (RFC 7523)

    /// <summary>
    /// Exchanges a JWT Authorization Grant (JAG) for an access token at an MCP Server's authorization server
    /// using the JWT Bearer grant (RFC 7523).
    /// </summary>
    /// <param name="options">Options for the JWT bearer grant request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A <see cref="TokenContainer"/> containing the access token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="EnterpriseAuthException">Thrown when the JWT bearer grant fails.</exception>
    public static async Task<TokenContainer> ExchangeJwtBearerGrantAsync(
        ExchangeJwtBearerGrantOptions options,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(options);
        Throw.IfNullOrEmpty(options.TokenEndpoint, "TokenEndpoint is required.");
        Throw.IfNullOrEmpty(options.Assertion, "Assertion (JAG) is required.");
        Throw.IfNullOrEmpty(options.ClientId, "ClientId is required.");

        var httpClient = options.HttpClient ?? new HttpClient();

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = GrantTypeJwtBearer,
            ["assertion"] = options.Assertion,
            ["client_id"] = options.ClientId,
        };

        if (!string.IsNullOrEmpty(options.ClientSecret))
        {
            formData["client_secret"] = options.ClientSecret!;
        }

        if (!string.IsNullOrEmpty(options.Scope))
        {
            formData["scope"] = options.Scope!;
        }

        using var requestContent = new FormUrlEncodedContent(formData);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint)
        {
            Content = requestContent
        };

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var httpResponse = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            OAuthErrorResponse? errorResponse = null;
            try
            {
                errorResponse = JsonSerializer.Deserialize(responseBody, McpJsonUtilities.JsonContext.Default.OAuthErrorResponse);
            }
            catch
            {
                // Could not parse error response
            }

            throw new EnterpriseAuthException(
                $"JWT bearer grant failed with status {(int)httpResponse.StatusCode}.",
                errorResponse?.Error,
                errorResponse?.ErrorDescription,
                errorResponse?.ErrorUri);
        }

        var response = JsonSerializer.Deserialize(responseBody, McpJsonUtilities.JsonContext.Default.JwtBearerAccessTokenResponse);

        if (response is null)
        {
            throw new EnterpriseAuthException($"Failed to parse JWT bearer grant response: {responseBody}");
        }

        if (string.IsNullOrEmpty(response.AccessToken))
        {
            throw new EnterpriseAuthException("JWT bearer grant response missing required field: access_token");
        }

        if (string.IsNullOrEmpty(response.TokenType))
        {
            throw new EnterpriseAuthException("JWT bearer grant response missing required field: token_type");
        }

        if (!string.Equals(response.TokenType, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new EnterpriseAuthException(
                $"JWT bearer grant response token_type must be 'bearer' per RFC 7523, got '{response.TokenType}'.");
        }

        return new TokenContainer
        {
            AccessToken = response.AccessToken,
            TokenType = response.TokenType,
            RefreshToken = response.RefreshToken,
            ExpiresIn = response.ExpiresIn,
            Scope = response.Scope,
            ObtainedAt = DateTimeOffset.UtcNow,
        };
    }

    #endregion

    #region Helper: Auth Server Metadata Discovery

    private static readonly string[] s_wellKnownPaths = [".well-known/openid-configuration", ".well-known/oauth-authorization-server"];

    /// <summary>
    /// Discovers authorization server metadata from the well-known endpoints.
    /// </summary>
    internal static async Task<AuthorizationServerMetadata> DiscoverAuthServerMetadataAsync(
        Uri issuerUrl,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var baseUrl = issuerUrl.ToString();
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            issuerUrl = new Uri($"{baseUrl}/");
        }

        foreach (var path in s_wellKnownPaths)
        {
            try
            {
                var wellKnownEndpoint = new Uri(issuerUrl, path);
                var response = await httpClient.GetAsync(wellKnownEndpoint, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var metadata = await JsonSerializer.DeserializeAsync(
                    stream,
                    McpJsonUtilities.JsonContext.Default.AuthorizationServerMetadata,
                    cancellationToken).ConfigureAwait(false);

                if (metadata is not null)
                {
                    return metadata;
                }
            }
            catch
            {
                continue;
            }
        }

        throw new EnterpriseAuthException($"Failed to discover authorization server metadata for: {issuerUrl}");
    }

    #endregion

    #region Helpers

    private static class Throw
    {
        public static void IfNull<T>(T value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null) where T : class
        {
            if (value is null)
            {
                throw new ArgumentNullException(name);
            }
        }

        public static void IfNullOrEmpty(string? value, string message)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(message);
            }
        }
    }

    #endregion
}

#region Options Types

/// <summary>
/// Options for requesting a JWT Authorization Grant from an Identity Provider via RFC 8693 Token Exchange.
/// </summary>
public sealed class RequestJwtAuthGrantOptions
{
    /// <summary>
    /// Gets or sets the IDP's token endpoint URL.
    /// </summary>
    public required string TokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the MCP authorization server URL (used as the <c>audience</c> parameter).
    /// </summary>
    public required string Audience { get; set; }

    /// <summary>
    /// Gets or sets the MCP resource server URL (used as the <c>resource</c> parameter).
    /// </summary>
    public required string Resource { get; set; }

    /// <summary>
    /// Gets or sets the OIDC ID token to exchange.
    /// </summary>
    public required string IdToken { get; set; }

    /// <summary>
    /// Gets or sets the client ID for authentication with the IDP.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for authentication with the IDP. Optional.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the scopes to request (space-separated). Optional.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the HTTP client for making requests. If not provided, a default HttpClient will be used.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}

/// <summary>
/// Options for discovering an IDP's token endpoint and requesting a JWT Authorization Grant.
/// Extends <see cref="RequestJwtAuthGrantOptions"/> semantics but replaces <c>TokenEndpoint</c>
/// with <c>IdpUrl</c>/<c>IdpTokenEndpoint</c> for automatic discovery.
/// </summary>
public sealed class DiscoverAndRequestJwtAuthGrantOptions
{
    /// <summary>
    /// Gets or sets the Identity Provider's base URL for OAuth/OIDC discovery.
    /// Used when <see cref="IdpTokenEndpoint"/> is not specified.
    /// </summary>
    public string? IdpUrl { get; set; }

    /// <summary>
    /// Gets or sets the IDP token endpoint URL. When provided, skips IDP metadata discovery.
    /// </summary>
    public string? IdpTokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the MCP authorization server URL (used as the <c>audience</c> parameter).
    /// </summary>
    public required string Audience { get; set; }

    /// <summary>
    /// Gets or sets the MCP resource server URL (used as the <c>resource</c> parameter).
    /// </summary>
    public required string Resource { get; set; }

    /// <summary>
    /// Gets or sets the OIDC ID token to exchange.
    /// </summary>
    public required string IdToken { get; set; }

    /// <summary>
    /// Gets or sets the client ID for authentication with the IDP.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for authentication with the IDP. Optional.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the scopes to request (space-separated). Optional.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the HTTP client for making requests.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}

/// <summary>
/// Options for exchanging a JWT Authorization Grant for an access token via RFC 7523.
/// </summary>
public sealed class ExchangeJwtBearerGrantOptions
{
    /// <summary>
    /// Gets or sets the MCP Server's authorization server token endpoint URL.
    /// </summary>
    public required string TokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the JWT Authorization Grant (JAG) assertion obtained from token exchange.
    /// </summary>
    public required string Assertion { get; set; }

    /// <summary>
    /// Gets or sets the client ID for authentication with the MCP authorization server.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for authentication with the MCP authorization server. Optional.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the scopes to request (space-separated). Optional.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the HTTP client for making requests.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}

#endregion

#region Response Types

/// <summary>
/// Represents the response from an RFC 8693 Token Exchange for the JAG flow.
/// Contains the JWT Authorization Grant in the <see cref="AccessToken"/> field.
/// </summary>
internal sealed class JagTokenExchangeResponse
{
    /// <summary>
    /// Gets or sets the issued JAG. Despite the name "access_token" (required by RFC 8693),
    /// for SEP-990 this contains a JAG JWT, not an OAuth access token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;

    /// <summary>
    /// Gets or sets the type of the security token issued.
    /// For SEP-990, this MUST be <see cref="EnterpriseAuth.TokenTypeIdJag"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("issued_token_type")]
    public string IssuedTokenType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the token type. For SEP-990, this MUST be "N_A" per RFC 8693 §2.2.1.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string TokenType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the scope of the issued token, if different from the request.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the lifetime in seconds of the issued token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }
}

/// <summary>
/// Represents the response from a JWT Bearer grant (RFC 7523) access token request.
/// </summary>
internal sealed class JwtBearerAccessTokenResponse
{
    /// <summary>
    /// Gets or sets the OAuth access token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;

    /// <summary>
    /// Gets or sets the token type. This should be "Bearer".
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string TokenType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the lifetime in seconds of the access token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the scope of the access token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

/// <summary>
/// Represents an OAuth error response per RFC 6749 Section 5.2.
/// Used for both token exchange and JWT bearer grant error responses.
/// </summary>
internal sealed class OAuthErrorResponse
{
    /// <summary>
    /// Gets or sets the error code.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the human-readable error description.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }

    /// <summary>
    /// Gets or sets the URI identifying a human-readable web page with error information.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("error_uri")]
    public string? ErrorUri { get; set; }
}

#endregion

#region Exception Type

/// <summary>
/// Represents an error that occurred during Enterprise Managed Authorization (SEP-990) operations,
/// including token exchange (RFC 8693) and JWT bearer grant (RFC 7523) failures.
/// </summary>
public sealed class EnterpriseAuthException : Exception
{
    /// <summary>
    /// Gets the OAuth error code, if available (e.g., "invalid_request", "invalid_grant").
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the human-readable error description from the OAuth error response.
    /// </summary>
    public string? ErrorDescription { get; }

    /// <summary>
    /// Gets the URI identifying a human-readable web page with error information.
    /// </summary>
    public string? ErrorUri { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnterpriseAuthException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The OAuth error code.</param>
    /// <param name="errorDescription">The human-readable error description.</param>
    /// <param name="errorUri">The error URI.</param>
    public EnterpriseAuthException(string message, string? errorCode = null, string? errorDescription = null, string? errorUri = null)
        : base(FormatMessage(message, errorCode, errorDescription))
    {
        ErrorCode = errorCode;
        ErrorDescription = errorDescription;
        ErrorUri = errorUri;
    }

    private static string FormatMessage(string message, string? errorCode, string? errorDescription)
    {
        if (!string.IsNullOrEmpty(errorCode))
        {
            message = $"{message} Error: {errorCode}";
            if (!string.IsNullOrEmpty(errorDescription))
            {
                message = $"{message} ({errorDescription})";
            }
        }
        return message;
    }
}

#endregion
