using Microsoft.AspNetCore.Http;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Configures an OAuth authorization-server facade for upstream providers that do not support
/// Dynamic Client Registration.
/// </summary>
public sealed class McpOAuthProxyOptions
{
    /// <summary>
    /// Gets or sets the public issuer URI advertised by the proxy.
    /// </summary>
    public Uri Issuer { get; set; } = null!;

    /// <summary>
    /// Gets or sets the upstream OpenID Connect issuer used for endpoint discovery.
    /// </summary>
    /// <remarks>
    /// Set this when <see cref="UpstreamAuthorizationEndpoint"/> and
    /// <see cref="UpstreamTokenEndpoint"/> are not configured explicitly.
    /// </remarks>
    public Uri? UpstreamIssuer { get; set; }

    /// <summary>
    /// Gets or sets the upstream authorization endpoint.
    /// </summary>
    public Uri? UpstreamAuthorizationEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the upstream token endpoint.
    /// </summary>
    public Uri? UpstreamTokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the pre-registered upstream client identifier.
    /// </summary>
    public string UpstreamClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the pre-registered upstream client secret, when required.
    /// </summary>
    public string? UpstreamClientSecret { get; set; }

    /// <summary>
    /// Gets or sets how the proxy authenticates at the upstream token endpoint.
    /// </summary>
    public McpOAuthProxyClientAuthenticationMethod UpstreamClientAuthenticationMethod { get; set; } =
        McpOAuthProxyClientAuthenticationMethod.ClientSecretPost;

    /// <summary>
    /// Gets or sets the fixed proxy callback URI registered with the upstream provider.
    /// </summary>
    public Uri UpstreamRedirectUri { get; set; } = null!;

    /// <summary>
    /// Gets the complete set of scopes that dynamic clients may request.
    /// </summary>
    public ISet<string> AllowedScopes { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the exact resource indicator URIs that dynamic clients may request.
    /// </summary>
    /// <remarks>
    /// A requested resource is rejected unless it appears in this set. This prevents an untrusted
    /// dynamic client from choosing the audience of a proxy-issued token.
    /// </remarks>
    public ISet<string> AllowedResources { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets additional parameters included in upstream authorization requests.
    /// </summary>
    public IDictionary<string, string> AdditionalAuthorizationParameters { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets additional parameters included in upstream token requests.
    /// </summary>
    public IDictionary<string, string> AdditionalTokenParameters { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets whether a downstream RFC 8707 resource indicator is forwarded upstream.
    /// </summary>
    public bool ForwardResourceIndicator { get; set; }

    /// <summary>
    /// Gets or sets an optional JSON Web Key Set URI advertised by the proxy.
    /// </summary>
    /// <remarks>
    /// Configure this when <see cref="TokenFactory"/> issues JWT access tokens that clients or
    /// resource servers validate through the proxy metadata.
    /// </remarks>
    public Uri? JwksUri { get; set; }

    /// <summary>
    /// Gets or sets the callback that mints a client-facing access token.
    /// </summary>
    /// <remarks>
    /// The callback must issue a new token for the proxy issuer and protected resource. The proxy
    /// rejects a result that equals any upstream access, refresh, or ID token.
    /// </remarks>
    public Func<McpOAuthProxyTokenContext, CancellationToken, ValueTask<McpOAuthProxyTokenResult>> TokenFactory { get; set; } = null!;

    /// <summary>
    /// Gets or sets the policy that approves dynamic client registrations.
    /// </summary>
    /// <remarks>
    /// This policy is required and should authenticate an initial access token, enforce a trusted
    /// redirect-URI policy, or otherwise establish that the dynamic client may use the proxy's
    /// upstream application registration.
    /// </remarks>
    public Func<McpOAuthProxyClientRegistrationContext, CancellationToken, ValueTask<bool>> ClientRegistrationValidator { get; set; } = null!;

    /// <summary>
    /// Gets or sets the policy that approves an authorization request before redirecting upstream.
    /// </summary>
    /// <remarks>
    /// This policy is required. Applications should use it to enforce downstream client consent,
    /// authenticated-user policy, or an equivalent approval boundary for the registered client.
    /// </remarks>
    public Func<McpOAuthProxyAuthorizationContext, CancellationToken, ValueTask<bool>> AuthorizationValidator { get; set; } = null!;

    /// <summary>
    /// Gets or sets the maximum accepted lifetime of a token returned by <see cref="TokenFactory"/>.
    /// </summary>
    public TimeSpan MaximumAccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the lifetime of authorization transactions.
    /// </summary>
    public TimeSpan AuthorizationTransactionLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the lifetime of proxy authorization codes.
    /// </summary>
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the lifetime of dynamic client registrations.
    /// </summary>
    public TimeSpan ClientRegistrationLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Gets or sets the maximum lifetime of proxy refresh-token mappings.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets the secure policy for the browser-binding cookie.
    /// </summary>
    /// <remarks>
    /// Keep the default in production. <see cref="CookieSecurePolicy.SameAsRequest"/> may be used
    /// for loopback-only development servers.
    /// </remarks>
    public CookieSecurePolicy CookieSecurePolicy { get; set; } = CookieSecurePolicy.Always;
}

/// <summary>
/// Specifies how the proxy authenticates to the upstream token endpoint.
/// </summary>
public enum McpOAuthProxyClientAuthenticationMethod
{
    /// <summary>
    /// Sends the client credentials using HTTP Basic authentication.
    /// </summary>
    ClientSecretBasic,

    /// <summary>
    /// Sends the client credentials in the form body.
    /// </summary>
    ClientSecretPost,

    /// <summary>
    /// Does not send a client secret.
    /// </summary>
    None,
}
