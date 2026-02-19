using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ReadResourceResultTests
{
    [Fact]
    public static void ReadResourceResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = "file:///readme.md",
                    MimeType = "text/markdown",
                    Text = "# Hello"
                },
                new BlobResourceContents
                {
                    Uri = "file:///image.png",
                    MimeType = "image/png",
                    Blob = System.Text.Encoding.UTF8.GetBytes("base64data")
                }
            ],
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ReadResourceResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Contents.Count);

        var textContent = Assert.IsType<TextResourceContents>(deserialized.Contents[0]);
        Assert.Equal("file:///readme.md", textContent.Uri);
        Assert.Equal("text/markdown", textContent.MimeType);
        Assert.Equal("# Hello", textContent.Text);

        var blobContent = Assert.IsType<BlobResourceContents>(deserialized.Contents[1]);
        Assert.Equal("file:///image.png", blobContent.Uri);
        Assert.Equal("image/png", blobContent.MimeType);
        Assert.Equal("base64data", System.Text.Encoding.UTF8.GetString(blobContent.Blob.ToArray()));

        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void ReadResourceResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ReadResourceResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ReadResourceResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Contents);
        Assert.Null(deserialized.Meta);
    }
}
