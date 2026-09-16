using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class PrimitiveSchemaDefinitionTests
{
    [Fact]
    public static void StringSchema_UnknownProperties_AreIgnored()
    {
        const string json = """
        {
            "type": "string",
            "title": "Test",

            "unknownNull": null,
            "unknownEmptyObject": {},
            "unknownObject": {"a": 1},
            "unknownEmptyArray": [],
            "unknownArray": [1, 2, 3],
            "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

            "minLength": 5,
            "maxLength": 50,
            "format": "email"
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            json,
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
    public static void NumberSchema_UnknownProperties_AreIgnored()
    {
        const string json = """
        {
            "type": "number",
            "description": "Test Number",

            "unknownNull": null,
            "unknownEmptyObject": {},
            "unknownObject": {"a": 1},
            "unknownEmptyArray": [],
            "unknownArray": [1, 2, 3],
            "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

            "minimum": 0,
            "maximum": 1000
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            json,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var numberSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(result);
        Assert.Equal("number", numberSchema.Type);
        Assert.Equal("Test Number", numberSchema.Description);
        Assert.Equal(0, numberSchema.Minimum);
        Assert.Equal(1000, numberSchema.Maximum);
    }

    [Fact]
    public static void BooleanSchema_UnknownProperties_AreIgnored()
    {
        const string json = """
        {
            "type": "boolean",
            "title": "Test Boolean",

            "unknownNull": null,
            "unknownEmptyObject": {},
            "unknownObject": {"a": 1},
            "unknownEmptyArray": [],
            "unknownArray": [1, 2, 3],
            "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

            "default": false
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            json,
            McpJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var boolSchema = Assert.IsType<ElicitRequestParams.BooleanSchema>(result);
        Assert.Equal("boolean", boolSchema.Type);
        Assert.Equal("Test Boolean", boolSchema.Title);
        Assert.False(boolSchema.Default);
    }

    [Fact]
    public static void UntitledSingleSelectEnumSchema_UnknownProperties_AreIgnored()
    {
        const string json = """
        {
            "type": "string",
            "enum": ["option1", "option2", "option3"],

            "unknownNull": null,
            "unknownEmptyObject": {},
            "unknownObject": {"a": 1},
            "unknownEmptyArray": [],
            "unknownArray": [1, 2, 3],
            "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

            "default": "option1"
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            json,
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
    public static void TitledSingleSelectEnumSchema_UnknownProperties_AreIgnored()
    {
        const string json = """
        {
            "type": "string",
            "oneOf": [
                {"const": "option1", "title": "Option 1"},
                {"const": "option2", "title": "Option 2"}
            ],

            "unknownNull": null,
            "unknownEmptyObject": {},
            "unknownObject": {"a": 1},
            "unknownEmptyArray": [],
            "unknownArray": [1, 2, 3],
            "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

            "default": "option2"
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            json,
            McpJsonUtilities.DefaultOptions);
        Assert.NotNull(result);
        var enumSchema = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(result);
        Assert.Equal("string", enumSchema.Type);
        Assert.Equal(2, enumSchema.OneOf.Count);
        Assert.Contains(enumSchema.OneOf, option => option.Const == "option1" && option.Title == "Option 1");
        Assert.Contains(enumSchema.OneOf, option => option.Const == "option2" && option.Title == "Option 2");
        Assert.Equal("option2", enumSchema.Default);
    }

    [Fact]
    public static void UntitledMultiSelectEnumSchema_UnknownProperties_AreIgnored()
    {
        const string json = """
        {
            "type": "array",
            "items": {
                "unknownNull": null,
                "unknownEmptyObject": {},
                "unknownObject": {"a": 1},
                "unknownEmptyArray": [],
                "unknownArray": [1, 2, 3],
                "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

                "enum": ["optionA", "optionB", "optionC"]
            },

            "unknownNull": null,
            "unknownEmptyObject": {},
            "unknownObject": {"a": 1},
            "unknownEmptyArray": [],
            "unknownArray": [1, 2, 3],
            "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

            "default": ["optionA", "optionC"]
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            json,
            McpJsonUtilities.DefaultOptions);
        Assert.NotNull(result);
        var enumSchema = Assert.IsType<ElicitRequestParams.UntitledMultiSelectEnumSchema>(result);
        Assert.Equal("array", enumSchema.Type);
        Assert.Equal(3, enumSchema.Items.Enum.Count);
        Assert.Contains("optionA", enumSchema.Items.Enum);
        Assert.Contains("optionB", enumSchema.Items.Enum);
        Assert.Contains("optionC", enumSchema.Items.Enum);
        Assert.Equal(2, enumSchema.Default!.Count);
        Assert.Contains("optionA", enumSchema.Default);
        Assert.Contains("optionC", enumSchema.Default);
    }

    [Fact]
    public static void TitledMultiSelectEnumSchema_UnknownProperties_AreIgnored()
    {
        const string json = """
        {
            "type": "array",
            "items": {
                "unknownNull": null,
                "unknownEmptyObject": {},
                "unknownObject": {"a": 1},
                "unknownEmptyArray": [],
                "unknownArray": [1, 2, 3],
                "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

                "anyOf": [
                    {"const": "optionX", "title": "Option X"},
                    {"const": "optionY", "title": "Option Y"},
                    {"const": "optionZ", "title": "Option Z"}
                ]
            },

            "unknownNull": null,
            "unknownEmptyObject": {},
            "unknownObject": {"a": 1},
            "unknownEmptyArray": [],
            "unknownArray": [1, 2, 3],
            "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

            "default": ["optionX", "optionZ"]
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            json,
            McpJsonUtilities.DefaultOptions);
        Assert.NotNull(result);
        var enumSchema = Assert.IsType<ElicitRequestParams.TitledMultiSelectEnumSchema>(result);
        Assert.Equal("array", enumSchema.Type);
        Assert.Equal(3, enumSchema.Items.AnyOf.Count);
        Assert.Contains(enumSchema.Items.AnyOf, option => option.Const == "optionX" && option.Title == "Option X");
        Assert.Contains(enumSchema.Items.AnyOf, option => option.Const == "optionY" && option.Title == "Option Y");
        Assert.Contains(enumSchema.Items.AnyOf, option => option.Const == "optionZ" && option.Title == "Option Z");
        Assert.Equal(2, enumSchema.Default!.Count);
        Assert.Contains("optionX", enumSchema.Default);
        Assert.Contains("optionZ", enumSchema.Default);
    }

#pragma warning disable MCP9001 // LegacyTitledEnumSchema is deprecated but supported for backward compatibility
    [Fact]
    public static void LegacyTitledEnumSchema_UnknownProperties_AreIgnored()
    {
        const string json = """
        {
            "type": "string",
            "enum": ["option1", "option2"],

            "unknownNull": null,
            "unknownEmptyObject": {},
            "unknownObject": {"a": 1},
            "unknownEmptyArray": [],
            "unknownArray": [1, 2, 3],
            "unknownNestedObject": {"b": {"c": "d", "e": ["f"]}},

            "enumNames": ["Option 1", "Option 2"],
            "default": "option2"
        }
        """;

        var result = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(
            json,
            McpJsonUtilities.DefaultOptions);
        Assert.NotNull(result);
        var enumSchema = Assert.IsType<ElicitRequestParams.LegacyTitledEnumSchema>(result);
        Assert.Equal("string", enumSchema.Type);
        Assert.Equal(2, enumSchema.Enum.Count);
        Assert.Contains("option1", enumSchema.Enum);
        Assert.Contains("option2", enumSchema.Enum);
        Assert.Contains("Option 1", enumSchema.EnumNames!);
        Assert.Contains("Option 2", enumSchema.EnumNames!);
    }
#pragma warning restore MCP9001
}
