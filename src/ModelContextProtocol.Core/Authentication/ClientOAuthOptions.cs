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
    /// Gets or sets the OAuth scopes to request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When specified, these scopes will be used instead of the scopes advertised by the protected resource.
    /// If not specified, the provider will use the scopes from the protected resource metadata.
    /// </para>
    /// <para>
    /// Common OAuth scopes include "openid", "profile", and "email".
    /// </para>
    /// </remarks>
    public IEnumerable<string>? Scopes { get; set; }

    /// <summary>
    /// Gets or sets the callback that handles the OAuth authorization flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This callback receives the authorization and redirect URIs in an
    /// <see cref="AuthorizationCallbackContext"/> and returns the authorization response.
    /// If neither this callback nor <see cref="AuthorizationRedirectDelegate"/> is specified, a default
    /// implementation prompts the user to enter the full redirect URL manually.
    /// </para>
    /// <para>
    /// Custom implementations might open a browser, start an HTTP listener, or use other mechanisms to capture
    /// the authorization response. They must return the <c>code</c> and <c>state</c> query parameters
    /// from the redirect URI callback. The SDK requires an exact state match before exchanging the code.
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
    /// This delegate returns only the authorization code and cannot provide the <c>state</c> parameter
    /// from the authorization response. Consequently, SDK state validation is skipped when this delegate
    /// is used. Use <see cref="AuthorizationCallbackHandler"/> to enable SDK state validation.
    /// </para>
    /// <para>
    /// Applications that supply their own <c>state</c> through <see cref="AdditionalAuthorizationParameters"/>
    /// must validate the returned state in this delegate.
    /// </para>
    /// <para>
    /// This property cannot be configured together with <see cref="AuthorizationCallbackHandler"/>.
    /// </para>
    /// </remarks>
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
    /// Parameters specified cannot override or append to automatically set parameters like
    /// <c>redirect_uri</c>, which should instead be configured via <see cref="RedirectUri"/>.
    /// The <c>state</c> parameter is also reserved when using <see cref="AuthorizationCallbackHandler"/>
    /// or the default handler, and is generated uniquely for each authorization transaction.
    /// For compatibility, applications using <see cref="AuthorizationRedirectDelegate"/> may supply
    /// their own <c>state</c> value and validate it themselves.
    /// </para>
    /// </remarks>
    public IDictionary<string, string> AdditionalAuthorizationParameters { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets the token cache to use for storing and retrieving tokens beyond the lifetime of the transport.
    /// If none is provided, tokens will be cached with the transport.
    /// </summary>
    public ITokenCache? TokenCache { get; set; }
}
