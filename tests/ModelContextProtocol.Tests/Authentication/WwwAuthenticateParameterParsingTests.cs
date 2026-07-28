using System.Reflection;

namespace ModelContextProtocol.Tests.Authentication;

// RFC 9110 section 11.6.1 / section 5.6.4 quoted-string parsing for WWW-Authenticate auth-params.
// See https://github.com/modelcontextprotocol/csharp-sdk/issues/1088.
//
// ClientOAuthProvider is internal and ParseWwwAuthenticateParameters is private, so it's invoked
// via reflection here rather than through the public API surface (which would require driving a
// full OAuth challenge/metadata-fetch flow just to exercise a pure string-parsing helper).
public class WwwAuthenticateParameterParsingTests
{
    private static readonly MethodInfo ParseMethod = Type
        .GetType("ModelContextProtocol.Authentication.ClientOAuthProvider, ModelContextProtocol.Core")!
        .GetMethod("ParseWwwAuthenticateParameters", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static string? Parse(string parameters, string parameterName) =>
        (string?)ParseMethod.Invoke(null, [parameters, parameterName]);

    [Theory]
    [InlineData(
        "error=\"invalid_token\", error_description=\"The access token expired\"",
        "error", "invalid_token")]
    [InlineData(
        "error=\"invalid_token\", error_description=\"The access token expired\"",
        "error_description", "The access token expired")]
    [InlineData(
        "resource_metadata=\"https://example.com/.well-known/oauth-protected-resource\"",
        "resource_metadata", "https://example.com/.well-known/oauth-protected-resource")]
    // A comma inside a quoted value must not split the parameter.
    [InlineData(
        "error_description=\"expired, try again\", resource_metadata=\"https://example.com/x\"",
        "resource_metadata", "https://example.com/x")]
    [InlineData(
        "error_description=\"expired, try again\", resource_metadata=\"https://example.com/x\"",
        "error_description", "expired, try again")]
    // The exact repro from the issue: a quoted value that is only a comma.
    [InlineData("scope=\",\"", "scope", ",")]
    // Escaped double quote inside a quoted value.
    [InlineData("scope=\"read \\\"write\\\" execute\"", "scope", "read \"write\" execute")]
    // Escaped backslash inside a quoted value.
    [InlineData("scope=\"a\\\\b\"", "scope", "a\\b")]
    // Extra whitespace around '=' and ',' (BWS / OWS).
    [InlineData("error = \"invalid_token\" , resource_metadata = \"https://x\"", "resource_metadata", "https://x")]
    // Unquoted token value is legal per the auth-param grammar (token / quoted-string).
    [InlineData("resource_metadata=https://example.com/x", "resource_metadata", "https://example.com/x")]
    // Missing parameter returns null.
    [InlineData("error=\"invalid_token\"", "resource_metadata", null)]
    public void ParseWwwAuthenticateParameters_HandlesRfc9110QuotedStrings(
        string parameters, string parameterName, string? expected)
    {
        var actual = Parse(parameters, parameterName);

        Assert.Equal(expected, actual);
    }
}
