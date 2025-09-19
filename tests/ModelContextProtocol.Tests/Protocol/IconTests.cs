using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class IconTests
{
    [Fact]
    public static void Icon_SerializesToJson_WithAllProperties()
    {
        var icon = new Icon
        {
            Src = "https://example.com/icon.png",
            MimeType = "image/png",
            Sizes = "48x48"
        };

        string json = JsonSerializer.Serialize(icon);
        var result = JsonSerializer.Deserialize<Icon>(json);

        Assert.Equal("https://example.com/icon.png", result!.Src);
        Assert.Equal("image/png", result.MimeType);
        Assert.Equal("48x48", result.Sizes);
    }

    [Fact]
    public static void Icon_SerializesToJson_WithOnlyRequiredProperties()
    {
        var icon = new Icon
        {
            Src = "data:image/svg+xml;base64,PHN2Zy4uLjwvc3ZnPg=="
        };

        string json = JsonSerializer.Serialize(icon);
        var result = JsonSerializer.Deserialize<Icon>(json);

        Assert.Equal("data:image/svg+xml;base64,PHN2Zy4uLjwvc3ZnPg==", result!.Src);
        Assert.Null(result.MimeType);
        Assert.Null(result.Sizes);
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
    [InlineData("")]
    [InlineData("   ")]
    public static void Icon_DoesNotValidateEmptyOrWhitespaceSrc(string src)
    {
        // The Icon class doesn't enforce validation in the constructor
        // It's up to consumers to validate the URI format
        var icon = new Icon { Src = src };
        Assert.Equal(src, icon.Src);
    }
}