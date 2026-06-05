using System.Net.Http.Headers;
using System.Text.Json;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides internal utilities for the Cross-Application Access authorization flow.
/// </summary>
/// <remarks>
/// Implements the Enterprise Managed Authorization flow as specified at
/// <see href="https://github.com/modelcontextprotocol/ext-auth/blob/main/specification/draft/enterprise-managed-authorization.mdx"/>.
/// </remarks>
internal static class IdentityAssertionGrant
{
    #region Constants

    /// <summary>Grant type URN for RFC 8693 token exchange.</summary>
    public const string GrantTypeTokenExchange = "urn:ietf:params:oauth:grant-type:token-exchange";

    /// <summary>Grant type URN for RFC 7523 JWT Bearer authorization grant.</summary>
    public const string GrantTypeJwtBearer = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    /// <summary>Token type URN for OpenID Connect ID Tokens (RFC 8693).</summary>
    public const string TokenTypeIdToken = "urn:ietf:params:oauth:token-type:id_token";

    /// <summary>Token type URN for SAML 2.0 assertions (RFC 8693).</summary>
    public const string TokenTypeSaml2 = "urn:ietf:params:oauth:token-type:saml2";

    /// <summary>
    /// Token type URN for Identity Assertion JWT Authorization Grants.
    /// As specified at
    /// <see href="https://github.com/modelcontextprotocol/ext-auth/blob/main/specification/draft/enterprise-managed-authorization.mdx"/>.
    /// </summary>
    public const string TokenTypeIdJag = "urn:ietf:params:oauth:token-type:id-jag";

    /// <summary>
    /// The expected value for <c>token_type</c> in a JAG token exchange response per RFC 8693 §2.2.1.
    /// The issued token is not an OAuth access token, so its type is "N_A".
    /// </summary>
    public const string TokenTypeNotApplicable = "N_A";

    #endregion

    #region Token Exchange (RFC 8693)

    /// <summary>
    /// Requests a JWT Authorization Grant (JAG) from an Identity Provider via RFC 8693 Token Exchange.
    /// Returns the JAG string to be used as a JWT Bearer assertion (RFC 7523) against the MCP authorization server.
    /// </summary>
    public static async Task<string> RequestJwtAuthorizationGrantAsync(
        RequestJwtAuthGrantOptions options,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(options);
        Throw.IfNullOrWhiteSpace(options.TokenEndpoint);
        Throw.IfNullOrWhiteSpace(options.Audience);
        Throw.IfNullOrWhiteSpace(options.Resource);
        Throw.IfNullOrWhiteSpace(options.IdToken);
        Throw.IfNullOrWhiteSpace(options.ClientId);

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

            throw new IdentityAssertionGrantException(
                $"Token exchange failed with status {(int)httpResponse.StatusCode}.",
                errorResponse?.Error,
                errorResponse?.ErrorDescription,
                errorResponse?.ErrorUri);
        }

        var response = JsonSerializer.Deserialize(responseBody, McpJsonUtilities.JsonContext.Default.JagTokenExchangeResponse);

        if (response is null)
        {
            var ex = new IdentityAssertionGrantException("Failed to parse token exchange response.");
            ex.Data["ResponseBody"] = responseBody;
            throw ex;
        }

        if (string.IsNullOrEmpty(response.AccessToken))
        {
            throw new IdentityAssertionGrantException("Token exchange response missing required field: access_token");
        }

        if (!string.Equals(response.IssuedTokenType, TokenTypeIdJag, StringComparison.Ordinal))
        {
            throw new IdentityAssertionGrantException(
                $"Token exchange response issued_token_type must be '{TokenTypeIdJag}', got '{response.IssuedTokenType}'.");
        }

        if (!string.Equals(response.TokenType, TokenTypeNotApplicable, StringComparison.Ordinal))
        {
            throw new IdentityAssertionGrantException(
                $"Token exchange response token_type must be '{TokenTypeNotApplicable}' per RFC 8693 §2.2.1, got '{response.TokenType}'.");
        }

        return response.AccessToken;
    }

    #endregion

    #region JWT Bearer Grant (RFC 7523)

    /// <summary>
    /// Exchanges a JWT Authorization Grant (JAG) for an access token at an MCP Server's authorization server
    /// using the JWT Bearer grant (RFC 7523).
    /// </summary>
    public static async Task<TokenContainer> ExchangeJwtBearerGrantAsync(
        ExchangeJwtBearerGrantOptions options,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(options);
        Throw.IfNullOrWhiteSpace(options.TokenEndpoint);
        Throw.IfNullOrWhiteSpace(options.Assertion);
        Throw.IfNullOrWhiteSpace(options.ClientId);

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

            throw new IdentityAssertionGrantException(
                $"JWT bearer grant failed with status {(int)httpResponse.StatusCode}.",
                errorResponse?.Error,
                errorResponse?.ErrorDescription,
                errorResponse?.ErrorUri);
        }

        var response = JsonSerializer.Deserialize(responseBody, McpJsonUtilities.JsonContext.Default.JwtBearerAccessTokenResponse);

        if (response is null)
        {
            var ex = new IdentityAssertionGrantException("Failed to parse JWT bearer grant response.");
            ex.Data["ResponseBody"] = responseBody;
            throw ex;
        }

        if (string.IsNullOrEmpty(response.AccessToken))
        {
            throw new IdentityAssertionGrantException("JWT bearer grant response missing required field: access_token");
        }

        if (string.IsNullOrEmpty(response.TokenType))
        {
            throw new IdentityAssertionGrantException("JWT bearer grant response missing required field: token_type");
        }

        if (!string.Equals(response.TokenType, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new IdentityAssertionGrantException(
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

        throw new IdentityAssertionGrantException($"Failed to discover authorization server metadata for: {issuerUrl}");
    }

    #endregion
}
