using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Tests for AIContentExtensions with anonymous types in AdditionalProperties.
/// This validates the fix for the sampling pipeline regression in 0.5.0-preview.1.
/// </summary>
public class AIContentExtensionsAnonymousTypeTests
{
    [Fact]
    public void ToContentBlock_WithAnonymousTypeInAdditionalProperties_DoesNotThrow()
    {
        // This is the minimal repro from the issue
        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["data"] = new { X = 1.0, Y = 2.0 }
            }
        };

        // Should not throw NotSupportedException
        var contentBlock = c.ToContentBlock();

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        Assert.True(contentBlock.Meta.ContainsKey("data"));
    }

    [Fact]
    public void ToContentBlock_WithMultipleAnonymousTypes_DoesNotThrow()
    {
        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["point"] = new { X = 1.0, Y = 2.0 },
                ["metadata"] = new { Name = "Test", Id = 42 },
                ["config"] = new { Enabled = true, Timeout = 30 }
            }
        };

        var contentBlock = c.ToContentBlock();

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        Assert.Equal(3, contentBlock.Meta.Count);
    }

    [Fact]
    public void ToContentBlock_WithNestedAnonymousTypes_DoesNotThrow()
    {
        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["outer"] = new 
                { 
                    Inner = new { Value = "test" },
                    Count = 5
                }
            }
        };

        var contentBlock = c.ToContentBlock();

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        Assert.True(contentBlock.Meta.ContainsKey("outer"));
    }

    [Fact]
    public void ToContentBlock_WithMixedTypesInAdditionalProperties_DoesNotThrow()
    {
        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["anonymous"] = new { X = 1.0, Y = 2.0 },
                ["string"] = "test",
                ["number"] = 42,
                ["boolean"] = true,
                ["array"] = new[] { 1, 2, 3 }
            }
        };

        var contentBlock = c.ToContentBlock();

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        Assert.Equal(5, contentBlock.Meta.Count);
    }

    [Fact]
    public void TextContent_ToContentBlock_WithAnonymousTypeInAdditionalProperties_PreservesData()
    {
        TextContent textContent = new("Hello, world!")
        {
            AdditionalProperties = new()
            {
                ["location"] = new { Lat = 40.7128, Lon = -74.0060 }
            }
        };

        var contentBlock = textContent.ToContentBlock();
        var textBlock = Assert.IsType<TextContentBlock>(contentBlock);

        Assert.Equal("Hello, world!", textBlock.Text);
        Assert.NotNull(textBlock.Meta);
        Assert.True(textBlock.Meta.ContainsKey("location"));
    }

    [Fact]
    public void DataContent_ToContentBlock_WithAnonymousTypeInAdditionalProperties_PreservesData()
    {
        byte[] imageData = [1, 2, 3, 4, 5];
        DataContent dataContent = new(imageData, "image/png")
        {
            AdditionalProperties = new()
            {
                ["dimensions"] = new { Width = 100, Height = 200 }
            }
        };

        var contentBlock = dataContent.ToContentBlock();
        var imageBlock = Assert.IsType<ImageContentBlock>(contentBlock);

        Assert.Equal(Convert.ToBase64String(imageData), imageBlock.Data);
        Assert.Equal("image/png", imageBlock.MimeType);
        Assert.NotNull(imageBlock.Meta);
        Assert.True(imageBlock.Meta.ContainsKey("dimensions"));
    }
}
