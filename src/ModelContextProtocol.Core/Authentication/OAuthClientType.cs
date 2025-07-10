namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the type of OAuth client.
/// </summary>
public enum OAuthClientType
{
    /// <summary>
    /// A confidential client, typically a server-side application that can securely store credentials.
    /// </summary>
    Confidential,

    /// <summary>
    /// A public client, typically a client-side application that cannot securely store credentials.
    /// </summary>
    Public,
}