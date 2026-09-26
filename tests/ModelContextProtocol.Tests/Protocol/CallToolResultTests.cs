using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class CallToolResultTests
{
    [Fact]
    public static void CallToolResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "Result text" }],
            StructuredContent = JsonElement.Parse("""{"temperature":72}"""),
            IsError = false,
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CallToolResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Content);
        var textBlock = Assert.IsType<TextContentBlock>(deserialized.Content[0]);
        Assert.Equal("Result text", textBlock.Text);
        Assert.NotNull(deserialized.StructuredContent);
        Assert.Equal(72, deserialized.StructuredContent.Value.GetProperty("temperature").GetInt32());
        Assert.False(deserialized.IsError);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void CallToolResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new CallToolResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CallToolResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Content);
        Assert.Null(deserialized.StructuredContent);
        Assert.Null(deserialized.IsError);
        Assert.Null(deserialized.Meta);
    }

    [Fact]
    public static void CallToolResult_SerializationRoundTrip_PreservesEmbeddedPdfResource()
    {
        byte[] pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n");
        var original = new CallToolResult
        {
            Content =
            [
                new EmbeddedResourceBlock
                {
                    Resource = BlobResourceContents.FromBytes(
                        pdfBytes,
                        "file:///mypdf.pdf",
                        "application/pdf")
                }
            ]
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement resourceBlock = document.RootElement.GetProperty("content")[0];
        Assert.Equal("resource", resourceBlock.GetProperty("type").GetString());

        JsonElement resource = resourceBlock.GetProperty("resource");
        Assert.Equal("file:///mypdf.pdf", resource.GetProperty("uri").GetString());
        Assert.Equal("application/pdf", resource.GetProperty("mimeType").GetString());
        Assert.Equal(Convert.ToBase64String(pdfBytes), resource.GetProperty("blob").GetString());

        var deserialized = JsonSerializer.Deserialize<CallToolResult>(json, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(deserialized);
        var embeddedResource = Assert.IsType<EmbeddedResourceBlock>(Assert.Single(deserialized.Content));
        var pdfResource = Assert.IsType<BlobResourceContents>(embeddedResource.Resource);
        Assert.Equal("file:///mypdf.pdf", pdfResource.Uri);
        Assert.Equal("application/pdf", pdfResource.MimeType);
        Assert.Equal(pdfBytes, pdfResource.DecodedData.ToArray());
    }
}
