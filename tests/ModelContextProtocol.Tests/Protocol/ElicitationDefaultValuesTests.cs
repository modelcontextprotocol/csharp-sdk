using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ElicitationDefaultValuesTests
{
    [Fact]
    public static void StringSchema_Default_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.StringSchema
        {
            Title = "Name",
            Description = "User's name",
            Default = "John Doe"
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var stringSchema = Assert.IsType<ElicitRequestParams.StringSchema>(deserialized);
        Assert.Equal("John Doe", stringSchema.Default);
        Assert.Equal("Name", stringSchema.Title);
        Assert.Equal("User's name", stringSchema.Description);
        Assert.Contains("\"default\":\"John Doe\"", json);
    }

    [Fact]
    public static void StringSchema_Default_Null_DoesNotSerialize()
    {
        // Arrange
        var schema = new ElicitRequestParams.StringSchema
        {
            Title = "Name"
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.DoesNotContain("\"default\"", json);
    }

    [Fact]
    public static void NumberSchema_Default_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.NumberSchema
        {
            Title = "Age",
            Description = "User's age",
            Default = 25.5,
            Minimum = 0,
            Maximum = 150
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var numberSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(deserialized);
        Assert.Equal(25.5, numberSchema.Default);
        Assert.Equal("Age", numberSchema.Title);
        Assert.Equal("User's age", numberSchema.Description);
        Assert.Contains("\"default\":25.5", json);
    }

    [Fact]
    public static void NumberSchema_Integer_Default_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.NumberSchema
        {
            Type = "integer",
            Title = "Count",
            Default = 42
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var numberSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(deserialized);
        Assert.Equal(42, numberSchema.Default);
        Assert.Equal("Count", numberSchema.Title);
        Assert.Contains("\"default\":42", json);
    }

    [Fact]
    public static void NumberSchema_Default_Null_DoesNotSerialize()
    {
        // Arrange
        var schema = new ElicitRequestParams.NumberSchema
        {
            Title = "Age"
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.DoesNotContain("\"default\"", json);
    }

    [Fact]
    public static void BooleanSchema_Default_True_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.BooleanSchema
        {
            Title = "Active",
            Description = "Is user active",
            Default = true
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var booleanSchema = Assert.IsType<ElicitRequestParams.BooleanSchema>(deserialized);
        Assert.True(booleanSchema.Default);
        Assert.Contains("\"default\":true", json);
    }

    [Fact]
    public static void BooleanSchema_Default_False_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.BooleanSchema
        {
            Title = "Active",
            Default = false
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var booleanSchema = Assert.IsType<ElicitRequestParams.BooleanSchema>(deserialized);
        Assert.False(booleanSchema.Default);
        Assert.Contains("\"default\":false", json);
    }

    [Fact]
    public static void PrimitiveSchemaDefinition_StringSchema_WithDefault_RoundTrips()
    {
        // Arrange
        var schema = new ElicitRequestParams.StringSchema
        {
            Title = "Email",
            Format = "email",
            Default = "user@example.com"
        };

        // Act - serialize as base type to test the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var stringSchema = Assert.IsType<ElicitRequestParams.StringSchema>(deserialized);
        Assert.Equal("user@example.com", stringSchema.Default);
        Assert.Equal("email", stringSchema.Format);
    }

    [Fact]
    public static void PrimitiveSchemaDefinition_NumberSchema_WithDefault_RoundTrips()
    {
        // Arrange
        var schema = new ElicitRequestParams.NumberSchema
        {
            Title = "Score",
            Minimum = 0,
            Maximum = 100,
            Default = 75.5
        };

        // Act - serialize as base type to test the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var numberSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(deserialized);
        Assert.Equal(75.5, numberSchema.Default);
        Assert.Equal(0, numberSchema.Minimum);
        Assert.Equal(100, numberSchema.Maximum);
    }

    [Fact]
    public static void UntitledSingleSelectEnumSchema_Default_Null_DoesNotSerialize()
    {
        // Arrange
        var schema = new ElicitRequestParams.UntitledSingleSelectEnumSchema
        {
            Title = "Priority",
            Enum = ["low", "medium", "high"]
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.DoesNotContain("\"default\"", json);
    }

    [Fact]
    public static void TitledSingleSelectEnumSchema_Default_Null_DoesNotSerialize()
    {
        // Arrange
        var schema = new ElicitRequestParams.TitledSingleSelectEnumSchema
        {
            Title = "Priority",
            OneOf =
            {
                new ElicitRequestParams.EnumSchemaOption { Title = "Low", Const = "low" },
                new ElicitRequestParams.EnumSchemaOption { Title = "Medium", Const = "medium" },
                new ElicitRequestParams.EnumSchemaOption { Title = "High", Const = "high" }
            }
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.DoesNotContain("\"default\"", json);
    }

    [Fact]
    public static void UntitledMultiSelectEnumSchema_Default_Null_DoesNotSerialize()
    {
        // Arrange
        var schema = new ElicitRequestParams.UntitledMultiSelectEnumSchema
        {
            Title = "Tags",
            Items = new ElicitRequestParams.UntitledEnumItemsSchema
            {
                Enum = ["tag1", "tag2", "tag3"]
            }
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.DoesNotContain("\"default\"", json);
    }

    [Fact]
    public static void TitledMultiSelectEnumSchema_Default_Null_DoesNotSerialize()
    {
        // Arrange
        var schema = new ElicitRequestParams.TitledMultiSelectEnumSchema
        {
            Title = "Tags",
            Items = new ElicitRequestParams.TitledEnumItemsSchema
            {
                AnyOf =
                [
                    new ElicitRequestParams.EnumSchemaOption { Title = "Tag 1", Const = "tag1" },
                    new ElicitRequestParams.EnumSchemaOption { Title = "Tag 2", Const = "tag2" },
                    new ElicitRequestParams.EnumSchemaOption { Title = "Tag 3", Const = "tag3" }
                ]
            }
        };
        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.DoesNotContain("\"default\"", json);
    }

#pragma warning disable MCP9001 // LegacyTitledEnumSchema is deprecated but supported for backward compatibility
    [Fact]
    public static void LegacyTitledEnumSchema_Default_Null_DoesNotSerialize()
    {
        // Arrange
        var schema = new ElicitRequestParams.LegacyTitledEnumSchema
        {
            Title = "Legacy Options",
            Enum = ["option1", "option2"],
            EnumNames = ["Option 1", "Option 2"]
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.DoesNotContain("\"default\"", json);
    }
#pragma warning restore MCP9001

    [Fact]
    public static void RequestSchema_WithAllDefaultTypes_Serializes_Correctly()
    {
        // Arrange
        var requestParams = new ElicitRequestParams
        {
            Message = "Please fill out the form",
            RequestedSchema = new()
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["name"] = new ElicitRequestParams.StringSchema
                    {
                        Title = "Name",
                        Default = "John Doe"
                    },
                    ["age"] = new ElicitRequestParams.NumberSchema
                    {
                        Title = "Age",
                        Type = "integer",
                        Default = 30
                    },
                    ["score"] = new ElicitRequestParams.NumberSchema
                    {
                        Title = "Score",
                        Default = 85.5
                    },
                    ["active"] = new ElicitRequestParams.BooleanSchema
                    {
                        Title = "Active",
                        Default = true
                    },
                    ["status"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                    {
                        Title = "Status",
                        Enum = ["active", "inactive"],
                        Default = "active"
                    },
                    ["tags"] = new ElicitRequestParams.UntitledMultiSelectEnumSchema
                    {
                        Title = "Tags",
                        Items = new ElicitRequestParams.UntitledEnumItemsSchema
                        {
                            Enum = ["tag1", "tag2", "tag3"]
                        },
                        Default = ["tag1", "tag3"]
                    },
                    ["salutation"] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                    {
                        Title = "Salutation",
                        OneOf =
                        {
                            new ElicitRequestParams.EnumSchemaOption { Title = "N/A", Const = "none" },
                            new ElicitRequestParams.EnumSchemaOption { Title = "Mr.", Const = "mr" },
                            new ElicitRequestParams.EnumSchemaOption { Title = "Ms.", Const = "ms" },
                            new ElicitRequestParams.EnumSchemaOption { Title = "Dr.", Const = "dr" }
                        },
                        Default = "none"
                    },
                    ["categories"] = new ElicitRequestParams.TitledMultiSelectEnumSchema
                    {
                        Title = "Categories",
                        Items = new ElicitRequestParams.TitledEnumItemsSchema
                        {
                            AnyOf =
                            [
                                new ElicitRequestParams.EnumSchemaOption { Title = "Category 1", Const = "cat1" },
                                new ElicitRequestParams.EnumSchemaOption { Title = "Category 2", Const = "cat2" },
                                new ElicitRequestParams.EnumSchemaOption { Title = "Category 3", Const = "cat3" }
                            ]
                        },
                        Default = ["cat2", "cat3"]
                    }
                }
            }
        };

        // Act
        string json = JsonSerializer.Serialize(requestParams, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.RequestedSchema);
        Assert.Equal(8, deserialized.RequestedSchema.Properties.Count);

        var nameSchema = Assert.IsType<ElicitRequestParams.StringSchema>(deserialized.RequestedSchema.Properties["name"]);
        Assert.Equal("John Doe", nameSchema.Default);

        var ageSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(deserialized.RequestedSchema.Properties["age"]);
        Assert.Equal(30, ageSchema.Default);

        var scoreSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(deserialized.RequestedSchema.Properties["score"]);
        Assert.Equal(85.5, scoreSchema.Default);

        var activeSchema = Assert.IsType<ElicitRequestParams.BooleanSchema>(deserialized.RequestedSchema.Properties["active"]);
        Assert.True(activeSchema.Default);

        var statusSchema = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(deserialized.RequestedSchema.Properties["status"]);
        Assert.Equal("active", statusSchema.Default);

        var tagsSchema = Assert.IsType<ElicitRequestParams.UntitledMultiSelectEnumSchema>(deserialized.RequestedSchema.Properties["tags"]);
        Assert.Equal(new List<string> { "tag1", "tag3" }, tagsSchema.Default);

        var salutationSchema = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(deserialized.RequestedSchema.Properties["salutation"]);
        Assert.Equal("none", salutationSchema.Default);

        var categoriesSchema = Assert.IsType<ElicitRequestParams.TitledMultiSelectEnumSchema>(deserialized.RequestedSchema.Properties["categories"]);
        Assert.Equal(new List<string> { "cat2", "cat3" }, categoriesSchema.Default);
    }
}
