using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ReferenceTests
{
    [Fact]
    public static void PromptReference_UnknownArrayProperty_IsIgnored()
    {
        // This test verifies that the Reference.Converter properly skips unknown properties
        // even when they contain complex structures like arrays or objects.
        //
        // In this unexpected JSON, "unknownArray" appears inside a prompt reference (where it doesn't belong).
        // The converter should gracefully ignore this unknown property and successfully deserialize
        // the rest of the reference.

        const string jsonWithUnknownArray = """
        {
            "type": "ref/prompt",
            "name": "test_prompt",
            "title": "Test Prompt",
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

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithUnknownArray,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var promptRef = Assert.IsType<PromptReference>(result);
        Assert.Equal("ref/prompt", promptRef.Type);
        Assert.Equal("test_prompt", promptRef.Name);
        Assert.Equal("Test Prompt", promptRef.Title);
    }

    [Fact]
    public static void ResourceReference_UnknownObjectProperty_IsIgnored()
    {
        // Test that unknown properties with nested objects are properly skipped

        const string jsonWithUnknownObject = """
        {
            "type": "ref/resource",
            "uri": "file:///test/resource",
            "unknownObject": {
                "deeply": {
                    "nested": {
                        "value": "should be ignored"
                    }
                }
            }
        }
        """;

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithUnknownObject,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var resourceRef = Assert.IsType<ResourceTemplateReference>(result);
        Assert.Equal("ref/resource", resourceRef.Type);
        Assert.Equal("file:///test/resource", resourceRef.Uri);
    }

    [Fact]
    public static void PromptReference_UnknownMixedProperties_AreIgnored()
    {
        // Test multiple unknown properties with different types

        const string jsonWithMixedUnknown = """
        {
            "type": "ref/prompt",
            "name": "my_prompt",
            "unknownString": "value",
            "unknownNumber": 42,
            "unknownArray": [1, 2, 3],
            "unknownObject": {"key": "value"},
            "unknownBool": true,
            "unknownNull": null
        }
        """;

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithMixedUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var promptRef = Assert.IsType<PromptReference>(result);
        Assert.Equal("ref/prompt", promptRef.Type);
        Assert.Equal("my_prompt", promptRef.Name);
    }

    [Fact]
    public static void ResourceReference_UnknownNestedArrays_AreIgnored()
    {
        // Test complex unknown properties with arrays of objects

        const string jsonWithNestedArrays = """
        {
            "type": "ref/resource",
            "uri": "resource://test/{id}",
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

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithNestedArrays,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var resourceRef = Assert.IsType<ResourceTemplateReference>(result);
        Assert.Equal("ref/resource", resourceRef.Type);
        Assert.Equal("resource://test/{id}", resourceRef.Uri);
    }

    [Fact]
    public static void PromptReference_MultipleUnknownProperties_AllIgnored()
    {
        // Test that multiple unknown properties are all properly skipped

        const string jsonWithMultipleUnknown = """
        {
            "type": "ref/prompt",
            "unknownOne": {"a": 1},
            "name": "test",
            "unknownTwo": [1, 2, 3],
            "title": "Test Title",
            "unknownThree": {"b": {"c": "d"}}
        }
        """;

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithMultipleUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var promptRef = Assert.IsType<PromptReference>(result);
        Assert.Equal("ref/prompt", promptRef.Type);
        Assert.Equal("test", promptRef.Name);
        Assert.Equal("Test Title", promptRef.Title);
    }

    [Fact]
    public static void ResourceReference_UnknownArrayOfArrays_IsIgnored()
    {
        // Test deeply nested array structures in unknown properties

        const string jsonWithArrayOfArrays = """
        {
            "type": "ref/resource",
            "uri": "http://example.com/resource",
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

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithArrayOfArrays,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var resourceRef = Assert.IsType<ResourceTemplateReference>(result);
        Assert.Equal("ref/resource", resourceRef.Type);
        Assert.Equal("http://example.com/resource", resourceRef.Uri);
    }

    [Fact]
    public static void PromptReference_EmptyUnknownArray_IsIgnored()
    {
        // Test empty arrays in unknown properties

        const string jsonWithEmptyArray = """
        {
            "type": "ref/prompt",
            "name": "prompt",
            "unknownEmpty": []
        }
        """;

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithEmptyArray,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var promptRef = Assert.IsType<PromptReference>(result);
        Assert.Equal("ref/prompt", promptRef.Type);
        Assert.Equal("prompt", promptRef.Name);
    }

    [Fact]
    public static void ResourceReference_EmptyUnknownObject_IsIgnored()
    {
        // Test empty objects in unknown properties

        const string jsonWithEmptyObject = """
        {
            "type": "ref/resource",
            "uri": "test://resource",
            "unknownEmpty": {}
        }
        """;

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithEmptyObject,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var resourceRef = Assert.IsType<ResourceTemplateReference>(result);
        Assert.Equal("ref/resource", resourceRef.Type);
        Assert.Equal("test://resource", resourceRef.Uri);
    }

    [Fact]
    public static void PromptReference_UnknownPropertiesBetweenRequired_AreIgnored()
    {
        // Test unknown properties interspersed with required ones

        const string jsonWithInterspersedUnknown = """
        {
            "unknownFirst": {"x": 1},
            "type": "ref/prompt",
            "unknownSecond": [1, 2],
            "name": "my_prompt",
            "unknownThird": {"nested": {"value": true}},
            "title": "My Prompt"
        }
        """;

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithInterspersedUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var promptRef = Assert.IsType<PromptReference>(result);
        Assert.Equal("ref/prompt", promptRef.Type);
        Assert.Equal("my_prompt", promptRef.Name);
        Assert.Equal("My Prompt", promptRef.Title);
    }

    [Fact]
    public static void ResourceReference_VeryDeeplyNestedUnknown_IsIgnored()
    {
        // Test very deeply nested structures in unknown properties

        const string jsonWithVeryDeepNesting = """
        {
            "type": "ref/resource",
            "uri": "deep://resource",
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

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithVeryDeepNesting,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var resourceRef = Assert.IsType<ResourceTemplateReference>(result);
        Assert.Equal("ref/resource", resourceRef.Type);
        Assert.Equal("deep://resource", resourceRef.Uri);
    }

    [Fact]
    public static void PromptReference_SerializationRoundTrip_PreservesKnownProperties()
    {
        // Test that serialization/deserialization preserves known properties

        var original = new PromptReference
        {
            Name = "test_prompt",
            Title = "Test Prompt Title"
        };

        var json = JsonSerializer.Serialize<Reference>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Reference>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        var promptRef = Assert.IsType<PromptReference>(deserialized);
        Assert.Equal(original.Type, promptRef.Type);
        Assert.Equal(original.Name, promptRef.Name);
        Assert.Equal(original.Title, promptRef.Title);
    }

    [Fact]
    public static void ResourceReference_SerializationRoundTrip_PreservesKnownProperties()
    {
        // Test that serialization/deserialization preserves known properties

        var original = new ResourceTemplateReference
        {
            Uri = "file:///path/to/resource"
        };

        var json = JsonSerializer.Serialize<Reference>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Reference>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        var resourceRef = Assert.IsType<ResourceTemplateReference>(deserialized);
        Assert.Equal(original.Type, resourceRef.Type);
        Assert.Equal(original.Uri, resourceRef.Uri);
    }

    [Fact]
    public static void PromptReference_WithoutTitle_Deserializes()
    {
        // Test that title is optional

        const string jsonWithoutTitle = """
        {
            "type": "ref/prompt",
            "name": "minimal_prompt"
        }
        """;

        var result = JsonSerializer.Deserialize<Reference>(
            jsonWithoutTitle,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var promptRef = Assert.IsType<PromptReference>(result);
        Assert.Equal("ref/prompt", promptRef.Type);
        Assert.Equal("minimal_prompt", promptRef.Name);
        Assert.Null(promptRef.Title);
    }

    [Fact]
    public static void Reference_UnknownType_ThrowsException()
    {
        // Test that unknown reference types throw an exception

        const string jsonWithUnknownType = """
        {
            "type": "ref/unknown",
            "name": "test"
        }
        """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Reference>(jsonWithUnknownType, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public static void PromptReference_MissingName_ThrowsException()
    {
        // Test that missing required name property throws an exception

        const string jsonMissingName = """
        {
            "type": "ref/prompt",
            "title": "Test"
        }
        """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Reference>(jsonMissingName, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public static void ResourceReference_MissingUri_ThrowsException()
    {
        // Test that missing required uri property throws an exception

        const string jsonMissingUri = """
        {
            "type": "ref/resource"
        }
        """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Reference>(jsonMissingUri, McpJsonUtilities.DefaultOptions));
    }
}
