using System.Diagnostics.CodeAnalysis;

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

    /// <summary>
    /// Gets or sets the application type to use during dynamic client registration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Valid values are "native" and "web". If not specified, the application type will be
    /// automatically determined based on the redirect URI: "native" for localhost/127.0.0.1
    /// redirect URIs, "web" for all others.
    /// </para>
    /// <para>
    /// Per the MCP specification, native applications (desktop, mobile, CLI, localhost web apps)
    /// should use "native", and web applications (remote browser-based) should use "web".
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.DcrApplicationType_DiagnosticId, UrlFormat = Experimentals.DcrApplicationType_Url)]
    public string? ApplicationType { get; set; }
}
