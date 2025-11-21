using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class PrimitiveSchemaDefinitionTests
{
    [Fact]
    public static void StringSchema_UnknownArrayProperty_IsIgnored()
    {
        // This test verifies that the PrimitiveSchemaDefinition.Converter properly skips unknown properties
        // even when they contain complex structures like arrays or objects.
        //
        // In this unexpected JSON, "unknownArray" appears inside a string schema (where it doesn't belong).
        // The converter should gracefully ignore this unknown property and successfully deserialize
        // the rest of the schema.

        const string jsonWithUnknownArray = """
        {
            "type": "string",
            "title": "Test String",
            "minLength": 1,
            "maxLength": 100,
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

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithUnknownArray,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var stringSchema = Assert.IsType<ElicitRequestParams.StringSchema>(result);
        Assert.Equal("string", stringSchema.Type);
        Assert.Equal("Test String", stringSchema.Title);
        Assert.Equal(1, stringSchema.MinLength);
        Assert.Equal(100, stringSchema.MaxLength);
    }

    [Fact]
    public static void NumberSchema_UnknownObjectProperty_IsIgnored()
    {
        // Test that unknown properties with nested objects are properly skipped

        const string jsonWithUnknownObject = """
        {
            "type": "number",
            "description": "Test Number",
            "minimum": 0,
            "maximum": 1000,
            "unknownObject": {
                "deeply": {
                    "nested": {
                        "value": "should be ignored"
                    }
                }
            }
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithUnknownObject,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var numberSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(result);
        Assert.Equal("number", numberSchema.Type);
        Assert.Equal("Test Number", numberSchema.Description);
        Assert.Equal(0, numberSchema.Minimum);
        Assert.Equal(1000, numberSchema.Maximum);
    }

    [Fact]
    public static void BooleanSchema_UnknownMixedProperties_AreIgnored()
    {
        // Test multiple unknown properties with different types

        const string jsonWithMixedUnknown = """
        {
            "type": "boolean",
            "title": "Test Boolean",
            "unknownString": "value",
            "unknownNumber": 42,
            "unknownArray": [1, 2, 3],
            "unknownObject": {"key": "value"},
            "unknownBool": true,
            "unknownNull": null,
            "default": false
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithMixedUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var boolSchema = Assert.IsType<ElicitRequestParams.BooleanSchema>(result);
        Assert.Equal("boolean", boolSchema.Type);
        Assert.Equal("Test Boolean", boolSchema.Title);
        Assert.False(boolSchema.Default);
    }

    [Fact]
    public static void EnumSchema_UnknownNestedArrays_AreIgnored()
    {
        // Test complex unknown properties with arrays of objects

        const string jsonWithNestedArrays = """
        {
            "type": "string",
            "enum": ["option1", "option2", "option3"],
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
            ],
            "default": "option1"
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithNestedArrays,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var enumSchema = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(result);
        Assert.Equal("string", enumSchema.Type);
        Assert.Equal(3, enumSchema.Enum.Count);
        Assert.Contains("option1", enumSchema.Enum);
        Assert.Contains("option2", enumSchema.Enum);
        Assert.Contains("option3", enumSchema.Enum);
        Assert.Equal("option1", enumSchema.Default);
    }

    [Fact]
    public static void StringSchema_MultipleUnknownProperties_AllIgnored()
    {
        // Test that multiple unknown properties are all properly skipped

        const string jsonWithMultipleUnknown = """
        {
            "type": "string",
            "title": "Test",
            "unknownOne": {"a": 1},
            "minLength": 5,
            "unknownTwo": [1, 2, 3],
            "maxLength": 50,
            "unknownThree": {"b": {"c": "d"}},
            "format": "email"
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithMultipleUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var stringSchema = Assert.IsType<ElicitRequestParams.StringSchema>(result);
        Assert.Equal("string", stringSchema.Type);
        Assert.Equal("Test", stringSchema.Title);
        Assert.Equal(5, stringSchema.MinLength);
        Assert.Equal(50, stringSchema.MaxLength);
        Assert.Equal("email", stringSchema.Format);
    }

    [Fact]
    public static void IntegerSchema_UnknownArrayOfArrays_IsIgnored()
    {
        // Test deeply nested array structures in unknown properties

        const string jsonWithArrayOfArrays = """
        {
            "type": "integer",
            "minimum": 1,
            "maximum": 100,
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

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithArrayOfArrays,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var numberSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(result);
        Assert.Equal("integer", numberSchema.Type);
        Assert.Equal(1, numberSchema.Minimum);
        Assert.Equal(100, numberSchema.Maximum);
    }

    [Fact]
    public static void StringSchema_EmptyUnknownArray_IsIgnored()
    {
        // Test empty arrays in unknown properties

        const string jsonWithEmptyArray = """
        {
            "type": "string",
            "description": "Test",
            "unknownEmpty": [],
            "minLength": 0
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithEmptyArray,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var stringSchema = Assert.IsType<ElicitRequestParams.StringSchema>(result);
        Assert.Equal("string", stringSchema.Type);
        Assert.Equal("Test", stringSchema.Description);
        Assert.Equal(0, stringSchema.MinLength);
    }

    [Fact]
    public static void NumberSchema_EmptyUnknownObject_IsIgnored()
    {
        // Test empty objects in unknown properties

        const string jsonWithEmptyObject = """
        {
            "type": "number",
            "title": "Test Number",
            "unknownEmpty": {},
            "minimum": 0.0,
            "maximum": 100.0
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithEmptyObject,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var numberSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(result);
        Assert.Equal("number", numberSchema.Type);
        Assert.Equal("Test Number", numberSchema.Title);
        Assert.Equal(0.0, numberSchema.Minimum);
        Assert.Equal(100.0, numberSchema.Maximum);
    }

    [Fact]
    public static void EnumSchema_UnknownPropertiesBetweenRequired_AreIgnored()
    {
        // Test unknown properties interspersed with required ones

        const string jsonWithInterspersedUnknown = """
        {
            "unknownFirst": {"x": 1},
            "type": "string",
            "unknownSecond": [1, 2],
            "enum": ["a", "b"],
            "unknownThird": {"nested": {"value": true}}
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithInterspersedUnknown,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var enumSchema = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(result);
        Assert.Equal("string", enumSchema.Type);
        Assert.Equal(2, enumSchema.Enum.Count);
        Assert.Contains("a", enumSchema.Enum);
        Assert.Contains("b", enumSchema.Enum);
    }

    [Fact]
    public static void BooleanSchema_VeryDeeplyNestedUnknown_IsIgnored()
    {
        // Test very deeply nested structures in unknown properties

        const string jsonWithVeryDeepNesting = """
        {
            "type": "boolean",
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
            },
            "default": true
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            jsonWithVeryDeepNesting,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var boolSchema = Assert.IsType<ElicitRequestParams.BooleanSchema>(result);
        Assert.Equal("boolean", boolSchema.Type);
        Assert.True(boolSchema.Default);
    }

    [Fact]
    public static void EnumSchema_Deserialization_PreservesKnownProperties()
    {
        // Test deserialization of enum schema with all properties

        const string enumSchemaJson = """
        {
            "type": "string",
            "title": "Test Enum",
            "description": "A test enum schema",
            "enum": ["option1", "option2", "option3"],
            "default": "option2"
        }
        """;

        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            enumSchemaJson,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        var enumSchema = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(deserialized);
        Assert.Equal("string", enumSchema.Type);
        Assert.Equal("Test Enum", enumSchema.Title);
        Assert.Equal("A test enum schema", enumSchema.Description);
        Assert.Equal(3, enumSchema.Enum.Count);
        Assert.Contains("option1", enumSchema.Enum);
        Assert.Contains("option2", enumSchema.Enum);
        Assert.Contains("option3", enumSchema.Enum);
        Assert.Equal("option2", enumSchema.Default);
    }
}
