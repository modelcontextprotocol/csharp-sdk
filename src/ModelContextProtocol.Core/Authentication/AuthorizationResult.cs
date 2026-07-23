namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the result of an OAuth authorization redirect, containing the authorization code
/// and optionally the issuer identifier from the authorization response.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Iss"/> property should be populated from the <c>iss</c> query parameter in the
/// redirect URI when present, as specified by
/// <see href="https://datatracker.ietf.org/doc/html/rfc9207">RFC 9207</see>.
/// This enables the SDK to validate that the authorization response originated from the expected
/// authorization server, mitigating mix-up attacks.
/// </para>
/// </remarks>
public sealed class AuthorizationResult
{
    /// <summary>
    /// Gets the authorization code returned by the authorization server.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Gets the issuer identifier returned in the authorization response per
    /// <see href="https://datatracker.ietf.org/doc/html/rfc9207">RFC 9207</see>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value should be extracted from the <c>iss</c> query parameter of the redirect URI.
    /// When present, the SDK validates it against the expected authorization server issuer to
    /// prevent mix-up attacks.
    /// </para>
    /// <para>
    /// Implementations of <see cref="ClientOAuthOptions.AuthorizationCallbackHandler"/> should populate this
    /// property whenever the <c>iss</c> parameter is present in the redirect URI callback.
    /// </para>
    /// </remarks>
    public string? Iss { get; init; }
}
