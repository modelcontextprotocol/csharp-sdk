using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class PromptTests
{
    [Fact]
    public static void Prompt_SerializationRoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new Prompt
        {
            Name = "code_review",
            Title = "Code Review Prompt",
            Description = "Review the provided code",
            Icons =
            [
                new() { Source = "https://example.com/review-icon.svg", MimeType = "image/svg+xml", Sizes = ["any"] }
            ],
            Arguments =
            [
                new() { Name = "code", Description = "The code to review", Required = true }
            ]
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<Prompt>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.NotNull(deserialized.Icons);
        Assert.Equal(original.Icons.Count, deserialized.Icons.Count);
        Assert.Equal(original.Icons[0].Source, deserialized.Icons[0].Source);
        Assert.Equal(original.Icons[0].MimeType, deserialized.Icons[0].MimeType);
        Assert.Equal(original.Icons[0].Sizes, deserialized.Icons[0].Sizes);
        Assert.NotNull(deserialized.Arguments);
        Assert.Equal(original.Arguments.Count, deserialized.Arguments.Count);
        Assert.Equal(original.Arguments[0].Name, deserialized.Arguments[0].Name);
        Assert.Equal(original.Arguments[0].Description, deserialized.Arguments[0].Description);
        Assert.Equal(original.Arguments[0].Required, deserialized.Arguments[0].Required);
    }

    [Fact]
    public static void Prompt_SerializationRoundTrip_WithMinimalProperties()
    {
        // Arrange
        var original = new Prompt
        {
            Name = "simple_prompt"
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<Prompt>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.Icons, deserialized.Icons);
        Assert.Equal(original.Arguments, deserialized.Arguments);
    }

    [Fact]
    public static void Prompt_HasCorrectJsonPropertyNames()
    {
        var prompt = new Prompt
        {
            Name = "test_prompt",
            Title = "Test Prompt",
            Description = "A test prompt",
            Icons = [new() { Source = "https://example.com/icon.webp" }],
            Arguments =
            [
                new() { Name = "input", Description = "Input parameter" }
            ]
        };

        string json = JsonSerializer.Serialize(prompt, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"name\":", json);
        Assert.Contains("\"title\":", json);
        Assert.Contains("\"description\":", json);
        Assert.Contains("\"icons\":", json);
        Assert.Contains("\"arguments\":", json);
    }
}
