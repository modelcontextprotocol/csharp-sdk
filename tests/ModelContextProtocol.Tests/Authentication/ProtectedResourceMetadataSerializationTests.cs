using System.Text.Json;
using ModelContextProtocol.Authentication;

namespace ModelContextProtocol.Tests.Authentication;

public class ProtectedResourceMetadataSerializationTests
{
    [Fact]
    public void EmptyAuthorizationServersAndScopes_AreOmitted()
    {
        // RFC 9728 §3.2: parameters with zero values MUST be omitted from the response.
        var metadata = new ProtectedResourceMetadata { Resource = "https://mcp.example.com" };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(metadata, McpJsonUtilities.DefaultOptions));

        Assert.False(json.RootElement.TryGetProperty("authorization_servers", out _));
        Assert.False(json.RootElement.TryGetProperty("scopes_supported", out _));
    }

    [Fact]
    public void EmptyBearerMethods_AreKept()
    {
        // RFC 9728 §2: "The empty array [] can be used to indicate that no bearer methods are supported."
        var metadata = new ProtectedResourceMetadata { Resource = "https://mcp.example.com", BearerMethodsSupported = [] };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(metadata, McpJsonUtilities.DefaultOptions));

        Assert.Equal(0, json.RootElement.GetProperty("bearer_methods_supported").GetArrayLength());
    }

    [Fact]
    public void AuthorizationServersAndScopes_RoundTrip()
    {
        var metadata = new ProtectedResourceMetadata
        {
            Resource = "https://mcp.example.com",
            AuthorizationServers = ["https://auth.example.com"],
            ScopesSupported = ["files:read"],
        };

        var roundTripped = JsonSerializer.Deserialize<ProtectedResourceMetadata>(
            JsonSerializer.Serialize(metadata, McpJsonUtilities.DefaultOptions), McpJsonUtilities.DefaultOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(["https://auth.example.com"], roundTripped.AuthorizationServers);
        Assert.Equal(["files:read"], roundTripped.ScopesSupported);
    }

    [Fact]
    public void MissingAuthorizationServersAndScopes_DeserializeAsEmpty()
    {
        var metadata = JsonSerializer.Deserialize<ProtectedResourceMetadata>(
            """{"resource":"https://mcp.example.com"}""", McpJsonUtilities.DefaultOptions);

        Assert.NotNull(metadata);
        Assert.Empty(metadata.AuthorizationServers);
        Assert.Empty(metadata.ScopesSupported);
    }
}
