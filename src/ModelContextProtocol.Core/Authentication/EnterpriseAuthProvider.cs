using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Context provided to the assertion callback for Enterprise Managed Authorization (SEP-990).
/// Contains the URLs discovered during the OAuth flow that are needed for the token exchange step.
/// </summary>
public sealed class EnterpriseAuthAssertionContext
{
    /// <summary>
    /// Gets the MCP resource server URL (i.e., the <c>resource</c> parameter for token exchange).
    /// This is the URL of the MCP server being accessed.
    /// </summary>
    public required Uri ResourceUrl { get; init; }

    /// <summary>
    /// Gets the MCP authorization server URL (i.e., the <c>audience</c> parameter for token exchange).
    /// This is the URL of the authorization server protecting the MCP resource.
    /// </summary>
    public required Uri AuthorizationServerUrl { get; init; }
}

/// <summary>
/// Provides Enterprise Managed Authorization (SEP-990) as a standalone, non-interactive provider
/// that can be used alongside the MCP client's OAuth infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// This provider implements the full Identity Assertion Authorization Grant flow:
/// </para>
/// <list type="number">
/// <item><description>
/// The <see cref="AssertionCallback"/> is called to obtain a JWT Authorization Grant (JAG).
/// The callback receives a <see cref="EnterpriseAuthAssertionContext"/> with the discovered
/// resource URL and authorization server URL. Typically, the callback calls
/// <see cref="EnterpriseAuth.DiscoverAndRequestJwtAuthorizationGrantAsync"/> or
/// <see cref="EnterpriseAuth.RequestJwtAuthorizationGrantAsync"/> to perform the RFC 8693
/// Token Exchange at the enterprise IdP.
/// </description></item>
/// <item><description>
/// The returned JAG is then exchanged for an access token at the MCP Server's
/// authorization server via the RFC 7523 JWT Bearer grant.
/// </description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var provider = new EnterpriseAuthProvider(new EnterpriseAuthProviderOptions
/// {
///     ClientId = "my-mcp-client-id",
///     AssertionCallback = async (context, ct) =>
///     {
///         // Use Layer 2 utility to get a JAG from the enterprise IdP
///         return await EnterpriseAuth.DiscoverAndRequestJwtAuthorizationGrantAsync(
///             new DiscoverAndRequestJwtAuthGrantOptions
///             {
///                 IdpUrl = "https://company.okta.com",
///                 Audience = context.AuthorizationServerUrl.ToString(),
///                 Resource = context.ResourceUrl.ToString(),
///                 IdToken = myIdToken, // from SSO login
///                 ClientId = "idp-client-id",
///             }, ct);
///     }
/// });
///
/// // Use with MCP client transport
/// var tokens = await provider.GetAccessTokenAsync(
///     resourceUrl: new Uri("https://mcp-server.example.com"),
///     authorizationServerUrl: new Uri("https://auth.example.com"),
///     cancellationToken: ct);
/// </code>
/// </example>
public sealed class EnterpriseAuthProvider
{
    private readonly EnterpriseAuthProviderOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    private TokenContainer? _cachedTokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnterpriseAuthProvider"/> class.
    /// </summary>
    /// <param name="options">Configuration for the Enterprise Auth provider.</param>
    /// <param name="httpClient">Optional HTTP client. A default will be created if not provided.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Required option values are missing.</exception>
    public EnterpriseAuthProvider(
        EnterpriseAuthProviderOptions options,
        HttpClient? httpClient = null,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrEmpty(options.ClientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(options));
        }

        if (options.AssertionCallback is null)
        {
            throw new ArgumentException("AssertionCallback is required.", nameof(options));
        }

        _httpClient = httpClient ?? new HttpClient();
        _logger = (ILogger?)loggerFactory?.CreateLogger<EnterpriseAuthProvider>() ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets or sets the assertion callback that is invoked to obtain a JWT Authorization Grant (JAG).
    /// </summary>
    /// <remarks>
    /// The callback receives a <see cref="EnterpriseAuthAssertionContext"/> containing
    /// the resource and authorization server URLs discovered during the OAuth flow.
    /// It should return the JAG JWT string obtained via token exchange at the enterprise IdP.
    /// </remarks>
    public Func<EnterpriseAuthAssertionContext, CancellationToken, Task<string>> AssertionCallback
        => _options.AssertionCallback!;

