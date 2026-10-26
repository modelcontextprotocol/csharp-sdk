namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides subject token type identifiers supported by the Identity Assertion Authorization Grant flow.
/// </summary>
public static class IdentityAssertionGrantSubjectTokenTypes
{
    /// <summary>
    /// The RFC 8693 token type identifier for an OpenID Connect ID token.
    /// </summary>
    public const string IdToken = "urn:ietf:params:oauth:token-type:id_token";

    /// <summary>
    /// The RFC 8693 token type identifier for a SAML 2.0 assertion.
    /// </summary>
    public const string Saml2 = "urn:ietf:params:oauth:token-type:saml2";
}
