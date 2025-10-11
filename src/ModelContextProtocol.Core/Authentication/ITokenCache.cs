namespace ModelContextProtocol.Authentication;

/// <summary>
/// Allows the client to cache access tokens beyond the lifetime of the transport.
/// </summary>
public interface ITokenCache
{
    /// <summary>
    /// Cache the token. After a new access token is acquired, this method is invoked to store it.
    /// </summary>
    ValueTask StoreTokenAsync(TokenContainerCacheable token, CancellationToken cancellationToken);

    /// <summary>
    /// Get the cached token. This method is invoked for every request.
    /// </summary>
    ValueTask<TokenContainerCacheable?> GetTokenAsync(CancellationToken cancellationToken);
}