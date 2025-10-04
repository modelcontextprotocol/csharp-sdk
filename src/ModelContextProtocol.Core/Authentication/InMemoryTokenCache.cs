
namespace ModelContextProtocol.Authentication;

/// <summary>
/// Caches the token in-memory within this instance.
/// </summary>
internal class InMemoryTokenCache : ITokenCache
{
    private TokenContainer? _token;

    /// <summary>
    /// Cache the token.
    /// </summary>
    public Task StoreTokenAsync(TokenContainer token, CancellationToken cancellationToken)
    {
        _token = token;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get the cached token.
    /// </summary>
    public Task<TokenContainer?> GetTokenAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_token);
    }
}