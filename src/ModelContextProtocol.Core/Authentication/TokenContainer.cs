namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a cacheable combination of tokens ready to be used for authentication.
/// </summary>
public sealed class TokenContainer
{
    /// <summary>
    /// Gets or sets the token type (typically "Bearer").
    /// </summary>
    public required string TokenType { get; set; }

    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the number of seconds until the access token expires.
    /// </summary>
    public int? ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the scope of the access token.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the token was obtained.
    /// </summary>
    public required DateTimeOffset ObtainedAt { get; set; }

    /// <summary>
    /// Gets or sets the OAuth client ID that these tokens were issued to.
    /// </summary>
    /// <remarks>
    /// This is persisted alongside the tokens so that a durable <see cref="ITokenCache"/> can survive a
    /// process restart: on a cold start the client ID is restored from the cache, allowing a persisted
    /// <see cref="RefreshToken"/> to be used without re-running dynamic client registration or prompting
    /// the user to re-authorize. It reflects the client ID currently in use, whether that was obtained via
    /// dynamic client registration, a client-id metadata document, or configured explicitly. On a cold
    /// start it is only restored when no client ID has been configured, so an explicitly configured client
    /// ID always takes precedence.
    /// </remarks>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the OAuth client secret that these tokens were issued to, if any.
    /// </summary>
    /// <remarks>
    /// This is persisted alongside <see cref="ClientId"/> so a durable <see cref="ITokenCache"/> can use a
    /// persisted <see cref="RefreshToken"/> after a restart. It is only populated when a client secret was
    /// issued (for example via dynamic client registration).
    /// <para>
    /// Security: persisting this means a durable <see cref="ITokenCache"/> stores a confidential client
    /// credential, not just the refresh token (which for a confidential client is not usable on its own).
    /// Cache implementations that persist to durable storage must protect these values at rest (for
    /// example with OS-level encryption or a dedicated secret store); otherwise a cache compromise would
    /// expose a complete, usable credential set.
    /// </para>
    /// </remarks>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the token endpoint authentication method associated with <see cref="ClientId"/>.
    /// </summary>
    /// <remarks>
    /// This is persisted alongside <see cref="ClientId"/> so a refresh performed on a cold start uses the
    /// same authentication method that was negotiated when the client was registered (for example
    /// <c>none</c> for a public client rather than the default <c>client_secret_post</c>).
    /// </remarks>
    public string? TokenEndpointAuthMethod { get; set; }

    internal bool IsExpired => ExpiresIn is not null && DateTimeOffset.UtcNow >= ObtainedAt.AddSeconds(ExpiresIn.Value);
}
