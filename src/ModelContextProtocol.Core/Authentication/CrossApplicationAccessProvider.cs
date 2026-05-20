using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides Cross-Application Access authorization as a standalone, non-interactive provider
/// that can be used alongside the MCP client's OAuth infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// This provider implements the full Identity Assertion Authorization Grant flow as specified at
/// <see href="https://github.com/modelcontextprotocol/ext-auth/blob/main/specification/draft/enterprise-managed-authorization.mdx"/>:
/// </para>
/// <list type="number">
/// <item><description>
/// The <see cref="CrossApplicationAccessProviderOptions.IdTokenCallback"/> is called to obtain an OIDC ID token.
/// It receives a <see cref="CrossApplicationAccessContext"/> with the discovered resource and authorization
/// server URLs.
/// </description></item>
/// <item><description>
/// The provider performs the RFC 8693 token exchange at the enterprise Identity Provider
/// (using the configured <c>IdpTokenEndpoint</c> or discovered from <c>IdpUrl</c>),
/// exchanging the ID token for a JWT Authorization Grant (JAG).
/// </description></item>
/// <item><description>
/// The JAG is then exchanged for an access token at the MCP Server's authorization server
/// via the RFC 7523 JWT Bearer grant.
/// </description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var provider = new CrossApplicationAccessProvider(new CrossApplicationAccessProviderOptions
/// {
///     ClientId = "mcp-client-id",
///     IdpTokenEndpoint = "https://company.okta.com/oauth2/token",
///     IdpClientId = "idp-client-id",
///     IdTokenCallback = async (context, ct) =>
///     {
///         // Return the ID token from your SSO session
///         return myIdToken;
///     }
/// });
///
/// var tokens = await provider.GetAccessTokenAsync(
///     resourceUrl: new Uri("https://mcp-server.example.com"),
///     authorizationServerUrl: new Uri("https://auth.example.com"),
///     cancellationToken: ct);
/// </code>
/// </example>
public sealed class CrossApplicationAccessProvider
{
    private readonly CrossApplicationAccessProviderOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    private TokenContainer? _cachedTokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossApplicationAccessProvider"/> class.
    /// </summary>
    /// <param name="options">Configuration for the Cross-Application Access provider.</param>
    /// <param name="httpClient">Optional HTTP client. A default will be created if not provided.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Required option values are missing.</exception>
    public CrossApplicationAccessProvider(
        CrossApplicationAccessProviderOptions options,
        HttpClient? httpClient = null,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrEmpty(options.ClientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(options));
        }

        if (string.IsNullOrEmpty(options.IdpClientId))
        {
            throw new ArgumentException("IdpClientId is required.", nameof(options));
        }

        if (string.IsNullOrEmpty(options.IdpUrl) && string.IsNullOrEmpty(options.IdpTokenEndpoint))
        {
            throw new ArgumentException("Either IdpUrl or IdpTokenEndpoint is required.", nameof(options));
        }

        if (options.IdTokenCallback is null)
        {
            throw new ArgumentException("IdTokenCallback is required.", nameof(options));
        }

