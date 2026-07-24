namespace ModelContextProtocol.Authentication;

/// <summary>
/// Allows the client to cache access tokens beyond the lifetime of the transport.
/// </summary>
/// <remarks>
/// Implementations must be safe for concurrent use. A single cache instance may be shared by multiple
/// in-flight requests, and <see cref="GetTokensAsync"/> in particular can be invoked concurrently
/// (it is called on the request hot path without holding the provider's token-acquisition lock).
/// </remarks>
public interface ITokenCache
{
    /// <summary>
    /// Cache the token. After a new access token is acquired, this method is invoked to store it.
    /// </summary>
    ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken);

    /// <summary>
    /// Get the cached token. This method is invoked for every request.
    /// </summary>
    ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken);
}