
namespace ModelContextProtocol.Authentication;

/// <summary>
/// Caches the token in-memory within this instance.
/// </summary>
internal class InMemoryTokenCache : ITokenCache
{
    private TokenContainer? _tokens;

    /// <summary>
    /// Cache the token.
    /// </summary>
    public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        _tokens = tokens;
        return default;
    }

    /// <summary>
    /// Get the cached token.
    /// </summary>
    public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        return new ValueTask<TokenContainer?>(_tokens);
    }
}