        _httpClient = httpClient ?? new HttpClient();
        _logger = (ILogger?)loggerFactory?.CreateLogger<CrossApplicationAccessProvider>() ?? NullLogger.Instance;
    }

    /// <summary>
    /// Performs the full Cross-Application Access flow to obtain an access token for the given MCP resource.
    /// </summary>
    /// <param name="resourceUrl">The MCP resource server URL.</param>
    /// <param name="authorizationServerUrl">The MCP authorization server URL.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A <see cref="TokenContainer"/> containing the access token.</returns>
    /// <exception cref="CrossApplicationAccessException">Thrown when any step of the flow fails.</exception>
    public async Task<TokenContainer> GetAccessTokenAsync(
        Uri resourceUrl,
        Uri authorizationServerUrl,
        CancellationToken cancellationToken = default)
    {
        // Return cached token if still valid
        if (_cachedTokens is not null && !_cachedTokens.IsExpired)
        {
            return _cachedTokens;
        }

        _logger.LogDebug("Starting Cross-Application Access flow for resource {ResourceUrl}", resourceUrl);

        // Step 1: Discover MCP authorization server metadata to find the token endpoint
        var mcpAuthMetadata = await CrossApplicationAccess.DiscoverAuthServerMetadataAsync(
            authorizationServerUrl, _httpClient, cancellationToken).ConfigureAwait(false);

        var mcpTokenEndpoint = mcpAuthMetadata.TokenEndpoint?.ToString()
            ?? throw new CrossApplicationAccessException(
                $"MCP authorization server metadata at {authorizationServerUrl} missing token_endpoint.");

        // Step 2: Call the ID token callback to get the caller's OIDC ID token
        var context = new CrossApplicationAccessContext
        {
            ResourceUrl = resourceUrl,
            AuthorizationServerUrl = authorizationServerUrl,
        };

        _logger.LogDebug("Requesting ID token via callback");
        var idToken = await _options.IdTokenCallback(context, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(idToken))
        {
            throw new CrossApplicationAccessException("ID token callback returned a null or empty token.");
        }

        // Step 3: RFC 8693 token exchange — ID token → JWT Authorization Grant (JAG) at the enterprise IdP
        _logger.LogDebug("Performing RFC 8693 token exchange at IdP");
        var idpTokenEndpoint = await ResolveIdpTokenEndpointAsync(cancellationToken).ConfigureAwait(false);

        var jag = await CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(
            new RequestJwtAuthGrantOptions
            {
                TokenEndpoint = idpTokenEndpoint,
                Audience = authorizationServerUrl.ToString(),
                Resource = resourceUrl.ToString(),
                IdToken = idToken,
                ClientId = _options.IdpClientId,
                ClientSecret = _options.IdpClientSecret,
                Scope = _options.IdpScope,
                HttpClient = _httpClient,
            }, cancellationToken).ConfigureAwait(false);

        // Step 4: RFC 7523 JWT bearer grant — JAG → access token at the MCP authorization server
        _logger.LogDebug("Exchanging JAG for access token at {McpTokenEndpoint}", mcpTokenEndpoint);
        var tokens = await CrossApplicationAccess.ExchangeJwtBearerGrantAsync(
            new ExchangeJwtBearerGrantOptions
            {
                TokenEndpoint = mcpTokenEndpoint,
                Assertion = jag,
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret,
                Scope = _options.Scope,
                HttpClient = _httpClient,
            }, cancellationToken).ConfigureAwait(false);

        _cachedTokens = tokens;
        _logger.LogDebug("Cross-Application Access flow completed successfully");

        return tokens;
    }

    /// <summary>
    /// Clears any cached tokens, forcing a fresh token exchange on the next call to <see cref="GetAccessTokenAsync"/>.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedTokens = null;
    }

    private string? _resolvedIdpTokenEndpoint;

    private async Task<string> ResolveIdpTokenEndpointAsync(CancellationToken cancellationToken)
    {
        if (_resolvedIdpTokenEndpoint is not null)
        {
            return _resolvedIdpTokenEndpoint;
        }

        if (!string.IsNullOrEmpty(_options.IdpTokenEndpoint))
        {
            _resolvedIdpTokenEndpoint = _options.IdpTokenEndpoint;
            return _resolvedIdpTokenEndpoint;
        }

        // Discover from IdpUrl
        var idpMetadata = await CrossApplicationAccess.DiscoverAuthServerMetadataAsync(
            new Uri(_options.IdpUrl!), _httpClient, cancellationToken).ConfigureAwait(false);

        _resolvedIdpTokenEndpoint = idpMetadata.TokenEndpoint?.ToString()
            ?? throw new CrossApplicationAccessException(
                $"IdP metadata discovery for {_options.IdpUrl} did not return a token_endpoint.");

        return _resolvedIdpTokenEndpoint;
    }
}
