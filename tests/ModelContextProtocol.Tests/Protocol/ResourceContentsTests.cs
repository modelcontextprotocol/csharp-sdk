using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ResourceContentsTests
{
    [Fact]
    public static void TextResourceContents_UnknownArrayProperty_IsIgnored()
    {
        // This test verifies that the ResourceContents.Converter properly skips unknown properties
        // even when they contain complex structures like arrays or objects.
        //
        // In this unexpected JSON, "unknownArray" appears inside text resource contents (where it doesn't belong).
        // The converter should gracefully ignore this unknown property and successfully deserialize
        // the rest of the resource contents.

        const string jsonWithUnknownArray = """
        {
            "uri": "file:///test.txt",
            "mimeType": "text/plain",
            "text": "Test content",
            "unknownArray": [
                {
                    "nested": "value"
                },
                {
                    "another": "object"
                }
            ]
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithUnknownArray,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var textResource = Assert.IsType<TextResourceContents>(result);
        Assert.Equal("file:///test.txt", textResource.Uri);
        Assert.Equal("text/plain", textResource.MimeType);
        Assert.Equal("Test content", textResource.Text);
    }

    [Fact]
    public static void BlobResourceContents_UnknownObjectProperty_IsIgnored()
    {
        // Test that unknown properties with nested objects are properly skipped

        const string jsonWithUnknownObject = """
        {
            "uri": "file:///test.bin",
            "mimeType": "application/octet-stream",
            "blob": "AQIDBA==",
            "unknownObject": {
                "deeply": {
                    "nested": {
                        "value": "should be ignored"
                    }
                }
            }
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithUnknownObject,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var blobResource = Assert.IsType<BlobResourceContents>(result);
        Assert.Equal("file:///test.bin", blobResource.Uri);
        Assert.Equal("application/octet-stream", blobResource.MimeType);
        Assert.Equal("AQIDBA==", System.Text.Encoding.UTF8.GetString(blobResource.Blob.ToArray()));
    }

    [Fact]
    public static void TextResourceContents_UnknownMixedProperties_AreIgnored()
    {
        // Test multiple unknown properties with different types

        const string jsonWithMixedUnknown = """
        {
            "uri": "test://resource",
            "text": "content",
            "unknownString": "value",
            "unknownNumber": 42,
            "unknownArray": [1, 2, 3],
            "unknownObject": {"key": "value"},
            "unknownBool": true,
            "unknownNull": null
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithMixedUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var textResource = Assert.IsType<TextResourceContents>(result);
        Assert.Equal("test://resource", textResource.Uri);
        Assert.Equal("content", textResource.Text);
    }

    [Fact]
    public static void BlobResourceContents_UnknownNestedArrays_AreIgnored()
    {
        // Test complex unknown properties with arrays of objects

        const string jsonWithNestedArrays = """
        {
            "uri": "blob://test",
            "blob": "SGVsbG8=",
            "mimeType": "application/custom",
            "unknownComplex": [
                {
                    "nested": [
                        {"deep": "value1"},
                        {"deep": "value2"}
                    ]
                },
                {
                    "nested": [
                        {"deep": "value3"}
                    ]
                }
            ]
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithNestedArrays,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var blobResource = Assert.IsType<BlobResourceContents>(result);
        Assert.Equal("blob://test", blobResource.Uri);
        Assert.Equal("SGVsbG8=", System.Text.Encoding.UTF8.GetString(blobResource.Blob.ToArray()));
        Assert.Equal("application/custom", blobResource.MimeType);
    }

    [Fact]
    public static void TextResourceContents_MultipleUnknownProperties_AllIgnored()
    {
        // Test that multiple unknown properties are all properly skipped

        const string jsonWithMultipleUnknown = """
        {
            "uri": "file:///test",
            "unknownOne": {"a": 1},
            "text": "Test text",
            "unknownTwo": [1, 2, 3],
            "mimeType": "text/plain",
            "unknownThree": {"b": {"c": "d"}}
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithMultipleUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var textResource = Assert.IsType<TextResourceContents>(result);
        Assert.Equal("file:///test", textResource.Uri);
        Assert.Equal("Test text", textResource.Text);
        Assert.Equal("text/plain", textResource.MimeType);
    }

    [Fact]
    public static void BlobResourceContents_UnknownArrayOfArrays_IsIgnored()
    {
        // Test deeply nested array structures in unknown properties

        const string jsonWithArrayOfArrays = """
        {
            "uri": "http://example.com/blob",
            "blob": "Zm9v",
            "unknownNested": [
                [
                    [1, 2, 3],
                    [4, 5, 6]
                ],
                [
                    [7, 8, 9]
                ]
            ]
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithArrayOfArrays,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var blobResource = Assert.IsType<BlobResourceContents>(result);
        Assert.Equal("http://example.com/blob", blobResource.Uri);
        Assert.Equal("Zm9v", System.Text.Encoding.UTF8.GetString(blobResource.Blob.ToArray()));
    }

    [Fact]
    public static void TextResourceContents_EmptyUnknownArray_IsIgnored()
    {
        // Test empty arrays in unknown properties

        const string jsonWithEmptyArray = """
        {
            "uri": "test://text",
            "text": "content",
            "unknownEmpty": []
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithEmptyArray,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var textResource = Assert.IsType<TextResourceContents>(result);
        Assert.Equal("test://text", textResource.Uri);
        Assert.Equal("content", textResource.Text);
    }

    [Fact]
    public static void BlobResourceContents_EmptyUnknownObject_IsIgnored()
    {
        // Test empty objects in unknown properties

        const string jsonWithEmptyObject = """
        {
            "uri": "test://blob",
            "blob": "YmFy",
            "unknownEmpty": {}
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithEmptyObject,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var blobResource = Assert.IsType<BlobResourceContents>(result);
        Assert.Equal("test://blob", blobResource.Uri);
        Assert.Equal("YmFy", System.Text.Encoding.UTF8.GetString(blobResource.Blob.ToArray()));
    }

    [Fact]
    public static void TextResourceContents_UnknownPropertiesBetweenRequired_AreIgnored()
    {
        // Test unknown properties interspersed with required ones

        const string jsonWithInterspersedUnknown = """
        {
            "unknownFirst": {"x": 1},
            "uri": "file:///document.txt",
            "unknownSecond": [1, 2],
            "text": "Document content",
            "unknownThird": {"nested": {"value": true}},
            "mimeType": "text/plain"
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithInterspersedUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var textResource = Assert.IsType<TextResourceContents>(result);
        Assert.Equal("file:///document.txt", textResource.Uri);
        Assert.Equal("Document content", textResource.Text);
        Assert.Equal("text/plain", textResource.MimeType);
    }

    [Fact]
    public static void BlobResourceContents_VeryDeeplyNestedUnknown_IsIgnored()
    {
        // Test very deeply nested structures in unknown properties

        const string jsonWithVeryDeepNesting = """
        {
            "uri": "deep://blob",
            "blob": "ZGVlcA==",
            "unknownDeep": {
                "level1": {
                    "level2": {
                        "level3": {
                            "level4": {
                                "level5": {
                                    "value": "deep"
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithVeryDeepNesting,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var blobResource = Assert.IsType<BlobResourceContents>(result);
        Assert.Equal("deep://blob", blobResource.Uri);
        Assert.Equal("ZGVlcA==", System.Text.Encoding.UTF8.GetString(blobResource.Blob.ToArray()));
    }

    [Fact]
    public static void TextResourceContents_WithMeta_UnknownPropertiesIgnored()
    {
        // Test that _meta property works correctly alongside unknown properties

        const string jsonWithMetaAndUnknown = """
        {
            "uri": "test://meta",
            "text": "content with meta",
            "unknownProp": {"data": "ignored"},
            "_meta": {
                "customField": "metaValue"
            }
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithMetaAndUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var textResource = Assert.IsType<TextResourceContents>(result);
        Assert.Equal("test://meta", textResource.Uri);
        Assert.Equal("content with meta", textResource.Text);
        Assert.NotNull(textResource.Meta);
        Assert.True(textResource.Meta.ContainsKey("customField"));
    }

    [Fact]
    public static void TextResourceContents_SerializationRoundTrip_PreservesKnownProperties()
    {
        // Test that serialization/deserialization preserves known properties

        var original = new TextResourceContents
        {
            Uri = "file:///test.txt",
            MimeType = "text/plain",
            Text = "Test content"
        };

        var json = JsonSerializer.Serialize<ResourceContents>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceContents>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        var textResource = Assert.IsType<TextResourceContents>(deserialized);
        Assert.Equal(original.Uri, textResource.Uri);
        Assert.Equal(original.MimeType, textResource.MimeType);
        Assert.Equal(original.Text, textResource.Text);
    }

    [Fact]
    public static void BlobResourceContents_SerializationRoundTrip_PreservesKnownProperties()
    {
        // Test that serialization/deserialization preserves known properties

        var original = new BlobResourceContents
        {
            Uri = "file:///test.bin",
            MimeType = "application/octet-stream",
            Blob = System.Text.Encoding.UTF8.GetBytes("AQIDBA==")
        };

        var json = JsonSerializer.Serialize<ResourceContents>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceContents>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        var blobResource = Assert.IsType<BlobResourceContents>(deserialized);
        Assert.Equal(original.Uri, blobResource.Uri);
        Assert.Equal(original.MimeType, blobResource.MimeType);
        Assert.True(original.Blob.Span.SequenceEqual(blobResource.Blob.Span));
    }

    [Fact]
    public static void ResourceContents_MissingBothTextAndBlob_ReturnsNull()
    {
        // Test that missing both text and blob properties returns null

        const string jsonWithoutContent = """
        {
            "uri": "test://empty",
            "mimeType": "application/unknown"
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithoutContent,
            McpJsonUtilities.DefaultOptions);

        Assert.Null(result);
    }

    [Fact]
    public static void ResourceContents_WithBothTextAndBlob_PrefersBlob()
    {
        // Test that when both text and blob are present, blob takes precedence

        const string jsonWithBoth = """
        {
            "uri": "test://both",
            "text": "text content",
            "blob": "YmxvYg=="
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithBoth,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var blobResource = Assert.IsType<BlobResourceContents>(result);
        Assert.Equal("test://both", blobResource.Uri);
        Assert.Equal("YmxvYg==", System.Text.Encoding.UTF8.GetString(blobResource.Blob.ToArray()));
    }

    [Fact]
    public static void TextResourceContents_MissingUri_UsesEmptyString()
    {
        // Test that missing uri defaults to empty string

        const string jsonWithoutUri = """
        {
            "text": "content without uri"
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithoutUri,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var textResource = Assert.IsType<TextResourceContents>(result);
        Assert.Equal(string.Empty, textResource.Uri);
        Assert.Equal("content without uri", textResource.Text);
    }

    [Fact]
    public static void BlobResourceContents_MissingUri_UsesEmptyString()
    {
        // Test that missing uri defaults to empty string

        const string jsonWithoutUri = """
        {
            "blob": "YmxvYg=="
        }
        """;

        var result = JsonSerializer.Deserialize<ResourceContents>(
            jsonWithoutUri,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var blobResource = Assert.IsType<BlobResourceContents>(result);
        Assert.Equal(string.Empty, blobResource.Uri);
        Assert.Equal("YmxvYg==", System.Text.Encoding.UTF8.GetString(blobResource.Blob.ToArray()));
    }

    [Fact]
    public static void TextResourceContents_NullMimeType_OmittedFromJson()
    {
        var resource = new TextResourceContents
        {
            Uri = "file:///test.txt",
            MimeType = null,
            Text = "hello"
        };

        var json = JsonSerializer.Serialize<ResourceContents>(resource, McpJsonUtilities.DefaultOptions);

        Assert.DoesNotContain("mimeType", json);
    }

    [Fact]
    public static void BlobResourceContents_NullMimeType_OmittedFromJson()
    {
        var resource = new BlobResourceContents
        {
            Uri = "file:///test.bin",
            MimeType = null,
            Blob = new byte[] { 1, 2, 3 }
        };

        var json = JsonSerializer.Serialize<ResourceContents>(resource, McpJsonUtilities.DefaultOptions);

        Assert.DoesNotContain("mimeType", json);
    }

    [Fact]
    public static void BlobResourceContents_Deserialization_HandlesEscapedForwardSlashInBase64()
    {
        // Base64 uses '/' which some JSON encoders escape as '\/' (valid JSON).
        // The converter must unescape before storing the base64 UTF-8 bytes.
        byte[] originalBytes = [0xFF, 0xD8, 0xFF, 0xE0]; // sample bytes that produce '/' in base64
        string base64 = Convert.ToBase64String(originalBytes); // "/9j/4A=="
        Assert.Contains("/", base64);

        // Simulate a JSON encoder that escapes '/' as '\/'
        string json = $$"""{"uri":"file:///test.bin","blob":"{{base64.Replace("/", "\\/")}}","mimeType":"application/octet-stream"}""";

        var deserialized = JsonSerializer.Deserialize<ResourceContents>(json, McpJsonUtilities.DefaultOptions);
        var blob = Assert.IsType<BlobResourceContents>(deserialized);
        Assert.Equal(base64, System.Text.Encoding.UTF8.GetString(blob.Blob.ToArray()));
        Assert.Equal(originalBytes, blob.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(ContentBlockTests.Base64TestData), MemberType = typeof(ContentBlockTests))]
    public static void BlobResourceContents_FromBytes_RoundtripsCorrectly(byte[] originalBytes)
    {
        string expectedBase64 = Convert.ToBase64String(originalBytes);

        var blob = BlobResourceContents.FromBytes(originalBytes, "file:///test.bin", "application/octet-stream");

        Assert.Equal("file:///test.bin", blob.Uri);
        Assert.Equal("application/octet-stream", blob.MimeType);
        Assert.Equal(originalBytes, blob.DecodedData.ToArray());
        Assert.Equal(expectedBase64, Encoding.UTF8.GetString(blob.Blob.ToArray()));
    }

    [Theory]
    [MemberData(nameof(ContentBlockTests.Base64TestData), MemberType = typeof(ContentBlockTests))]
    public static void BlobResourceContents_BlobSetter_RoundtripsCorrectly(byte[] originalBytes)
    {
        string base64 = Convert.ToBase64String(originalBytes);
        byte[] base64Utf8 = Encoding.UTF8.GetBytes(base64);

        var blob = new BlobResourceContents { Blob = base64Utf8, Uri = "file:///test.bin" };

        Assert.Equal(base64Utf8, blob.Blob.ToArray());
        Assert.Equal(originalBytes, blob.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(ContentBlockTests.Base64TestData), MemberType = typeof(ContentBlockTests))]
    public static void BlobResourceContents_JsonRoundtrip_PreservesData(byte[] originalBytes)
    {
        string base64 = Convert.ToBase64String(originalBytes);
        byte[] base64Utf8 = Encoding.UTF8.GetBytes(base64);

        var original = new BlobResourceContents
        {
            Blob = base64Utf8,
            Uri = "file:///test.bin",
            MimeType = "application/octet-stream"
        };
        string json = JsonSerializer.Serialize<ResourceContents>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = Assert.IsType<BlobResourceContents>(
            JsonSerializer.Deserialize<ResourceContents>(json, McpJsonUtilities.DefaultOptions));

        Assert.Equal(base64Utf8, deserialized.Blob.ToArray());
        Assert.Equal(originalBytes, deserialized.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(ContentBlockTests.Base64TestData), MemberType = typeof(ContentBlockTests))]
    public static void BlobResourceContents_FromBytes_JsonRoundtrip_PreservesData(byte[] originalBytes)
    {
        string expectedBase64 = Convert.ToBase64String(originalBytes);

        var original = BlobResourceContents.FromBytes(originalBytes, "file:///test.bin", "application/octet-stream");
        string json = JsonSerializer.Serialize<ResourceContents>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = Assert.IsType<BlobResourceContents>(
            JsonSerializer.Deserialize<ResourceContents>(json, McpJsonUtilities.DefaultOptions));

        Assert.Equal(expectedBase64, Encoding.UTF8.GetString(deserialized.Blob.ToArray()));
        Assert.Equal(originalBytes, deserialized.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(ContentBlockTests.Base64TestData), MemberType = typeof(ContentBlockTests))]
    public static void BlobResourceContents_EscapedJsonRoundtrip_PreservesData(byte[] originalBytes)
    {
        string base64 = Convert.ToBase64String(originalBytes);

        string json = $$"""{"uri":"file:///test.bin","blob":"{{base64.Replace("/", "\\/")}}","mimeType":"application/octet-stream"}""";

        var deserialized = Assert.IsType<BlobResourceContents>(
            JsonSerializer.Deserialize<ResourceContents>(json, McpJsonUtilities.DefaultOptions));

        Assert.Equal(base64, Encoding.UTF8.GetString(deserialized.Blob.ToArray()));
        Assert.Equal(originalBytes, deserialized.DecodedData.ToArray());
    }

    [Fact]
    public static void BlobResourceContents_BlobSetterInvalidatesCachedDecodedData()
    {
        byte[] bytes1 = [1, 2, 3];
        var blob = BlobResourceContents.FromBytes(bytes1, "file:///test.bin");

        Assert.Equal(bytes1, blob.DecodedData.ToArray());

        byte[] newBytes = [4, 5, 6];
        string newBase64 = Convert.ToBase64String(newBytes);
        blob.Blob = Encoding.UTF8.GetBytes(newBase64);

        Assert.Equal(newBytes, blob.DecodedData.ToArray());
    }

    [Theory]
    [MemberData(nameof(ContentBlockTests.Base64TestData), MemberType = typeof(ContentBlockTests))]
    public static void BlobResourceContents_FromBytes_LazilyEncodesBlob(byte[] originalBytes)
    {
        var blob = BlobResourceContents.FromBytes(originalBytes, "file:///test.bin");

        // Access DecodedData first without touching Blob
        Assert.Equal(originalBytes, blob.DecodedData.ToArray());

        // Now access Blob and verify lazy encoding
        string expectedBase64 = Convert.ToBase64String(originalBytes);
        Assert.Equal(expectedBase64, Encoding.UTF8.GetString(blob.Blob.ToArray()));
    }
}
