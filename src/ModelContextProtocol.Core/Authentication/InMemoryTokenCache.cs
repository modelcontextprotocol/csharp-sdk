
namespace ModelContextProtocol.Authentication;

/// <summary>
/// Caches the token in-memory within this instance.
/// </summary>
internal class InMemoryTokenCache : ITokenCache
{
    private TokenContainerCacheable? _token;

    /// <summary>
    /// Cache the token.
    /// </summary>
    public ValueTask StoreTokenAsync(TokenContainerCacheable token, CancellationToken cancellationToken)
    {
        _token = token;
        return default;
    }

    /// <summary>
    /// Get the cached token.
    /// </summary>
    public ValueTask<TokenContainerCacheable?> GetTokenAsync(CancellationToken cancellationToken)
    {
        return new ValueTask<TokenContainerCacheable?>(_token);
    }
}