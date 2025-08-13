using ModelContextProtocol.Server.Authorization;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Unit tests for AuthorizationResult and AuthorizationChallenge classes.
/// </summary>
public class AuthorizationResultTests
{
    [Fact]
    public void Constructor_WithValidParameters_SetsProperties()
    {
        // Arrange
        const bool isAuthorized = true;
        const string reason = "Test reason";
        const string additionalData = "Test data";

        // Act
        var result = new AuthorizationResult(isAuthorized, reason, additionalData);

        // Assert
        Assert.Equal(isAuthorized, result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(additionalData, result.AdditionalData);
    }

    [Fact]
    public void Constructor_WithMinimalParameters_SetsDefaults()
    {
        // Arrange & Act
        var result = new AuthorizationResult(true);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Null(result.Reason);
        Assert.Null(result.AdditionalData);
    }

    [Fact]
    public void Allow_WithoutParameters_ReturnsAuthorizedResult()
    {
        // Act
        var result = AuthorizationResult.Allow();

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Null(result.Reason);
        Assert.Null(result.AdditionalData);
    }

    [Fact]
    public void Allow_WithReason_ReturnsAuthorizedResultWithReason()
    {
        // Arrange
        const string reason = "User has permission";

        // Act
        var result = AuthorizationResult.Allow(reason);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.Null(result.AdditionalData);
    }

    [Fact]
    public void Allow_WithReasonAndData_ReturnsAuthorizedResultWithReasonAndData()
    {
        // Arrange
        const string reason = "User has permission";
        const string data = "Additional context";

        // Act
        var result = AuthorizationResult.Allow(reason, data);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(data, result.AdditionalData);
    }

    [Fact]
    public void Deny_WithReason_ReturnsDeniedResultWithReason()
    {
        // Arrange
        const string reason = "Insufficient permissions";

        // Act
        var result = AuthorizationResult.Deny(reason);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.Null(result.AdditionalData);
    }

    [Fact]
    public void Deny_WithReasonAndData_ReturnsDeniedResultWithReasonAndData()
    {
        // Arrange
        const string reason = "Insufficient permissions";
        const string data = "Additional context";

        // Act
        var result = AuthorizationResult.Deny(reason, data);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(data, result.AdditionalData);
    }

    [Fact]
    public void Deny_WithOnlyData_ReturnsDeniedResultWithDefaultReason()
    {
        // Arrange
        const string data = "Additional context";

        // Act
        var result = AuthorizationResult.Deny(data);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("Access denied", result.Reason);
        Assert.Equal(data, result.AdditionalData);
    }

    [Fact]
    public void DenyWithChallenge_WithReasonAndChallenge_ReturnsDeniedResultWithChallenge()
    {
        // Arrange
        const string reason = "Authentication required";
        var challenge = AuthorizationChallenge.CreateBearerChallenge("test-realm");

        // Act
        var result = AuthorizationResult.DenyWithChallenge(reason, challenge);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(challenge, result.AdditionalData);
    }

    [Fact]
    public void DenyWithBearerChallenge_WithBasicParameters_ReturnsDeniedResultWithBearerChallenge()
    {
        // Arrange
        const string reason = "Invalid token";
        const string realm = "test-realm";

        // Act
        var result = AuthorizationResult.DenyWithBearerChallenge(reason, realm);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.IsType<AuthorizationChallenge>(result.AdditionalData);

        var challenge = (AuthorizationChallenge)result.AdditionalData;
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
    }

    [Fact]
    public void DenyWithBearerChallenge_WithAllParameters_ReturnsDeniedResultWithFullBearerChallenge()
    {
        // Arrange
        const string reason = "Insufficient scope";
        const string realm = "test-realm";
        const string scope = "read:data";
        const string error = "insufficient_scope";
        const string errorDescription = "Token lacks required scope";

        // Act
        var result = AuthorizationResult.DenyWithBearerChallenge(reason, realm, scope, error, errorDescription);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.IsType<AuthorizationChallenge>(result.AdditionalData);

        var challenge = (AuthorizationChallenge)result.AdditionalData;
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"scope=\"{scope}\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"error=\"{error}\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"error_description=\"{errorDescription}\"", challenge.WwwAuthenticateValue);
    }

    [Fact]
    public void DenyWithBasicChallenge_WithRealm_ReturnsDeniedResultWithBasicChallenge()
    {
        // Arrange
        const string reason = "Authentication required";
        const string realm = "test-realm";

        // Act
        var result = AuthorizationResult.DenyWithBasicChallenge(reason, realm);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.IsType<AuthorizationChallenge>(result.AdditionalData);

        var challenge = (AuthorizationChallenge)result.AdditionalData;
        Assert.Contains("Basic", challenge.WwwAuthenticateValue);
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
    }

