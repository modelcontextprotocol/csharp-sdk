using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class ContentBlockTests
{
    [Fact]
    public void ResourceLinkBlock_SerializationRoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new ResourceLinkBlock
        {
            Uri = "https://example.com/resource",
            Name = "Test Resource",
            Title = "Test Resource Title",
            Description = "A test resource for validation",
            MimeType = "text/plain",
            Size = 1024,
            Icons = [new Icon { Source = "https://example.com/icon.png", MimeType = "image/png" }]
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var resourceLink = Assert.IsType<ResourceLinkBlock>(deserialized);
        
        Assert.Equal(original.Uri, resourceLink.Uri);
        Assert.Equal(original.Name, resourceLink.Name);
        Assert.Equal(original.Title, resourceLink.Title);
        Assert.Equal(original.Description, resourceLink.Description);
        Assert.Equal(original.MimeType, resourceLink.MimeType);
        Assert.Equal(original.Size, resourceLink.Size);
        Assert.Equal("resource_link", resourceLink.Type);
        Assert.NotNull(resourceLink.Icons);
        Assert.Single(resourceLink.Icons);
        Assert.Equal("https://example.com/icon.png", resourceLink.Icons[0].Source);
        Assert.Equal("image/png", resourceLink.Icons[0].MimeType);
    }

    [Fact]
    public void ResourceLinkBlock_DeserializationWithMinimalProperties_Succeeds()
    {
        // Arrange - JSON with only required properties
        const string Json = """
            {
                "type": "resource_link",
                "uri": "https://example.com/minimal",
                "name": "Minimal Resource"
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var resourceLink = Assert.IsType<ResourceLinkBlock>(deserialized);
        
        Assert.Equal("https://example.com/minimal", resourceLink.Uri);
        Assert.Equal("Minimal Resource", resourceLink.Name);
        Assert.Null(resourceLink.Title);
        Assert.Null(resourceLink.Description);
        Assert.Null(resourceLink.MimeType);
        Assert.Null(resourceLink.Size);
        Assert.Null(resourceLink.Icons);
        Assert.Equal("resource_link", resourceLink.Type);
    }

    [Fact]
    public void ResourceLinkBlock_DeserializationWithoutName_ThrowsJsonException()
    {
        // Arrange - JSON missing the required "name" property
        const string Json = """
            {
                "type": "resource_link",
                "uri": "https://example.com/missing-name"
            }
            """;

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ContentBlock>(Json, McpJsonUtilities.DefaultOptions));
        
        Assert.Contains("Name must be provided for 'resource_link' type", exception.Message);
    }

    [Fact]
    public void ResourceLinkBlock_DeserializationWithTitleAndIcons_Succeeds()
    {
        // Arrange - JSON with title and icons properties per spec
        const string Json = """
            {
                "type": "resource_link",
                "uri": "https://example.com/resource",
                "name": "my-resource",
                "title": "My Resource",
                "icons": [
                    { "src": "https://example.com/icon1.png", "mimeType": "image/png", "sizes": ["48x48"], "theme": "light" },
                    { "src": "https://example.com/icon2.svg", "mimeType": "image/svg+xml" }
                ]
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var resourceLink = Assert.IsType<ResourceLinkBlock>(deserialized);

        Assert.Equal("https://example.com/resource", resourceLink.Uri);
        Assert.Equal("my-resource", resourceLink.Name);
        Assert.Equal("My Resource", resourceLink.Title);
        Assert.NotNull(resourceLink.Icons);
        Assert.Equal(2, resourceLink.Icons.Count);
        Assert.Equal("https://example.com/icon1.png", resourceLink.Icons[0].Source);
        Assert.Equal("image/png", resourceLink.Icons[0].MimeType);
        Assert.NotNull(resourceLink.Icons[0].Sizes);
        Assert.Equal("48x48", resourceLink.Icons[0].Sizes![0]);
        Assert.Equal("light", resourceLink.Icons[0].Theme);
        Assert.Equal("https://example.com/icon2.svg", resourceLink.Icons[1].Source);
        Assert.Equal("image/svg+xml", resourceLink.Icons[1].MimeType);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownArrayProperty()
    {
        // This is a regression test where a server returned an unexpected response with
        // `structuredContent` as an array nested inside a content block. This should be
        // permitted with the `structuredContent` gracefully ignored in that location.
        string responseJson = @"{
            ""type"": ""text"",
            ""text"": ""[\n  {\n    \""Data\"": \""1234567890\""\n  }\n]"",
            ""structuredContent"": [
                {
                    ""Data"": ""1234567890""
                }
            ]
        }";

        var contentBlock = JsonSerializer.Deserialize<ContentBlock>(responseJson, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(contentBlock);

        var textBlock = Assert.IsType<TextContentBlock>(contentBlock);
        Assert.Contains("1234567890", textBlock.Text);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownObjectProperties()
    {
        string responseJson = @"{
            ""type"": ""text"",
            ""text"": ""Sample text"",
            ""unknownObject"": {
                ""nestedProp1"": ""value1"",
                ""nestedProp2"": {
                    ""deeplyNested"": true
                }
            }
        }";

        var contentBlock = JsonSerializer.Deserialize<ContentBlock>(responseJson, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(contentBlock);

        var textBlock = Assert.IsType<TextContentBlock>(contentBlock);
        Assert.Contains("Sample text", textBlock.Text);
    }

    [Fact]
    public void ToolResultContentBlock_WithError_SerializationRoundtrips()
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content = [new TextContentBlock { Text = "Error: City not found" }],
            IsError = true
        };

        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.True(result.IsError);
        Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("Error: City not found", textBlock.Text);
    }

    [Fact]
    public void ToolResultContentBlock_WithStructuredContent_SerializationRoundtrips()
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content =
            [
                new TextContentBlock { Text = "Result data" }
            ],
            StructuredContent = JsonElement.Parse("""{"temperature":18,"condition":"cloudy"}"""),
            IsError = false
        };

        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("Result data", textBlock.Text);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(18, result.StructuredContent.Value.GetProperty("temperature").GetInt32());
        Assert.Equal("cloudy", result.StructuredContent.Value.GetProperty("condition").GetString());
        Assert.False(result.IsError);
    }

    [Fact]
    public void ToolResultContentBlock_SerializationRoundTrip()
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content =
            [
                new TextContentBlock { Text = "Result data" },
                new ImageContentBlock { Data = System.Text.Encoding.UTF8.GetBytes("base64data"), MimeType = "image/png" }
            ],
            StructuredContent = JsonElement.Parse("""{"temperature":18,"condition":"cloudy"}"""),
            IsError = false
        };

        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.Equal(2, result.Content.Count);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("Result data", textBlock.Text);
        var imageBlock = Assert.IsType<ImageContentBlock>(result.Content[1]);
        Assert.Equal("base64data", System.Text.Encoding.UTF8.GetString(imageBlock.Data.ToArray()));
        Assert.Equal("image/png", imageBlock.MimeType);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(18, result.StructuredContent.Value.GetProperty("temperature").GetInt32());
        Assert.Equal("cloudy", result.StructuredContent.Value.GetProperty("condition").GetString());
        Assert.False(result.IsError);
    }

    [Fact]
    public void ToolUseContentBlock_SerializationRoundTrip()
    {
        ToolUseContentBlock toolUse = new()
        {
            Id = "call_abc123",
            Name = "get_weather",
            Input = JsonElement.Parse("""{"city":"Paris","units":"metric"}""")
        };

        var json = JsonSerializer.Serialize<ContentBlock>(toolUse, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolUseContentBlock>(deserialized);
        Assert.Equal("call_abc123", result.Id);
        Assert.Equal("get_weather", result.Name);
        Assert.Equal("Paris", result.Input.GetProperty("city").GetString());
        Assert.Equal("metric", result.Input.GetProperty("units").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ImageContentBlock_FromBytes_ThrowsForNullOrWhiteSpaceMimeType(string? mimeType)
    {
        Assert.ThrowsAny<ArgumentException>(() => ImageContentBlock.FromBytes((byte[])[1, 2, 3], mimeType!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AudioContentBlock_FromBytes_ThrowsForNullOrWhiteSpaceMimeType(string? mimeType)
    {
        Assert.ThrowsAny<ArgumentException>(() => AudioContentBlock.FromBytes((byte[])[1, 2, 3], mimeType!));
    }

    [Fact]
    public void ImageContentBlock_Deserialization_HandlesEscapedForwardSlashInBase64()
    {
        // Base64 uses '/' which some JSON encoders escape as '\/' (valid JSON).
        // The converter must unescape before storing the base64 UTF-8 bytes.
        byte[] originalBytes = [0xFF, 0xD8, 0xFF, 0xE0]; // sample bytes that produce '/' in base64
        string base64 = Convert.ToBase64String(originalBytes); // "/9j/4A=="
        Assert.Contains("/", base64);

        // Simulate a JSON encoder that escapes '/' as '\/'
        string json = $$"""{"type":"image","data":"{{base64.Replace("/", "\\/")}}","mimeType":"image/jpeg"}""";

        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);
        var image = Assert.IsType<ImageContentBlock>(deserialized);
        Assert.Equal(base64, System.Text.Encoding.UTF8.GetString(image.Data.ToArray()));
        Assert.Equal(originalBytes, image.DecodedData.ToArray());
    }

    [Fact]
    public void AudioContentBlock_Deserialization_HandlesEscapedForwardSlashInBase64()
    {
        byte[] originalBytes = [0xFF, 0xD8, 0xFF, 0xE0];
        string base64 = Convert.ToBase64String(originalBytes);
        Assert.Contains("/", base64);

        string json = $$"""{"type":"audio","data":"{{base64.Replace("/", "\\/")}}","mimeType":"audio/wav"}""";

        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);
        var audio = Assert.IsType<AudioContentBlock>(deserialized);
        Assert.Equal(base64, System.Text.Encoding.UTF8.GetString(audio.Data.ToArray()));
        Assert.Equal(originalBytes, audio.DecodedData.ToArray());
    }

    /// <summary>
    /// Provides test data for base64 roundtrip tests. Each entry is a byte array that exercises
    /// different base64 encoding characteristics:
    /// - Various lengths producing 0, 1, or 2 padding characters
    /// - Bytes that produce all 64 base64 alphabet characters including '+' and '/'
    /// </summary>
    public static TheoryData<byte[]> Base64TestData()
    {
        var data = new TheoryData<byte[]>
        {
            Array.Empty<byte>(),       // empty: ""
            new byte[] { 0x00 },       // 1 byte, 2 padding chars: "AA=="
            new byte[] { 0x00, 0x01 }, // 2 bytes, 1 padding char: "AAE="
            new byte[] { 0x00, 0x01, 0x02 }, // 3 bytes, no padding: "AAEC"
            new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, // produces '/' in base64: "/9j/4A=="
            new byte[] { 0xFB, 0xEF, 0xBE }, // produces '+' in base64: "++++"
        };

        // All 256 byte values to exercise the full base64 alphabet
        byte[] allBytes = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            allBytes[i] = (byte)i;
        }
        data.Add(allBytes);

        // Larger payload (1024 bytes)
        byte[] largePayload = new byte[1024];
        new Random(42).NextBytes(largePayload);
        data.Add(largePayload);

        return data;
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void ImageContentBlock_FromBytes_RoundtripsCorrectly(byte[] originalBytes)
    {
        string expectedBase64 = Convert.ToBase64String(originalBytes);

        var image = ImageContentBlock.FromBytes(originalBytes, "image/png");

        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(originalBytes, image.DecodedData.ToArray());
        Assert.Equal(expectedBase64, Encoding.UTF8.GetString(image.Data.ToArray()));
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void ImageContentBlock_DataSetter_RoundtripsCorrectly(byte[] originalBytes)
    {
        string base64 = Convert.ToBase64String(originalBytes);
        byte[] base64Utf8 = Encoding.UTF8.GetBytes(base64);

        var image = new ImageContentBlock { Data = base64Utf8, MimeType = "image/png" };

        Assert.Equal(base64Utf8, image.Data.ToArray());
        Assert.Equal(originalBytes, image.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void ImageContentBlock_JsonRoundtrip_PreservesData(byte[] originalBytes)
    {
        string base64 = Convert.ToBase64String(originalBytes);
        byte[] base64Utf8 = Encoding.UTF8.GetBytes(base64);

        var original = new ImageContentBlock { Data = base64Utf8, MimeType = "image/png" };
        string json = JsonSerializer.Serialize<ContentBlock>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = Assert.IsType<ImageContentBlock>(
            JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions));

        Assert.Equal(base64Utf8, deserialized.Data.ToArray());
        Assert.Equal(originalBytes, deserialized.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void ImageContentBlock_FromBytes_JsonRoundtrip_PreservesData(byte[] originalBytes)
    {
        string expectedBase64 = Convert.ToBase64String(originalBytes);

        var original = ImageContentBlock.FromBytes(originalBytes, "image/jpeg");
        string json = JsonSerializer.Serialize<ContentBlock>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = Assert.IsType<ImageContentBlock>(
            JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions));

        Assert.Equal(expectedBase64, Encoding.UTF8.GetString(deserialized.Data.ToArray()));
        Assert.Equal(originalBytes, deserialized.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void ImageContentBlock_EscapedJsonRoundtrip_PreservesData(byte[] originalBytes)
    {
        string base64 = Convert.ToBase64String(originalBytes);

        // Simulate JSON encoder that escapes '/' as '\/'
        string json = $$"""{"type":"image","data":"{{base64.Replace("/", "\\/")}}","mimeType":"image/png"}""";

        var deserialized = Assert.IsType<ImageContentBlock>(
            JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions));

        Assert.Equal(base64, Encoding.UTF8.GetString(deserialized.Data.ToArray()));
        Assert.Equal(originalBytes, deserialized.DecodedData.ToArray());
    }

    [Fact]
    public void ImageContentBlock_DataSetterInvalidatesCachedDecodedData()
    {
        byte[] bytes1 = [1, 2, 3];
        var image = ImageContentBlock.FromBytes(bytes1, "image/png");

        // Access DecodedData to populate cache
        Assert.Equal(bytes1, image.DecodedData.ToArray());

        // Set new Data to invalidate cache
        byte[] newBytes = [4, 5, 6];
        string newBase64 = Convert.ToBase64String(newBytes);
        image.Data = Encoding.UTF8.GetBytes(newBase64);

        Assert.Equal(newBytes, image.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void AudioContentBlock_FromBytes_RoundtripsCorrectly(byte[] originalBytes)
    {
        string expectedBase64 = Convert.ToBase64String(originalBytes);

        var audio = AudioContentBlock.FromBytes(originalBytes, "audio/wav");

        Assert.Equal("audio/wav", audio.MimeType);
        Assert.Equal(originalBytes, audio.DecodedData.ToArray());
        Assert.Equal(expectedBase64, Encoding.UTF8.GetString(audio.Data.ToArray()));
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void AudioContentBlock_DataSetter_RoundtripsCorrectly(byte[] originalBytes)
    {
        string base64 = Convert.ToBase64String(originalBytes);
        byte[] base64Utf8 = Encoding.UTF8.GetBytes(base64);

        var audio = new AudioContentBlock { Data = base64Utf8, MimeType = "audio/wav" };

        Assert.Equal(base64Utf8, audio.Data.ToArray());
        Assert.Equal(originalBytes, audio.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void AudioContentBlock_JsonRoundtrip_PreservesData(byte[] originalBytes)
    {
        string base64 = Convert.ToBase64String(originalBytes);
        byte[] base64Utf8 = Encoding.UTF8.GetBytes(base64);

        var original = new AudioContentBlock { Data = base64Utf8, MimeType = "audio/wav" };
        string json = JsonSerializer.Serialize<ContentBlock>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = Assert.IsType<AudioContentBlock>(
            JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions));

        Assert.Equal(base64Utf8, deserialized.Data.ToArray());
        Assert.Equal(originalBytes, deserialized.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void AudioContentBlock_FromBytes_JsonRoundtrip_PreservesData(byte[] originalBytes)
    {
        string expectedBase64 = Convert.ToBase64String(originalBytes);

        var original = AudioContentBlock.FromBytes(originalBytes, "audio/mp3");
        string json = JsonSerializer.Serialize<ContentBlock>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = Assert.IsType<AudioContentBlock>(
            JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions));

        Assert.Equal(expectedBase64, Encoding.UTF8.GetString(deserialized.Data.ToArray()));
        Assert.Equal(originalBytes, deserialized.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void AudioContentBlock_EscapedJsonRoundtrip_PreservesData(byte[] originalBytes)
    {
        string base64 = Convert.ToBase64String(originalBytes);

        string json = $$"""{"type":"audio","data":"{{base64.Replace("/", "\\/")}}","mimeType":"audio/wav"}""";

        var deserialized = Assert.IsType<AudioContentBlock>(
            JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions));

        Assert.Equal(base64, Encoding.UTF8.GetString(deserialized.Data.ToArray()));
        Assert.Equal(originalBytes, deserialized.DecodedData.ToArray());
    }

    [Fact]
    public void AudioContentBlock_DataSetterInvalidatesCachedDecodedData()
    {
        byte[] bytes1 = [1, 2, 3];
        var audio = AudioContentBlock.FromBytes(bytes1, "audio/wav");

        Assert.Equal(bytes1, audio.DecodedData.ToArray());

        byte[] newBytes = [4, 5, 6];
        string newBase64 = Convert.ToBase64String(newBytes);
        audio.Data = Encoding.UTF8.GetBytes(newBase64);

        Assert.Equal(newBytes, audio.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void ImageContentBlock_FromBytes_LazilyEncodesData(byte[] originalBytes)
    {
        // FromBytes should only decode when Data is accessed
        var image = ImageContentBlock.FromBytes(originalBytes, "image/png");

        // First, access DecodedData without touching Data
        Assert.Equal(originalBytes, image.DecodedData.ToArray());

        // Now access Data and verify it lazily encoded correctly
        string expectedBase64 = Convert.ToBase64String(originalBytes);
        Assert.Equal(expectedBase64, Encoding.UTF8.GetString(image.Data.ToArray()));
    }

    [Theory]
    [MemberData(nameof(Base64TestData))]
    public void AudioContentBlock_FromBytes_LazilyEncodesData(byte[] originalBytes)
    {
        var audio = AudioContentBlock.FromBytes(originalBytes, "audio/wav");

        Assert.Equal(originalBytes, audio.DecodedData.ToArray());

        string expectedBase64 = Convert.ToBase64String(originalBytes);
        Assert.Equal(expectedBase64, Encoding.UTF8.GetString(audio.Data.ToArray()));
    }
}