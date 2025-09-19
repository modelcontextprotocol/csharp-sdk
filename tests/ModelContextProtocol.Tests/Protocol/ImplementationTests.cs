using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ImplementationTests
{
    [Fact]
    public static void Implementation_SerializesToJson_WithAllProperties()
    {
        var implementation = new Implementation
        {
            Name = "test-server",
            Title = "Test MCP Server",
            Version = "1.0.0",
            Icons = new List<Icon>
            {
                new() { Src = "https://example.com/icon.png", MimeType = "image/png", Sizes = "48x48" },
                new() { Src = "https://example.com/icon.svg", MimeType = "image/svg+xml", Sizes = "any" }
            },
            WebsiteUrl = "https://example.com"
        };

        string json = JsonSerializer.Serialize(implementation);
        var result = JsonSerializer.Deserialize<Implementation>(json);

        Assert.Equal("test-server", result!.Name);
        Assert.Equal("Test MCP Server", result.Title);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("https://example.com", result.WebsiteUrl);
        Assert.NotNull(result.Icons);
        Assert.Equal(2, result.Icons.Count);
        Assert.Equal("https://example.com/icon.png", result.Icons[0].Src);
        Assert.Equal("https://example.com/icon.svg", result.Icons[1].Src);
    }

    [Fact]
    public static void Implementation_SerializesToJson_WithoutOptionalProperties()
    {
        var implementation = new Implementation
        {
            Name = "simple-server",
            Version = "1.0.0"
        };

        string json = JsonSerializer.Serialize(implementation);
        var result = JsonSerializer.Deserialize<Implementation>(json);

        Assert.Equal("simple-server", result!.Name);
        Assert.Null(result.Title);
        Assert.Equal("1.0.0", result.Version);
        Assert.Null(result.Icons);
        Assert.Null(result.WebsiteUrl);
    }

    [Fact]
    public static void Implementation_HasCorrectJsonPropertyNames()
    {
        var implementation = new Implementation
        {
            Name = "test-server",
            Title = "Test Server",
            Version = "1.0.0",
            Icons = new List<Icon> { new() { Src = "https://example.com/icon.png" } },
            WebsiteUrl = "https://example.com"
        };

        string json = JsonSerializer.Serialize(implementation);

        Assert.Contains("\"name\":", json);
        Assert.Contains("\"title\":", json);
        Assert.Contains("\"version\":", json);
        Assert.Contains("\"icons\":", json);
        Assert.Contains("\"websiteUrl\":", json);
    }

    [Fact]
    public static void Implementation_EmptyIconsList_SerializesAsEmptyArray()
    {
        var implementation = new Implementation
        {
            Name = "test-server",
            Version = "1.0.0",
            Icons = new List<Icon>()
        };

        string json = JsonSerializer.Serialize(implementation);
        var result = JsonSerializer.Deserialize<Implementation>(json);

        Assert.NotNull(result!.Icons);
        Assert.Empty(result.Icons);
    }
}