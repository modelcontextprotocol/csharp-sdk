using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ResourceIconTests
{
    [Fact]
    public static void Resource_SerializesToJson_WithIcons()
    {
        var resource = new Resource
        {
            Name = "document.pdf",
            Title = "Important Document",
            Uri = "file:///path/to/document.pdf",
            Description = "An important document",
            MimeType = "application/pdf",
            Icons = new List<Icon>
            {
                new() { Src = "https://example.com/pdf-icon.png", MimeType = "image/png", Sizes = "32x32" }
            }
        };

        string json = JsonSerializer.Serialize(resource);
        var result = JsonSerializer.Deserialize<Resource>(json);

        Assert.Equal("document.pdf", result!.Name);
        Assert.Equal("Important Document", result.Title);
        Assert.Equal("file:///path/to/document.pdf", result.Uri);
        Assert.Equal("An important document", result.Description);
        Assert.Equal("application/pdf", result.MimeType);
        Assert.NotNull(result.Icons);
        Assert.Single(result.Icons);
        Assert.Equal("https://example.com/pdf-icon.png", result.Icons[0].Src);
    }

    [Fact]
    public static void Resource_SerializesToJson_WithoutIcons()
    {
        var resource = new Resource
        {
            Name = "data.json",
            Uri = "file:///path/to/data.json",
            MimeType = "application/json"
        };

        string json = JsonSerializer.Serialize(resource);
        var result = JsonSerializer.Deserialize<Resource>(json);

        Assert.Equal("data.json", result!.Name);
        Assert.Equal("file:///path/to/data.json", result.Uri);
        Assert.Equal("application/json", result.MimeType);
        Assert.Null(result.Icons);
    }

    [Fact]
    public static void Resource_IconsProperty_HasCorrectJsonPropertyName()
    {
        var resource = new Resource
        {
            Name = "test_resource",
            Uri = "file:///test",
            Icons = new List<Icon> { new() { Src = "https://example.com/icon.svg" } }
        };

        string json = JsonSerializer.Serialize(resource);
        Assert.Contains("\"icons\":", json);
    }
}

public static class PromptIconTests
{
    [Fact]
    public static void Prompt_SerializesToJson_WithIcons()
    {
        var prompt = new Prompt
        {
            Name = "code_review",
            Title = "Code Review Prompt",
            Description = "Review the provided code",
            Icons = new List<Icon>
            {
                new() { Src = "https://example.com/review-icon.svg", MimeType = "image/svg+xml", Sizes = "any" }
            }
        };

        string json = JsonSerializer.Serialize(prompt);
        var result = JsonSerializer.Deserialize<Prompt>(json);

        Assert.Equal("code_review", result!.Name);
        Assert.Equal("Code Review Prompt", result.Title);
        Assert.Equal("Review the provided code", result.Description);
        Assert.NotNull(result.Icons);
        Assert.Single(result.Icons);
        Assert.Equal("https://example.com/review-icon.svg", result.Icons[0].Src);
        Assert.Equal("image/svg+xml", result.Icons[0].MimeType);
        Assert.Equal("any", result.Icons[0].Sizes);
    }

    [Fact]
    public static void Prompt_SerializesToJson_WithoutIcons()
    {
        var prompt = new Prompt
        {
            Name = "simple_prompt",
            Description = "A simple prompt"
        };

        string json = JsonSerializer.Serialize(prompt);
        var result = JsonSerializer.Deserialize<Prompt>(json);

        Assert.Equal("simple_prompt", result!.Name);
        Assert.Equal("A simple prompt", result.Description);
        Assert.Null(result.Icons);
    }

    [Fact]
    public static void Prompt_IconsProperty_HasCorrectJsonPropertyName()
    {
        var prompt = new Prompt
        {
            Name = "test_prompt",
            Icons = new List<Icon> { new() { Src = "https://example.com/icon.webp" } }
        };

        string json = JsonSerializer.Serialize(prompt);
        Assert.Contains("\"icons\":", json);
    }
}