    /// <summary>
    /// Performs the full Enterprise Auth flow to obtain an access token for the given MCP resource.
    /// </summary>
    /// <param name="resourceUrl">The MCP resource server URL.</param>
    /// <param name="authorizationServerUrl">The MCP authorization server URL.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A <see cref="TokenContainer"/> containing the access token.</returns>
    /// <exception cref="EnterpriseAuthException">Thrown when the assertion callback or JWT bearer grant fails.</exception>
    public async Task<TokenContainer> GetAccessTokenAsync(
        Uri resourceUrl,
        Uri authorizationServerUrl,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_cachedTokens is not null && !_cachedTokens.IsExpired)
        {
            return _cachedTokens;
        }

        _logger.LogDebug("Starting Enterprise Auth flow for resource {ResourceUrl}", resourceUrl);

        // Step 1: Discover MCP auth server metadata to find token endpoint
        var mcpAuthMetadata = await EnterpriseAuth.DiscoverAuthServerMetadataAsync(
            authorizationServerUrl, _httpClient, cancellationToken).ConfigureAwait(false);

        var tokenEndpoint = mcpAuthMetadata.TokenEndpoint?.ToString()
            ?? throw new EnterpriseAuthException(
                $"MCP authorization server metadata at {authorizationServerUrl} missing token_endpoint.");

        // Step 2: Call the assertion callback to get the JAG
        var context = new EnterpriseAuthAssertionContext
        {
            ResourceUrl = resourceUrl,
            AuthorizationServerUrl = authorizationServerUrl,
        };

        _logger.LogDebug("Requesting assertion (JAG) via callback");
        var jag = await AssertionCallback(context, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(jag))
        {
            throw new EnterpriseAuthException("Assertion callback returned a null or empty JAG.");
        }

        // Step 3: Exchange JAG for access token via JWT Bearer grant (RFC 7523)
        _logger.LogDebug("Exchanging JAG for access token at {TokenEndpoint}", tokenEndpoint);
        var tokens = await EnterpriseAuth.ExchangeJwtBearerGrantAsync(
            new ExchangeJwtBearerGrantOptions
            {
                TokenEndpoint = tokenEndpoint,
                Assertion = jag,
                ClientId = _options.ClientId!,
                ClientSecret = _options.ClientSecret,
                Scope = _options.Scope,
                HttpClient = _httpClient,
            }, cancellationToken).ConfigureAwait(false);

        _cachedTokens = tokens;
        _logger.LogDebug("Enterprise Auth flow completed successfully");

        return tokens;
    }

    /// <summary>
    /// Clears any cached tokens, forcing a fresh token exchange on the next call.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedTokens = null;
    }
}

/// <summary>
/// Configuration options for the <see cref="EnterpriseAuthProvider"/>.
/// </summary>
public sealed class EnterpriseAuthProviderOptions
{
    /// <summary>
    /// Gets or sets the MCP client ID used for the JWT Bearer grant at the MCP authorization server.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the MCP client secret used for the JWT Bearer grant at the MCP authorization server.
    /// Optional; only required if the MCP auth server requires client authentication.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the scopes to request from the MCP authorization server (space-separated).
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the assertion callback that obtains a JWT Authorization Grant (JAG).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This callback is invoked during the Enterprise Auth flow, after the MCP resource and
    /// authorization server URLs have been discovered. It receives a <see cref="EnterpriseAuthAssertionContext"/>
    /// with these URLs and should return the JAG JWT string.
    /// </para>
    /// <para>
    /// A typical implementation calls <see cref="EnterpriseAuth.DiscoverAndRequestJwtAuthorizationGrantAsync"/>
    /// or <see cref="EnterpriseAuth.RequestJwtAuthorizationGrantAsync"/> to perform the RFC 8693 token
    /// exchange at the enterprise Identity Provider.
    /// </para>
    /// </remarks>
    public required Func<EnterpriseAuthAssertionContext, CancellationToken, Task<string>> AssertionCallback { get; set; }
}
