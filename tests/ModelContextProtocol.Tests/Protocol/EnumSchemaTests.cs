using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class EnumSchemaTests
{
    [Fact]
    public void UntitledSingleSelectEnumSchema_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.UntitledSingleSelectEnumSchema
        {
            Title = "Priority",
            Description = "Task priority level",
            Enum = ["low", "medium", "high"],
            Default = "medium"
        };

        // Act
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(deserialized);
        Assert.Equal("string", result.Type);
        Assert.Equal("Priority", result.Title);
        Assert.Equal("Task priority level", result.Description);
        Assert.Equal(["low", "medium", "high"], result.Enum);
        Assert.Equal("medium", result.Default);
        Assert.Contains("\"type\":\"string\"", json);
        Assert.Contains("\"enum\":[\"low\",\"medium\",\"high\"]", json);
        Assert.DoesNotContain("enumNames", json);
        Assert.DoesNotContain("oneOf", json);
    }

    [Fact]
    public void TitledSingleSelectEnumSchema_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.TitledSingleSelectEnumSchema
        {
            Title = "Severity",
            Description = "Issue severity",
            OneOf =
            [
                new ElicitRequestParams.EnumSchemaOption { Const = "critical", Title = "Critical" },
                new ElicitRequestParams.EnumSchemaOption { Const = "high", Title = "High Priority" },
                new ElicitRequestParams.EnumSchemaOption { Const = "low", Title = "Low Priority" }
            ],
            Default = "high"
        };

        // Act
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(deserialized);
        Assert.Equal("string", result.Type);
        Assert.Equal("Severity", result.Title);
        Assert.Equal("Issue severity", result.Description);
        Assert.Equal(3, result.OneOf.Count);
        Assert.Equal("critical", result.OneOf[0].Const);
        Assert.Equal("Critical", result.OneOf[0].Title);
        Assert.Equal("high", result.Default);
        Assert.Contains("\"oneOf\":", json);
        Assert.Contains("\"const\":\"critical\"", json);
        Assert.Contains("\"title\":\"Critical\"", json);
        Assert.DoesNotContain("enum\":", json);
        Assert.DoesNotContain("enumNames", json);
    }

    [Fact]
    public void UntitledMultiSelectEnumSchema_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.UntitledMultiSelectEnumSchema
        {
            Title = "Tags",
            Description = "Select multiple tags",
            MinItems = 1,
            MaxItems = 3,
            Items = new ElicitRequestParams.UntitledEnumItemsSchema
            {
                Type = "string",
                Enum = ["bug", "feature", "documentation", "enhancement"]
            },
            Default = ["bug", "feature"]
        };

        // Act
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.UntitledMultiSelectEnumSchema>(deserialized);
        Assert.Equal("array", result.Type);
        Assert.Equal("Tags", result.Title);
        Assert.Equal("Select multiple tags", result.Description);
        Assert.Equal(1, result.MinItems);
        Assert.Equal(3, result.MaxItems);
        Assert.NotNull(result.Items);
        Assert.Equal("string", result.Items.Type);
        Assert.Equal(["bug", "feature", "documentation", "enhancement"], result.Items.Enum);
        Assert.Equal(["bug", "feature"], result.Default);
        Assert.Contains("\"type\":\"array\"", json);
        Assert.Contains("\"minItems\":1", json);
        Assert.Contains("\"maxItems\":3", json);
        Assert.Contains("\"items\":", json);
        Assert.DoesNotContain("anyOf", json);
    }

    [Fact]
    public void TitledMultiSelectEnumSchema_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.TitledMultiSelectEnumSchema
        {
            Title = "Features",
            Description = "Select desired features",
            MinItems = 2,
            Items = new ElicitRequestParams.TitledEnumItemsSchema
            {
                AnyOf =
                [
                    new ElicitRequestParams.EnumSchemaOption { Const = "auth", Title = "Authentication" },
                    new ElicitRequestParams.EnumSchemaOption { Const = "api", Title = "REST API" },
                    new ElicitRequestParams.EnumSchemaOption { Const = "ui", Title = "User Interface" }
                ]
            },
            Default = ["auth", "api"]
        };

        // Act
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.TitledMultiSelectEnumSchema>(deserialized);
        Assert.Equal("array", result.Type);
        Assert.Equal("Features", result.Title);
        Assert.Equal("Select desired features", result.Description);
        Assert.Equal(2, result.MinItems);
        Assert.NotNull(result.Items);
        Assert.NotNull(result.Items.AnyOf);
        Assert.Equal(3, result.Items.AnyOf.Count);
        Assert.Equal("auth", result.Items.AnyOf[0].Const);
        Assert.Equal("Authentication", result.Items.AnyOf[0].Title);
        Assert.Equal(["auth", "api"], result.Default);
        Assert.Contains("\"type\":\"array\"", json);
        Assert.Contains("\"anyOf\":", json);
        Assert.Contains("\"const\":\"auth\"", json);
        Assert.Contains("\"title\":\"Authentication\"", json);
    }

    [Fact]
    public void SingleSelectEnum_WithEnum_Deserializes_As_UntitledSingleSelect()
    {
        // Arrange - JSON with enum should deserialize as UntitledSingleSelectEnumSchema
        string json = """
            {
                "type": "string",
                "title": "Status",
                "enum": ["draft", "published", "archived"],
                "default": "draft"
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(deserialized);
        Assert.Equal("string", result.Type);
        Assert.Equal("Status", result.Title);
        Assert.Equal(["draft", "published", "archived"], result.Enum);
        Assert.Equal("draft", result.Default);
    }

    [Fact]
    public void SingleSelectEnum_WithOneOf_Deserializes_As_TitledSingleSelect()
    {
        // Arrange - JSON with oneOf should deserialize as TitledSingleSelectEnumSchema
        string json = """
            {
                "type": "string",
                "title": "Priority",
                "oneOf": [
                    { "const": "p0", "title": "P0 - Critical" },
                    { "const": "p1", "title": "P1 - High" },
                    { "const": "p2", "title": "P2 - Medium" }
                ],
                "default": "p1"
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(deserialized);
        Assert.Equal("string", result.Type);
        Assert.Equal("Priority", result.Title);
        Assert.Equal(3, result.OneOf.Count);
        Assert.Equal("p0", result.OneOf[0].Const);
        Assert.Equal("P0 - Critical", result.OneOf[0].Title);
        Assert.Equal("p1", result.Default);
    }

    [Fact]
    public void MultiSelectEnum_WithEnum_Deserializes_As_UntitledMultiSelect()
    {
        // Arrange - JSON with items.enum should deserialize as UntitledMultiSelectEnumSchema
        string json = """
            {
                "type": "array",
                "title": "Categories",
                "items": {
                    "type": "string",
                    "enum": ["tech", "business", "lifestyle"]
                },
                "default": ["tech"]
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.UntitledMultiSelectEnumSchema>(deserialized);
        Assert.Equal("array", result.Type);
        Assert.Equal("Categories", result.Title);
        Assert.NotNull(result.Items);
        Assert.Equal(["tech", "business", "lifestyle"], result.Items.Enum);
        Assert.Equal(["tech"], result.Default);
    }

    [Fact]
    public void MultiSelectEnum_WithAnyOf_Deserializes_As_TitledMultiSelect()
    {
        // Arrange - JSON with items.anyOf should deserialize as TitledMultiSelectEnumSchema
        string json = """
            {
                "type": "array",
                "title": "Roles",
                "items": {
                    "anyOf": [
                        { "const": "admin", "title": "Administrator" },
                        { "const": "user", "title": "User" },
                        { "const": "guest", "title": "Guest" }
                    ]
                },
                "default": ["user"]
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.TitledMultiSelectEnumSchema>(deserialized);
        Assert.Equal("array", result.Type);
        Assert.Equal("Roles", result.Title);
        Assert.NotNull(result.Items);
        Assert.NotNull(result.Items.AnyOf);
        Assert.Equal(3, result.Items.AnyOf.Count);
        Assert.Equal("admin", result.Items.AnyOf[0].Const);
        Assert.Equal("Administrator", result.Items.AnyOf[0].Title);
        Assert.Equal(["user"], result.Default);
    }

#pragma warning disable MCP9001 // EnumSchema and LegacyTitledEnumSchema are deprecated but supported for backward compatibility
    [Fact]
    public void LegacyTitledEnumSchema_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.LegacyTitledEnumSchema
        {
            Title = "Environment",
            Description = "Deployment environment",
            Enum = ["dev", "staging", "prod"],
            EnumNames = ["Development", "Staging", "Production"],
            Default = "staging"
        };

        // Act
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.LegacyTitledEnumSchema>(deserialized);
        Assert.Equal("string", result.Type);
        Assert.Equal("Environment", result.Title);
        Assert.Equal("Deployment environment", result.Description);
        Assert.Equal(["dev", "staging", "prod"], result.Enum);
        Assert.Equal(["Development", "Staging", "Production"], result.EnumNames);
        Assert.Equal("staging", result.Default);
        Assert.Contains("\"enumNames\":[\"Development\",\"Staging\",\"Production\"]", json);
    }

    [Fact]
    public void LegacyTitledEnumSchema_Direct_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.LegacyTitledEnumSchema
        {
            Title = "Environment",
            Description = "Deployment environment",
            Enum = ["dev", "staging", "prod"],
            EnumNames = ["Development", "Staging", "Production"],
            Default = "staging"
        };

        // Act
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.LegacyTitledEnumSchema>(deserialized);
        Assert.Equal("string", result.Type);
        Assert.Equal("Environment", result.Title);
        Assert.Equal("Deployment environment", result.Description);
        Assert.Equal(["dev", "staging", "prod"], result.Enum);
        Assert.Equal(["Development", "Staging", "Production"], result.EnumNames);
        Assert.Equal("staging", result.Default);
        Assert.Contains("\"enumNames\":[\"Development\",\"Staging\",\"Production\"]", json);
    }

    [Fact]
    public void Enum_WithEnumNames_Deserializes_As_LegacyTitledEnumSchema()
    {
        // Arrange - JSON with enumNames should deserialize as (deprecated) LegacyTitledEnumSchema
        string json = """
            {
                "type": "string",
                "title": "Environment",
                "description": "Deployment environment",
                "enum": ["dev", "staging", "prod"],
                "enumNames": ["Development", "Staging", "Production"],
                "default": "staging"
            }
            """;
        // Act
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var result = Assert.IsType<ElicitRequestParams.LegacyTitledEnumSchema>(deserialized);
        Assert.Equal("string", result.Type);
        Assert.Equal("Environment", result.Title);
        Assert.Equal("Deployment environment", result.Description);
        Assert.Equal(["dev", "staging", "prod"], result.Enum);
        Assert.Equal(["Development", "Staging", "Production"], result.EnumNames);
        Assert.Equal("staging", result.Default);
    }
#pragma warning restore MCP9001
}
