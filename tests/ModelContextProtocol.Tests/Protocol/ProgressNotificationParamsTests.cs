using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ProgressNotificationParamsTests
{
    [Fact]
    public static void ProgressNotificationParams_UnknownArrayProperty_IsIgnored()
    {
        // This test verifies that the ProgressNotificationParams.Converter properly skips unknown properties
        // even when they contain complex structures like arrays or objects.
        //
        // In this unexpected JSON, "unknownArray" appears inside progress notification params (where it doesn't belong).
        // The converter should gracefully ignore this unknown property and successfully deserialize
        // the rest of the notification parameters.

        const string jsonWithUnknownArray = """
        {
            "progressToken": "test-token",
            "progress": 50.0,
            "total": 100.0,
            "message": "Processing items",
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

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithUnknownArray,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("test-token", result.ProgressToken.ToString());
        Assert.Equal(50.0f, result.Progress.Progress);
        Assert.Equal(100.0f, result.Progress.Total);
        Assert.Equal("Processing items", result.Progress.Message);
    }

    [Fact]
    public static void ProgressNotificationParams_UnknownObjectProperty_IsIgnored()
    {
        // Test that unknown properties with nested objects are properly skipped

        const string jsonWithUnknownObject = """
        {
            "progressToken": 12345,
            "progress": 75.5,
            "unknownObject": {
                "deeply": {
                    "nested": {
                        "value": "should be ignored"
                    }
                }
            }
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithUnknownObject,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("12345", result.ProgressToken.ToString());
        Assert.Equal(75.5f, result.Progress.Progress);
        Assert.Null(result.Progress.Total);
    }

    [Fact]
    public static void ProgressNotificationParams_UnknownMixedProperties_AreIgnored()
    {
        // Test multiple unknown properties with different types

        const string jsonWithMixedUnknown = """
        {
            "progressToken": "abc-123",
            "unknownString": "value",
            "progress": 25.0,
            "unknownNumber": 42,
            "total": 200.0,
            "unknownArray": [1, 2, 3],
            "message": "Working on it",
            "unknownObject": {"key": "value"},
            "unknownBool": true,
            "unknownNull": null
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithMixedUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("abc-123", result.ProgressToken.ToString());
        Assert.Equal(25.0f, result.Progress.Progress);
        Assert.Equal(200.0f, result.Progress.Total);
        Assert.Equal("Working on it", result.Progress.Message);
    }

    [Fact]
    public static void ProgressNotificationParams_UnknownNestedArrays_AreIgnored()
    {
        // Test complex unknown properties with arrays of objects

        const string jsonWithNestedArrays = """
        {
            "progressToken": "nested-test",
            "progress": 33.3,
            "total": 99.9,
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

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithNestedArrays,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("nested-test", result.ProgressToken.ToString());
        Assert.Equal(33.3f, result.Progress.Progress);
        Assert.Equal(99.9f, result.Progress.Total);
    }

    [Fact]
    public static void ProgressNotificationParams_MultipleUnknownProperties_AllIgnored()
    {
        // Test that multiple unknown properties are all properly skipped

        const string jsonWithMultipleUnknown = """
        {
            "unknownOne": {"a": 1},
            "progressToken": "multi-test",
            "unknownTwo": [1, 2, 3],
            "progress": 10.0,
            "unknownThree": {"b": {"c": "d"}},
            "message": "Multiple unknowns"
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithMultipleUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("multi-test", result.ProgressToken.ToString());
        Assert.Equal(10.0f, result.Progress.Progress);
        Assert.Equal("Multiple unknowns", result.Progress.Message);
    }

    [Fact]
    public static void ProgressNotificationParams_UnknownArrayOfArrays_IsIgnored()
    {
        // Test deeply nested array structures in unknown properties

        const string jsonWithArrayOfArrays = """
        {
            "progressToken": 999,
            "progress": 88.0,
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

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithArrayOfArrays,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("999", result.ProgressToken.ToString());
        Assert.Equal(88.0f, result.Progress.Progress);
    }

    [Fact]
    public static void ProgressNotificationParams_EmptyUnknownArray_IsIgnored()
    {
        // Test empty arrays in unknown properties

        const string jsonWithEmptyArray = """
        {
            "progressToken": "empty-array",
            "progress": 0.0,
            "unknownEmpty": []
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithEmptyArray,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("empty-array", result.ProgressToken.ToString());
        Assert.Equal(0.0f, result.Progress.Progress);
    }

    [Fact]
    public static void ProgressNotificationParams_EmptyUnknownObject_IsIgnored()
    {
        // Test empty objects in unknown properties

        const string jsonWithEmptyObject = """
        {
            "progressToken": "empty-obj",
            "progress": 100.0,
            "total": 100.0,
            "unknownEmpty": {}
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithEmptyObject,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("empty-obj", result.ProgressToken.ToString());
        Assert.Equal(100.0f, result.Progress.Progress);
        Assert.Equal(100.0f, result.Progress.Total);
    }

    [Fact]
    public static void ProgressNotificationParams_UnknownPropertiesBetweenRequired_AreIgnored()
    {
        // Test unknown properties interspersed with required ones

        const string jsonWithInterspersedUnknown = """
        {
            "unknownFirst": {"x": 1},
            "progressToken": "interspersed",
            "unknownSecond": [1, 2],
            "progress": 42.0,
            "unknownThird": {"nested": {"value": true}},
            "total": 84.0,
            "unknownFourth": [],
            "message": "Interspersed test"
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithInterspersedUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("interspersed", result.ProgressToken.ToString());
        Assert.Equal(42.0f, result.Progress.Progress);
        Assert.Equal(84.0f, result.Progress.Total);
        Assert.Equal("Interspersed test", result.Progress.Message);
    }

    [Fact]
    public static void ProgressNotificationParams_VeryDeeplyNestedUnknown_IsIgnored()
    {
        // Test very deeply nested structures in unknown properties

        const string jsonWithVeryDeepNesting = """
        {
            "progressToken": 777,
            "progress": 50.0,
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

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithVeryDeepNesting,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("777", result.ProgressToken.ToString());
        Assert.Equal(50.0f, result.Progress.Progress);
    }

    [Fact]
    public static void ProgressNotificationParams_WithMeta_UnknownPropertiesIgnored()
    {
        // Test that _meta property works correctly alongside unknown properties

        const string jsonWithMetaAndUnknown = """
        {
            "progressToken": "meta-test",
            "progress": 65.0,
            "unknownProp": {"data": "ignored"},
            "_meta": {
                "customField": "metaValue"
            }
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonWithMetaAndUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("meta-test", result.ProgressToken.ToString());
        Assert.Equal(65.0f, result.Progress.Progress);
        Assert.NotNull(result.Meta);
        Assert.True(result.Meta.ContainsKey("customField"));
    }

    [Fact]
    public static void ProgressNotificationParams_SerializationRoundTrip_PreservesKnownProperties()
    {
        // Test that serialization/deserialization preserves known properties

        var original = new ProgressNotificationParams
        {
            ProgressToken = new ProgressToken("roundtrip-test"),
            Progress = new ProgressNotificationValue
            {
                Progress = 45.5f,
                Total = 91.0f,
                Message = "Roundtrip test"
            }
        };

        var json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ProgressNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ProgressToken.ToString(), deserialized.ProgressToken.ToString());
        Assert.Equal(original.Progress.Progress, deserialized.Progress.Progress);
        Assert.Equal(original.Progress.Total, deserialized.Progress.Total);
        Assert.Equal(original.Progress.Message, deserialized.Progress.Message);
    }

    [Fact]
    public static void ProgressNotificationParams_MinimalProperties_Deserializes()
    {
        // Test with only required properties

        const string jsonMinimal = """
        {
            "progressToken": "minimal",
            "progress": 50.0
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonMinimal,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("minimal", result.ProgressToken.ToString());
        Assert.Equal(50.0f, result.Progress.Progress);
        Assert.Null(result.Progress.Total);
        Assert.Null(result.Progress.Message);
    }

    [Fact]
    public static void ProgressNotificationParams_MissingProgress_ThrowsException()
    {
        // Test that missing required progress property throws an exception

        const string jsonMissingProgress = """
        {
            "progressToken": "test",
            "total": 100.0
        }
        """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ProgressNotificationParams>(jsonMissingProgress, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public static void ProgressNotificationParams_MissingProgressToken_ThrowsException()
    {
        // Test that missing required progressToken property throws an exception

        const string jsonMissingToken = """
        {
            "progress": 50.0,
            "total": 100.0
        }
        """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ProgressNotificationParams>(jsonMissingToken, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public static void ProgressNotificationParams_StringProgressToken_Deserializes()
    {
        // Test with string progress token

        const string jsonStringToken = """
        {
            "progressToken": "string-token-123",
            "progress": 25.0,
            "total": 100.0
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonStringToken,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("string-token-123", result.ProgressToken.ToString());
        Assert.Equal(25.0f, result.Progress.Progress);
    }

    [Fact]
    public static void ProgressNotificationParams_IntegerProgressToken_Deserializes()
    {
        // Test with integer progress token

        const string jsonIntToken = """
        {
            "progressToken": 42,
            "progress": 75.0,
            "total": 100.0
        }
        """;

        var result = JsonSerializer.Deserialize<ProgressNotificationParams>(
            jsonIntToken,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        Assert.Equal("42", result.ProgressToken.ToString());
        Assert.Equal(75.0f, result.Progress.Progress);
    }
}
