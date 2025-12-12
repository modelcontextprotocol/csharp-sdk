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
    /// Gets or sets the authorization redirect delegate for handling the OAuth authorization flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This delegate is responsible for handling the OAuth authorization URL and obtaining the authorization code.
    /// If not specified, a default implementation will be used that prompts the user to enter the code manually.
    /// </para>
    /// <para>
    /// Custom implementations might open a browser, start an HTTP listener, or use other mechanisms to capture
    /// the authorization code from the OAuth redirect.
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
