using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;

namespace ModelContextProtocol.AspNetCore.Authentication;

internal static class McpOAuthProxyUtilities
{
    public static string CreateRandomToken(int byteCount = 32) =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    public static string CreateCodeChallenge(string verifier) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public static bool IsSecureEndpoint(Uri uri) =>
        uri.IsAbsoluteUri &&
        (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
         (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback));

    public static bool IsValidRedirectUri(string value)
    {
        if (value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback);
    }

    public static string[] ParseScopes(string? scope) =>
        string.IsNullOrWhiteSpace(scope)
            ? []
            : scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    public static bool IsValidCodeChallenge(string value) =>
        value.Length == 43 && value.All(IsBase64UrlCharacter);

    public static bool IsValidCodeVerifier(string value) =>
        value.Length is >= 43 and <= 128 && value.All(IsPkceVerifierCharacter);

    public static bool IsValidOpaqueToken(string value) =>
        value.Length == 43 && value.All(IsBase64UrlCharacter);

    public static bool IsValidClientId(string value) =>
        value.StartsWith("mcp_", StringComparison.Ordinal) && IsValidOpaqueToken(value[4..]);

    public static Uri AppendPath(Uri baseUri, string path)
    {
        var builder = new UriBuilder(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/{path.TrimStart('/')}"
        };
        return builder.Uri;
    }

    private static bool IsBase64UrlCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '_';

    private static bool IsPkceVerifierCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '.' or '_' or '~';
}