    [Fact]
    public void DenyWithBasicChallenge_WithoutRealm_ReturnsDeniedResultWithBasicChallenge()
    {
        // Arrange
        const string reason = "Authentication required";

        // Act
        var result = AuthorizationResult.DenyWithBasicChallenge(reason);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal(reason, result.Reason);
        Assert.IsType<AuthorizationChallenge>(result.AdditionalData);

        var challenge = (AuthorizationChallenge)result.AdditionalData;
        Assert.Equal("Basic", challenge.WwwAuthenticateValue);
    }

    [Fact]
    public void DenyInsufficientScope_WithRequiredScope_ReturnsDeniedResultWithInsufficientScopeChallenge()
    {
        // Arrange
        const string requiredScope = "write:admin";
        const string realm = "test-realm";

        // Act
        var result = AuthorizationResult.DenyInsufficientScope(requiredScope, realm);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("Insufficient scope", result.Reason);
        Assert.Contains(requiredScope, result.Reason);
        Assert.IsType<AuthorizationChallenge>(result.AdditionalData);

        var challenge = (AuthorizationChallenge)result.AdditionalData;
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains($"scope=\"{requiredScope}\"", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"insufficient_scope\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
    }

    [Fact]
    public void DenyInsufficientScope_WithoutRealm_ReturnsDeniedResultWithInsufficientScopeChallenge()
    {
        // Arrange
        const string requiredScope = "write:admin";

        // Act
        var result = AuthorizationResult.DenyInsufficientScope(requiredScope);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("Insufficient scope", result.Reason);
        Assert.Contains(requiredScope, result.Reason);
        Assert.IsType<AuthorizationChallenge>(result.AdditionalData);

        var challenge = (AuthorizationChallenge)result.AdditionalData;
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains($"scope=\"{requiredScope}\"", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"insufficient_scope\"", challenge.WwwAuthenticateValue);
        Assert.DoesNotContain("realm=", challenge.WwwAuthenticateValue);
    }

    [Fact]
    public void DenyInvalidToken_WithRealm_ReturnsDeniedResultWithInvalidTokenChallenge()
    {
        // Arrange
        const string realm = "test-realm";

        // Act
        var result = AuthorizationResult.DenyInvalidToken(realm);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("Invalid or expired token", result.Reason);
        Assert.IsType<AuthorizationChallenge>(result.AdditionalData);

        var challenge = (AuthorizationChallenge)result.AdditionalData;
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"invalid_token\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
    }

    [Fact]
    public void DenyInvalidToken_WithCustomErrorDescription_ReturnsDeniedResultWithCustomDescription()
    {
        // Arrange
        const string realm = "test-realm";
        const string errorDescription = "Token has expired";

        // Act
        var result = AuthorizationResult.DenyInvalidToken(realm, errorDescription);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("Invalid or expired token", result.Reason);
        Assert.IsType<AuthorizationChallenge>(result.AdditionalData);

        var challenge = (AuthorizationChallenge)result.AdditionalData;
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"invalid_token\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"error_description=\"{errorDescription}\"", challenge.WwwAuthenticateValue);
        Assert.Contains($"realm=\"{realm}\"", challenge.WwwAuthenticateValue);
    }

    [Fact]
    public void DenyInvalidToken_WithoutParameters_ReturnsDeniedResultWithDefaultInvalidTokenChallenge()
    {
        // Act
        var result = AuthorizationResult.DenyInvalidToken();

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("Invalid or expired token", result.Reason);
        Assert.IsType<AuthorizationChallenge>(result.AdditionalData);

        var challenge = (AuthorizationChallenge)result.AdditionalData;
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"invalid_token\"", challenge.WwwAuthenticateValue);
        Assert.DoesNotContain("realm=", challenge.WwwAuthenticateValue);
    }

    [Theory]
    [InlineData(true, "Success", "Authorized: Success")]
    [InlineData(true, null, "Authorized")]
    [InlineData(false, "Failed", "Denied: Failed")]
    [InlineData(false, null, "Denied")]
    public void ToString_WithVariousStates_ReturnsExpectedString(bool isAuthorized, string? reason, string expected)
    {
        // Arrange
        var result = new AuthorizationResult(isAuthorized, reason);

        // Act
        var toString = result.ToString();

        // Assert
        Assert.Equal(expected, toString);
    }
}