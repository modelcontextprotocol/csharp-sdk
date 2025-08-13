using ModelContextProtocol.Server.Authorization;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Unit tests for AuthorizationChallenge class.
/// </summary>
public class AuthorizationChallengeTests
{
    [Fact]
    public void Constructor_WithValidParameters_SetsProperties()
    {
        // Arrange
        const string wwwAuthenticateValue = "Bearer realm=\"test\"";
        const int httpStatusCode = 401;

        // Act
        var challenge = new AuthorizationChallenge(wwwAuthenticateValue, httpStatusCode);

        // Assert
        Assert.Equal(wwwAuthenticateValue, challenge.WwwAuthenticateValue);
        Assert.Equal(httpStatusCode, challenge.HttpStatusCode);
    }

    [Fact]
    public void Constructor_WithDefaultStatusCode_SetsDefault401()
    {
        // Arrange
        const string wwwAuthenticateValue = "Bearer realm=\"test\"";

        // Act
        var challenge = new AuthorizationChallenge(wwwAuthenticateValue);

        // Assert
        Assert.Equal(wwwAuthenticateValue, challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void Constructor_WithNullWwwAuthenticateValue_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthorizationChallenge(null!));
    }

    [Fact]
    public void CreateBearerChallenge_WithNoParameters_ReturnsBasicBearerChallenge()
    {
        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge();

        // Assert
        Assert.Equal("Bearer", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBearerChallenge_WithRealm_ReturnsBearerChallengeWithRealm()
    {
        // Arrange
        const string realm = "test-realm";

        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge(realm: realm);

        // Assert
        Assert.Equal($"Bearer realm=\"{realm}\"", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBearerChallenge_WithScope_ReturnsBearerChallengeWithScope()
    {
        // Arrange
        const string scope = "read:data";

        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge(scope: scope);

        // Assert
        Assert.Equal($"Bearer scope=\"{scope}\"", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBearerChallenge_WithError_ReturnsBearerChallengeWithError()
    {
        // Arrange
        const string error = "invalid_token";

        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge(error: error);

        // Assert
        Assert.Equal($"Bearer error=\"{error}\"", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBearerChallenge_WithErrorDescription_ReturnsBearerChallengeWithErrorDescription()
    {
        // Arrange
        const string errorDescription = "The token has expired";

        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge(errorDescription: errorDescription);

        // Assert
        Assert.Equal($"Bearer error_description=\"{errorDescription}\"", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBearerChallenge_WithAllParameters_ReturnsBearerChallengeWithAllParameters()
    {
        // Arrange
        const string realm = "test-realm";
        const string scope = "read:data write:data";
        const string error = "insufficient_scope";
        const string errorDescription = "The request requires higher privileges";

        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge(realm, scope, error, errorDescription);

        // Assert
        var expected = $"Bearer realm=\"{realm}\", scope=\"{scope}\", error=\"{error}\", error_description=\"{errorDescription}\"";
        Assert.Equal(expected, challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBearerChallenge_WithEmptyStrings_IgnoresEmptyParameters()
    {
        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge("", "", "", "");

        // Assert
        Assert.Equal("Bearer", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBearerChallenge_WithWhitespaceStrings_IgnoresWhitespaceParameters()
    {
        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge("   ", "   ", "   ", "   ");

        // Assert
        Assert.Equal("Bearer", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBasicChallenge_WithRealm_ReturnsBasicChallengeWithRealm()
    {
        // Arrange
        const string realm = "test-realm";

        // Act
        var challenge = AuthorizationChallenge.CreateBasicChallenge(realm);

        // Assert
        Assert.Equal($"Basic realm=\"{realm}\"", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBasicChallenge_WithoutRealm_ReturnsBasicChallenge()
    {
        // Act
        var challenge = AuthorizationChallenge.CreateBasicChallenge();

        // Assert
        Assert.Equal("Basic", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBasicChallenge_WithNullRealm_ReturnsBasicChallenge()
    {
        // Act
        var challenge = AuthorizationChallenge.CreateBasicChallenge(null);

        // Assert
        Assert.Equal("Basic", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateBasicChallenge_WithEmptyRealm_ReturnsBasicChallenge()
    {
        // Act
        var challenge = AuthorizationChallenge.CreateBasicChallenge("");

        // Assert
        Assert.Equal("Basic", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateCustomChallenge_WithSchemeOnly_ReturnsCustomChallenge()
    {
        // Arrange
        const string scheme = "CustomAuth";

        // Act
        var challenge = AuthorizationChallenge.CreateCustomChallenge(scheme);

        // Assert
        Assert.Equal(scheme, challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateCustomChallenge_WithSchemeAndParameters_ReturnsCustomChallengeWithParameters()
    {
        // Arrange
        const string scheme = "CustomAuth";
        var parameters = new[] { ("realm", "test"), ("token", "abc123") };

        // Act
        var challenge = AuthorizationChallenge.CreateCustomChallenge(scheme, parameters);

        // Assert
        Assert.Equal($"{scheme} realm=\"test\", token=\"abc123\"", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateCustomChallenge_WithEmptyParameters_ReturnsSchemeOnly()
    {
        // Arrange
        const string scheme = "CustomAuth";
        var parameters = Array.Empty<(string, string)>();

        // Act
        var challenge = AuthorizationChallenge.CreateCustomChallenge(scheme, parameters);

        // Assert
        Assert.Equal(scheme, challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateCustomChallenge_WithNullParameters_ReturnsSchemeOnly()
    {
        // Arrange
        const string scheme = "CustomAuth";

        // Act
        var challenge = AuthorizationChallenge.CreateCustomChallenge(scheme, null);

        // Assert
        Assert.Equal(scheme, challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateInsufficientScopeChallenge_WithRequiredScope_ReturnsInsufficientScopeChallenge()
    {
        // Arrange
        const string requiredScope = "write:admin";

        // Act
        var challenge = AuthorizationChallenge.CreateInsufficientScopeChallenge(requiredScope);

        // Assert
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains($"scope=\"{requiredScope}\"", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"insufficient_scope\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"Required scope: {requiredScope}", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateInsufficientScopeChallenge_WithRequiredScopeAndRealm_ReturnsInsufficientScopeChallengeWithRealm()
    {
        // Arrange
        const string requiredScope = "write:admin";
        const string realm = "test-realm";

        // Act
        var challenge = AuthorizationChallenge.CreateInsufficientScopeChallenge(requiredScope, realm);

        // Assert
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"scope=\"{requiredScope}\"", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"insufficient_scope\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"Required scope: {requiredScope}", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateInvalidTokenChallenge_WithDefaultParameters_ReturnsInvalidTokenChallenge()
    {
        // Act
        var challenge = AuthorizationChallenge.CreateInvalidTokenChallenge();

        // Assert
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"invalid_token\"", challenge.WwwAuthenticateValue);
        Assert.Contains("expired, revoked, malformed, or invalid", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateInvalidTokenChallenge_WithRealm_ReturnsInvalidTokenChallengeWithRealm()
    {
        // Arrange
        const string realm = "test-realm";

        // Act
        var challenge = AuthorizationChallenge.CreateInvalidTokenChallenge(realm);

        // Assert
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"invalid_token\"", challenge.WwwAuthenticateValue);
        Assert.Contains("expired, revoked, malformed, or invalid", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Fact]
    public void CreateInvalidTokenChallenge_WithCustomErrorDescription_ReturnsInvalidTokenChallengeWithCustomDescription()
    {
        // Arrange
        const string realm = "test-realm";
        const string errorDescription = "Token signature validation failed";

        // Act
        var challenge = AuthorizationChallenge.CreateInvalidTokenChallenge(realm, errorDescription);

        // Assert
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"invalid_token\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"error_description=\"{errorDescription}\"", challenge.WwwAuthenticateValue);
        Assert.DoesNotContain("expired, revoked, malformed, or invalid", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Theory]
    [InlineData("realm with spaces")]
    [InlineData("realm\"with\"quotes")]
    [InlineData("realm,with,commas")]
    public void CreateBearerChallenge_WithSpecialCharactersInRealm_HandlesCorrectly(string realm)
    {
        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge(realm: realm);

        // Assert
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Theory]
    [InlineData("read:data write:data")]
    [InlineData("admin:full")]
    [InlineData("user:profile user:email")]
    public void CreateBearerChallenge_WithVariousScopes_HandlesCorrectly(string scope)
    {
        // Act
        var challenge = AuthorizationChallenge.CreateBearerChallenge(scope: scope);

        // Assert
        Assert.Contains($"scope=\"{scope}\"", challenge.WwwAuthenticateValue);
        Assert.Equal(401, challenge.HttpStatusCode);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public void Constructor_WithVariousStatusCodes_SetsCorrectStatusCode(int statusCode)
    {
        // Arrange
        const string wwwAuthenticateValue = "Bearer";

        // Act
        var challenge = new AuthorizationChallenge(wwwAuthenticateValue, statusCode);

        // Assert
        Assert.Equal(statusCode, challenge.HttpStatusCode);
        Assert.Equal(wwwAuthenticateValue, challenge.WwwAuthenticateValue);
    }
}