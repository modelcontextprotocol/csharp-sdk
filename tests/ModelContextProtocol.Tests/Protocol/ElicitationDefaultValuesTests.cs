using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class ElicitationDefaultValuesTests
{
    [Fact]
    public void StringSchema_Default_Serializes_Correctly()
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
    public void StringSchema_Default_Null_DoesNotSerialize()
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
    public void NumberSchema_Default_Serializes_Correctly()
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
    public void NumberSchema_Integer_Default_Serializes_Correctly()
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
    public void NumberSchema_Default_Null_DoesNotSerialize()
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
    public void EnumSchema_Default_Serializes_Correctly()
    {
        // Arrange
        var schema = new ElicitRequestParams.EnumSchema
        {
            Title = "Priority",
            Description = "Task priority",
            Enum = ["low", "medium", "high"],
            EnumNames = ["Low Priority", "Medium Priority", "High Priority"],
            Default = "medium"
        };

        // Act - serialize as base type to use the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var enumSchema = Assert.IsType<ElicitRequestParams.EnumSchema>(deserialized);
        Assert.Equal("medium", enumSchema.Default);
        Assert.Equal("Priority", enumSchema.Title);
        Assert.Equal("Task priority", enumSchema.Description);
        Assert.Contains("\"default\":\"medium\"", json);
    }

    [Fact]
    public void EnumSchema_Default_Null_DoesNotSerialize()
    {
        // Arrange
        var schema = new ElicitRequestParams.EnumSchema
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
    public void BooleanSchema_Default_True_Serializes_Correctly()
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
    public void BooleanSchema_Default_False_Serializes_Correctly()
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
    public void PrimitiveSchemaDefinition_StringSchema_WithDefault_RoundTrips()
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
    public void PrimitiveSchemaDefinition_NumberSchema_WithDefault_RoundTrips()
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
    public void PrimitiveSchemaDefinition_EnumSchema_WithDefault_RoundTrips()
    {
        // Arrange
        var schema = new ElicitRequestParams.EnumSchema
        {
            Title = "Status",
            Enum = ["draft", "published", "archived"],
            Default = "draft"
        };

        // Act - serialize as base type to test the converter
        string json = JsonSerializer.Serialize<ElicitRequestParams.PrimitiveSchemaDefinition>(schema, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var enumSchema = Assert.IsType<ElicitRequestParams.EnumSchema>(deserialized);
        Assert.Equal("draft", enumSchema.Default);
        Assert.Equal(["draft", "published", "archived"], enumSchema.Enum);
    }

    [Fact]
    public void RequestSchema_WithAllDefaultTypes_Serializes_Correctly()
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
                    ["status"] = new ElicitRequestParams.EnumSchema
                    {
                        Title = "Status",
                        Enum = ["active", "inactive"],
                        Default = "active"
                    }
                }
            }
        };

        // Act
        string json = JsonSerializer.Serialize(requestParams, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(5, deserialized.RequestedSchema.Properties.Count);
        
        var nameSchema = Assert.IsType<ElicitRequestParams.StringSchema>(deserialized.RequestedSchema.Properties["name"]);
        Assert.Equal("John Doe", nameSchema.Default);
        
        var ageSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(deserialized.RequestedSchema.Properties["age"]);
        Assert.Equal(30, ageSchema.Default);
        
        var scoreSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(deserialized.RequestedSchema.Properties["score"]);
        Assert.Equal(85.5, scoreSchema.Default);
        
        var activeSchema = Assert.IsType<ElicitRequestParams.BooleanSchema>(deserialized.RequestedSchema.Properties["active"]);
        Assert.True(activeSchema.Default);
        
        var statusSchema = Assert.IsType<ElicitRequestParams.EnumSchema>(deserialized.RequestedSchema.Properties["status"]);
        Assert.Equal("active", statusSchema.Default);
    }
}
