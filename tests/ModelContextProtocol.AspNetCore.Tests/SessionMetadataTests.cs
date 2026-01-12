namespace ModelContextProtocol.AspNetCore.Tests;

public class SessionMetadataTests
{
    [Fact]
    public void SessionMetadata_RequiresSessionId()
    {
        var metadata = new SessionMetadata
        {
            SessionId = "test-session-id"
        };

        Assert.Equal("test-session-id", metadata.SessionId);
    }

    [Fact]
    public void SessionMetadata_OptionalPropertiesAreNullByDefault()
    {
        var metadata = new SessionMetadata
        {
            SessionId = "test-session"
        };

        Assert.Null(metadata.UserIdClaimType);
        Assert.Null(metadata.UserIdClaimValue);
        Assert.Null(metadata.UserIdClaimIssuer);
        Assert.Null(metadata.CustomDataJson);
    }

    [Fact]
    public void SessionMetadata_StoresUserIdClaims()
    {
        var metadata = new SessionMetadata
        {
            SessionId = "test-session",
            UserIdClaimType = "sub",
            UserIdClaimValue = "user-123",
            UserIdClaimIssuer = "https://issuer.example.com"
        };

        Assert.Equal("sub", metadata.UserIdClaimType);
        Assert.Equal("user-123", metadata.UserIdClaimValue);
        Assert.Equal("https://issuer.example.com", metadata.UserIdClaimIssuer);
    }

    [Fact]
    public void SessionMetadata_StoresTimestamps()
    {
        var createdAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var lastActivity = new DateTime(2025, 1, 1, 12, 30, 0, DateTimeKind.Utc);

        var metadata = new SessionMetadata
        {
            SessionId = "test-session",
            CreatedAtUtc = createdAt,
            LastActivityUtc = lastActivity
        };

        Assert.Equal(createdAt, metadata.CreatedAtUtc);
        Assert.Equal(lastActivity, metadata.LastActivityUtc);
    }

    [Fact]
    public void SessionMetadata_StoresCustomData()
    {
        var customJson = """{"key": "value", "count": 42}""";

        var metadata = new SessionMetadata
        {
            SessionId = "test-session",
            CustomDataJson = customJson
        };

        Assert.Equal(customJson, metadata.CustomDataJson);
    }

    [Fact]
    public void SessionMetadata_CanRepresentAnonymousSession()
    {
        // Anonymous sessions have no user claims
        var metadata = new SessionMetadata
        {
            SessionId = "anonymous-session",
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityUtc = DateTime.UtcNow
        };

        Assert.Null(metadata.UserIdClaimValue);
        Assert.NotEmpty(metadata.SessionId);
    }

    [Fact]
    public void SessionMetadata_CanRepresentAuthenticatedSession()
    {
        var metadata = new SessionMetadata
        {
            SessionId = "authenticated-session",
            UserIdClaimType = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
            UserIdClaimValue = "user@example.com",
            UserIdClaimIssuer = "local",
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityUtc = DateTime.UtcNow
        };

        Assert.NotNull(metadata.UserIdClaimValue);
        Assert.Equal("user@example.com", metadata.UserIdClaimValue);
    }
}
