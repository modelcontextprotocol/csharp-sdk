namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides configuration options for the <see cref="ClientOAuthProvider"/> related to dynamic client registration (RFC 7591).
/// </summary>
public sealed class DynamicClientRegistrationOptions
{
    /// <summary>
    /// Gets or sets the client name to use during dynamic client registration.
    /// </summary>
    /// <remarks>
    /// This value is a human-readable name for the client that can be displayed to users during authorization.
    /// </remarks>
    public string? ClientName { get; set; }

    /// <summary>
    /// Gets or sets the client URI to use during dynamic client registration.
    /// </summary>
    /// <remarks>
    /// This value should be a URL pointing to the client's home page or information page.
    /// </remarks>
    public Uri? ClientUri { get; set; }

    /// <summary>
    /// Gets or sets the initial access token to use during dynamic client registration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This token is used to authenticate the client during the registration process.
    /// </para>
    /// <para>
    /// This token is required if the authorization server does not allow anonymous client registration.
    /// </para>
    /// </remarks>
    public string? InitialAccessToken { get; set; }

    /// <summary>
    /// Gets or sets the OIDC <c>application_type</c> sent during dynamic client registration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see langword="null"/>, the SDK infers the value from the configured
    /// <see cref="ClientOAuthOptions.RedirectUri"/>: loopback hosts (<c>localhost</c>,
    /// <c>127.0.0.1</c>, <c>[::1]</c>) and custom-scheme URIs map to <c>"native"</c>; remote
    /// <c>https://</c> URIs map to <c>"web"</c>.
    /// </para>
    /// <para>
    /// When set explicitly, the value is validated against the inferred type. A conflicting
    /// explicit value (for example <c>"web"</c> with a localhost redirect URI) causes the
    /// <see cref="ClientOAuthProvider"/> constructor to throw <see cref="ArgumentException"/>.
    /// </para>
    /// <para>
    /// This validation mirrors the OpenID Connect Dynamic Client Registration coupling between
    /// <c>application_type</c> and <c>redirect_uris</c>: <c>"web"</c> clients must use remote
    /// <c>https</c> redirect URIs, while <c>"native"</c> clients must use loopback or custom-scheme
    /// URIs. A conflicting combination is therefore rejected rather than sent, since a conformant
    /// authorization server would reject the registration anyway.
    /// </para>
    /// </remarks>
    public string? ApplicationType { get; set; }

    /// <summary>
    /// Gets or sets the delegate used for handling the dynamic client registration response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This delegate is responsible for processing the response from the dynamic client registration endpoint.
    /// </para>
    /// <para>
    /// The implementation should save the client credentials securely for future use.
    /// </para>
    /// </remarks>
    public Func<DynamicClientRegistrationResponse, CancellationToken, Task>? ResponseDelegate { get; set; }
}
