using System.Security.Cryptography;
using System.Text;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Provides utility methods for OAuth operations.
/// </summary>
internal static class OAuthUtils
{
    /// <summary>
    /// Generates a random token for authorization code or refresh token.
    /// </summary>
    /// <returns>A Base64Url encoded random token.</returns>
    public static string GenerateRandomToken()
    {
        var bytes = new byte[32];
        using (var randomNumberGenerator = RandomNumberGenerator.Create())
        {
            randomNumberGenerator.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Verifies a PKCE code challenge against a code verifier.
    /// </summary>
    /// <param name="codeVerifier">The code verifier to verify.</param>
    /// <param name="codeChallenge">The code challenge to verify against.</param>
    /// <returns>True if the code challenge is valid, false otherwise.</returns>
    public static bool VerifyCodeChallenge(string codeVerifier, string codeChallenge)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        var computedChallenge = Convert.ToBase64String(challengeBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return computedChallenge == codeChallenge;
    }

    /// <summary>
    /// Encodes a byte array to a Base64Url string.
    /// </summary>
    /// <param name="data">The data to encode.</param>
    /// <returns>A Base64Url encoded string.</returns>
    public static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decodes a Base64Url string to a byte array.
    /// </summary>
    /// <param name="base64Url">The Base64Url encoded string.</param>
    /// <returns>The decoded byte array.</returns>
    public static byte[] Base64UrlDecode(string base64Url)
    {
        string base64 = base64Url.Replace('-', '+').Replace('_', '/');

        // Add padding if needed
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        return Convert.FromBase64String(base64);
    }
}