using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class IconTests
{
    [Fact]
    public static void Icon_SerializationRoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new Icon
        {
            Src = "https://example.com/icon.png",
            MimeType = "image/png",
            Sizes = "48x48"
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<Icon>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Src, deserialized.Src);
        Assert.Equal(original.MimeType, deserialized.MimeType);
        Assert.Equal(original.Sizes, deserialized.Sizes);
    }

    [Fact]
    public static void Icon_SerializationRoundTrip_WithOnlyRequiredProperties()
    {
        // Arrange
        var original = new Icon
        {
            Src = "data:image/svg+xml;base64,PHN2Zy4uLjwvc3ZnPg=="
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<Icon>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Src, deserialized.Src);
        Assert.Equal(original.MimeType, deserialized.MimeType);
        Assert.Equal(original.Sizes, deserialized.Sizes);
    }

    [Fact]
    public static void Icon_HasCorrectJsonPropertyNames()
    {
        var icon = new Icon
        {
            Src = "https://example.com/icon.svg",
            MimeType = "image/svg+xml",
            Sizes = "any"
        };

        string json = JsonSerializer.Serialize(icon);

        Assert.Contains("\"src\":", json);
        Assert.Contains("\"mimeType\":", json);
        Assert.Contains("\"sizes\":", json);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"mimeType":"image/png"}""")]
    [InlineData("""{"sizes":"48x48"}""")]
    [InlineData("""{"mimeType":"image/png","sizes":"48x48"}""")]
    public static void Icon_DeserializationWithMissingSrc_ThrowsJsonException(string invalidJson)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Icon>(invalidJson));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("[]")]
    public static void Icon_DeserializationWithInvalidJson_ThrowsJsonException(string invalidJson)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Icon>(invalidJson));
    }
}