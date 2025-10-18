namespace ModelContextProtocol.Authentication;

internal static class TokenContainerConvert
{
    internal static TokenContainer ForUse(this TokenContainerCacheable token) => new()
    {
        AccessToken = token.AccessToken,
        RefreshToken = token.RefreshToken,
        ExpiresIn = token.ExpiresIn,
        ExtExpiresIn = token.ExtExpiresIn,
        TokenType = token.TokenType,
        Scope = token.Scope,
        ObtainedAt = token.ObtainedAt,
    };

    internal static TokenContainerCacheable ForCache(this TokenContainer token) => new()
    {
        AccessToken = token.AccessToken,
        RefreshToken = token.RefreshToken,
        ExpiresIn = token.ExpiresIn,
        ExtExpiresIn = token.ExtExpiresIn,
        TokenType = token.TokenType,
        Scope = token.Scope,
        ObtainedAt = token.ObtainedAt,
    };
}
