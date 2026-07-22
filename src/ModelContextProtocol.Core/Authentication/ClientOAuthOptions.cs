namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides configuration options for the <see cref="ClientOAuthProvider"/>.
/// </summary>
public sealed class ClientOAuthOptions
{
    /// <summary>
    /// Gets or sets the OAuth redirect URI.
    /// </summary>
    public required Uri RedirectUri { get; set; }

    /// <summary>
    /// Gets or sets the OAuth client ID. If not provided, the client will attempt to register dynamically.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the OAuth client secret.
    /// </summary>
    /// <remarks>
    /// This secret is optional for public clients or when using PKCE without client authentication.
    /// </remarks>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the HTTPS URL pointing to this client's metadata document.
    /// </summary>
    /// <remarks>
    /// When specified, and when the authorization server metadata reports
    /// <c>client_id_metadata_document_supported = true</c>, the OAuth client will respond to
    /// challenges by sending this URL as the client identifier instead of performing dynamic
    /// client registration.
    /// </remarks>
    public Uri? ClientMetadataDocumentUri { get; set; }

    /// <summary>
    /// Gets or sets the OAuth scopes to request as a fallback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These scopes are used only when the server does not provide scope information via the
    /// WWW-Authenticate header or Protected Resource Metadata (<c>scopes_supported</c>). This
    /// matches the MCP scope selection strategy: WWW-Authenticate scope → PRM scopes_supported →
    /// client-configured scopes → omit scope parameter.
    /// </para>
    /// <para>
    /// To filter or customize scopes when the server <em>does</em> provide scope information,
    /// use <see cref="ScopeSelector"/> instead.
    /// </para>
    /// </remarks>
    public IEnumerable<string>? Scopes { get; set; }

    /// <summary>
    /// Gets or sets a delegate that selects or filters the OAuth scopes to request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set, this delegate is called after the MCP scope selection strategy has determined the
    /// candidate scopes (WWW-Authenticate → PRM <c>scopes_supported</c> → <see cref="Scopes"/> fallback)
    /// and after <c>offline_access</c> has been automatically appended when advertised by the
    /// authorization server. The return value replaces the candidate scopes in the authorization request.
    /// </para>
    /// <para>
    /// Use this to request only a subset of the scopes offered by the server, or to append a custom
    /// scope that is not advertised in the server metadata. Return <see langword="null"/> or an empty
    /// enumerable to omit the <c>scope</c> parameter entirely.
    /// </para>
    /// </remarks>
    public ScopeSelectorDelegate? ScopeSelector { get; set; }

    /// <summary>
    /// Gets or sets the callback that handles the OAuth authorization flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This callback receives the authorization and redirect URIs in an
    /// <see cref="AuthorizationCallbackContext"/> and returns the authorization response.
    /// If not specified, a default implementation prompts the user to enter the full redirect URL manually.
    /// </para>
    /// <para>
    /// Custom implementations might open a browser, start an HTTP listener, or use other mechanisms to capture
    /// the authorization response. They should return both the <c>code</c> and <c>iss</c> query parameters
    /// from the redirect URI callback. This enables the SDK to validate the <c>iss</c> parameter per
    /// <see href="https://datatracker.ietf.org/doc/html/rfc9207">RFC 9207</see>, which mitigates
    /// mix-up attacks.
    /// </para>
    /// <para>
    /// This property cannot be configured together with <see cref="AuthorizationRedirectDelegate"/>.
    /// </para>
    /// </remarks>
    public Func<AuthorizationCallbackContext, CancellationToken, Task<AuthorizationResult?>>? AuthorizationCallbackHandler { get; set; }

    /// <summary>
    /// Gets or sets the legacy authorization redirect delegate for handling the OAuth authorization flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This delegate returns only the authorization code and cannot provide the <c>iss</c> parameter from
    /// the authorization response. Consequently, RFC 9207 issuer validation is skipped when this delegate
    /// is used. Use <see cref="AuthorizationCallbackHandler"/> for issuer-aware authorization flows.
    /// </para>
    /// <para>
    /// This property cannot be configured together with <see cref="AuthorizationCallbackHandler"/>.
    /// </para>
    /// </remarks>
    [Obsolete(
        ModelContextProtocol.Obsoletions.AuthorizationRedirectDelegate_Message,
        DiagnosticId = ModelContextProtocol.Obsoletions.AuthorizationRedirectDelegate_DiagnosticId,
        UrlFormat = ModelContextProtocol.Obsoletions.AuthorizationRedirectDelegate_Url)]
    public AuthorizationRedirectDelegate? AuthorizationRedirectDelegate { get; set; }

    /// <summary>
    /// Gets or sets the authorization server selector function.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This function is used to select which authorization server to use when multiple servers are available.
    /// If not specified, the first available server will be selected.
    /// </para>
    /// <para>
    /// The function receives a list of available authorization server URIs and should return the selected server,
    /// or null if no suitable server is found.
    /// </para>
    /// </remarks>
    public Func<IReadOnlyList<Uri>, Uri?>? AuthServerSelector { get; set; }

    /// <summary>
    /// Gets or sets the options to use during dynamic client registration.
    /// </summary>
    /// <remarks>
    /// This value is only used when no <see cref="ClientId"/> is specified.
    /// </remarks>
    public DynamicClientRegistrationOptions? DynamicClientRegistration { get; set; }

    /// <summary>
    /// Gets or sets additional parameters to include in the query string of the OAuth authorization request
    /// providing extra information or fulfilling specific requirements of the OAuth provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameters specified cannot override or append to any automatically set parameters like the "redirect_uri",
    /// which should instead be configured via <see cref="RedirectUri"/>.
    /// </para>
    /// </remarks>
    public IDictionary<string, string> AdditionalAuthorizationParameters { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets the token cache to use for storing and retrieving tokens beyond the lifetime of the transport.
    /// If none is provided, tokens will be cached with the transport.
    /// </summary>
    public ITokenCache? TokenCache { get; set; }
}
