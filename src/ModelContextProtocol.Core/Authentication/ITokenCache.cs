namespace ModelContextProtocol.Authentication;

/// <summary>
/// Allows the client to cache access tokens beyond the lifetime of the transport.
/// </summary>
public interface ITokenCache
{
    /// <summary>
    /// Cache the token.
    /// </summary>
    Task StoreTokenAsync(TokenContainer token, CancellationToken cancellationToken);

    /// <summary>
    /// Get the cached token.
    /// </summary>
    Task<TokenContainer?> GetTokenAsync(CancellationToken cancellationToken);
}