using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public partial class McpMetaAttributeTests
{
    #region Direct Attribute Instantiation Tests

    [Fact]
    public void McpMetaAttribute_StringConstructor_WithValue_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", "test-value");
        
        Assert.Equal("key", attr.Name);
        Assert.Equal("\"test-value\"", attr.JsonValue);
        
        // Verify it can be parsed back as JSON
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(JsonValueKind.String, node.GetValueKind());
        Assert.Equal("test-value", node.GetValue<string>());
    }

    [Fact]
    public void McpMetaAttribute_StringConstructor_WithNull_SerializesAsJsonNull()
    {
        var attr = new McpMetaAttribute("key", (string?)null);
        
        Assert.Equal("key", attr.Name);
        Assert.Equal("null", attr.JsonValue);
        
        // Verify it parses as JSON null
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.Null(node);
    }

    [Fact]
    public void McpMetaAttribute_StringConstructor_WithEmptyString_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", "");
        
        Assert.Equal("key", attr.Name);
        Assert.Equal("\"\"", attr.JsonValue);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal("", node.GetValue<string>());
    }

    [Fact]
    public void McpMetaAttribute_StringConstructor_WithSpecialCharacters_RoundtripsCorrectly()
    {
        var testString = "Line1\nLine2\tTab\"Quote";
        var attr = new McpMetaAttribute("key", testString);
        
        Assert.Equal("key", attr.Name);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(testString, node.GetValue<string>());
    }

    [Fact]
    public void McpMetaAttribute_StringConstructor_WithUnicode_RoundtripsCorrectly()
    {
        var testString = "Hello 世界 🌍";
        var attr = new McpMetaAttribute("key", testString);
        
        Assert.Equal("key", attr.Name);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(testString, node.GetValue<string>());
    }

    [Fact]
    public void McpMetaAttribute_DoubleConstructor_WithPositiveValue_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", 3.14159);
        
        Assert.Equal("key", attr.Name);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(JsonValueKind.Number, node.GetValueKind());
        Assert.Equal(3.14159, node.GetValue<double>());
    }

    [Fact]
    public void McpMetaAttribute_DoubleConstructor_WithNegativeValue_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", -999.999);
        
        Assert.Equal("key", attr.Name);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(JsonValueKind.Number, node.GetValueKind());
        Assert.Equal(-999.999, node.GetValue<double>());
    }

    [Fact]
    public void McpMetaAttribute_DoubleConstructor_WithZero_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", 0.0);
        
        Assert.Equal("key", attr.Name);
        Assert.Equal("0", attr.JsonValue);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(JsonValueKind.Number, node.GetValueKind());
        Assert.Equal(0.0, node.GetValue<double>());
    }

    [Fact]
    public void McpMetaAttribute_DoubleConstructor_WithIntegerValue_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", 42.0);
        
        Assert.Equal("key", attr.Name);
        Assert.Equal("42", attr.JsonValue);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(42.0, node.GetValue<double>());
    }

    [Fact]
    public void McpMetaAttribute_DoubleConstructor_WithMaxValue_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", double.MaxValue);
        
        Assert.Equal("key", attr.Name);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(double.MaxValue, node.GetValue<double>());
    }

    [Fact]
    public void McpMetaAttribute_DoubleConstructor_WithMinValue_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", double.MinValue);
        
        Assert.Equal("key", attr.Name);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(double.MinValue, node.GetValue<double>());
    }

    [Fact]
    public void McpMetaAttribute_DoubleConstructor_WithVerySmallValue_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", 0.000001);
        
        Assert.Equal("key", attr.Name);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        var value = node.GetValue<double>();
        Assert.True(Math.Abs(0.000001 - value) < 0.0000001);
    }

    [Fact]
    public void McpMetaAttribute_BoolConstructor_WithTrue_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", true);
        
        Assert.Equal("key", attr.Name);
        Assert.Equal("true", attr.JsonValue);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(JsonValueKind.True, node.GetValueKind());
        Assert.True(node.GetValue<bool>());
    }

    [Fact]
    public void McpMetaAttribute_BoolConstructor_WithFalse_RoundtripsCorrectly()
    {
        var attr = new McpMetaAttribute("key", false);
        
        Assert.Equal("key", attr.Name);
        Assert.Equal("false", attr.JsonValue);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(JsonValueKind.False, node.GetValueKind());
        Assert.False(node.GetValue<bool>());
    }

    [Fact]
    public void McpMetaAttribute_JsonValueProperty_CanBeOverridden()
    {
        var attr = new McpMetaAttribute("key", "original");
        
        Assert.Equal("\"original\"", attr.JsonValue);
        
        // Override with custom JSON
        attr.JsonValue = """{"custom": "value", "num": 123}""";
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        Assert.Equal(JsonValueKind.Object, node.GetValueKind());
        
        var obj = node.AsObject();
        Assert.Equal("value", obj["custom"]?.GetValue<string>());
        Assert.Equal(123, obj["num"]?.GetValue<int>());
    }

    [Fact]
    public void McpMetaAttribute_JsonValueProperty_SupportsComplexTypes()
    {
        var attr = new McpMetaAttribute("key", "placeholder")
        {
            JsonValue = """{"nested": {"deep": "value"}, "array": [1, 2, 3]}"""
        };
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.NotNull(node);
        
        var obj = node.AsObject();
        var nested = obj["nested"]?.AsObject();
        Assert.Equal("value", nested?["deep"]?.GetValue<string>());
        
        var array = obj["array"]?.AsArray();
        Assert.Equal(3, array?.Count);
        Assert.Equal(1, array?[0]?.GetValue<int>());
        Assert.Equal(2, array?[1]?.GetValue<int>());
        Assert.Equal(3, array?[2]?.GetValue<int>());
    }

    [Fact]
    public void McpMetaAttribute_StringConstructor_WithDefaultParameter_UsesNull()
    {
        var attr = new McpMetaAttribute("key");
        
        Assert.Equal("key", attr.Name);
        Assert.Equal("null", attr.JsonValue);
        
        var node = JsonNode.Parse(attr.JsonValue);
        Assert.Null(node);
    }

    [Fact]
    public void McpMetaAttribute_Name_CanContainSpecialCharacters()
    {
        var attr = new McpMetaAttribute("my-key_with.special/chars", "value");
        
        Assert.Equal("my-key_with.special/chars", attr.Name);
    }

    [Fact]
    public void McpMetaAttribute_MultipleInstances_AreIndependent()
    {
        var attr1 = new McpMetaAttribute("key1", "value1");
        var attr2 = new McpMetaAttribute("key2", 42.0);
        var attr3 = new McpMetaAttribute("key3", true);
        
        Assert.Equal("key1", attr1.Name);
        Assert.Equal("\"value1\"", attr1.JsonValue);
        
        Assert.Equal("key2", attr2.Name);
        Assert.Equal("42", attr2.JsonValue);
        
        Assert.Equal("key3", attr3.Name);
        Assert.Equal("true", attr3.JsonValue);
        
        // Modifying one doesn't affect others
        attr1.JsonValue = "\"modified\"";
        Assert.Equal("\"modified\"", attr1.JsonValue);
        Assert.Equal("42", attr2.JsonValue);
        Assert.Equal("true", attr3.JsonValue);
    }

    [Fact]
    public void McpServerTool_Create_WithStringMeta_PopulatesToolMeta()
    {
        var method = typeof(TestToolStringMetaClass).GetMethod(nameof(TestToolStringMetaClass.ToolWithStringMeta))!;
        
        var tool = McpServerTool.Create(method, target: null);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal(3, tool.ProtocolTool.Meta.Count);
        Assert.Equal("value1", tool.ProtocolTool.Meta["key1"]?.GetValue<string>());
        Assert.Equal("value2", tool.ProtocolTool.Meta["key2"]?.GetValue<string>());
        Assert.Null(tool.ProtocolTool.Meta["key3"]);
    }

    [Fact]
    public void McpServerTool_Create_WithDoubleMeta_PopulatesToolMeta()
    {
        var method = typeof(TestToolDoubleMetaClass2).GetMethod(nameof(TestToolDoubleMetaClass2.ToolWithDoubleMeta))!;
        
        var tool = McpServerTool.Create(method, target: null);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal(3, tool.ProtocolTool.Meta.Count);
        Assert.Equal(3.14, tool.ProtocolTool.Meta["pi"]?.GetValue<double>());
        Assert.Equal(0.0, tool.ProtocolTool.Meta["zero"]?.GetValue<double>());
        Assert.Equal(-1.5, tool.ProtocolTool.Meta["negative"]?.GetValue<double>());
    }

    [Fact]
    public void McpServerTool_Create_WithBoolMeta_PopulatesToolMeta()
    {
        var method = typeof(TestToolBoolMetaClass).GetMethod(nameof(TestToolBoolMetaClass.ToolWithBoolMeta))!;
        
        var tool = McpServerTool.Create(method, target: null);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal(2, tool.ProtocolTool.Meta.Count);
        Assert.True(tool.ProtocolTool.Meta["enabled"]?.GetValue<bool>());
        Assert.False(tool.ProtocolTool.Meta["deprecated"]?.GetValue<bool>());
    }

    [Fact]
    public void McpServerTool_Create_WithAllConstructorTypes_PopulatesToolMeta()
    {
        var method = typeof(TestToolAllTypesMetaClass).GetMethod(nameof(TestToolAllTypesMetaClass.ToolWithAllTypes))!;
        
        var tool = McpServerTool.Create(method, target: null);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal(4, tool.ProtocolTool.Meta.Count);
        
        Assert.Equal("test", tool.ProtocolTool.Meta["stringKey"]?.GetValue<string>());
        Assert.Equal(JsonValueKind.String, tool.ProtocolTool.Meta["stringKey"]?.GetValueKind());
        
        Assert.Equal(42.5, tool.ProtocolTool.Meta["doubleKey"]?.GetValue<double>());
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["doubleKey"]?.GetValueKind());
        
        Assert.True(tool.ProtocolTool.Meta["boolKey"]?.GetValue<bool>());
        Assert.Equal(JsonValueKind.True, tool.ProtocolTool.Meta["boolKey"]?.GetValueKind());
        
        Assert.Null(tool.ProtocolTool.Meta["nullKey"]);
    }

    [Fact]
    public void McpServerPrompt_Create_WithStringMeta_PopulatesPromptMeta()
    {
        var method = typeof(TestPromptStringMetaClass).GetMethod(nameof(TestPromptStringMetaClass.PromptWithStringMeta))!;
        
        var prompt = McpServerPrompt.Create(method, target: null);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal(2, prompt.ProtocolPrompt.Meta.Count);
        Assert.Equal("system", prompt.ProtocolPrompt.Meta["role"]?.GetValue<string>());
        Assert.Equal("instruction", prompt.ProtocolPrompt.Meta["type"]?.GetValue<string>());
    }

    [Fact]
    public void McpServerPrompt_Create_WithDoubleMeta_PopulatesPromptMeta()
    {
        var method = typeof(TestPromptDoubleMetaClass).GetMethod(nameof(TestPromptDoubleMetaClass.PromptWithDoubleMeta))!;
        
        var prompt = McpServerPrompt.Create(method, target: null);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal(2, prompt.ProtocolPrompt.Meta.Count);
        Assert.Equal(0.7, prompt.ProtocolPrompt.Meta["temperature"]?.GetValue<double>());
        Assert.Equal(100.0, prompt.ProtocolPrompt.Meta["maxTokens"]?.GetValue<double>());
    }

    [Fact]
    public void McpServerPrompt_Create_WithBoolMeta_PopulatesPromptMeta()
    {
        var method = typeof(TestPromptBoolMetaClass).GetMethod(nameof(TestPromptBoolMetaClass.PromptWithBoolMeta))!;
        
        var prompt = McpServerPrompt.Create(method, target: null);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Single(prompt.ProtocolPrompt.Meta);
        Assert.True(prompt.ProtocolPrompt.Meta["stream"]?.GetValue<bool>());
    }

    [Fact]
    public void McpServerPrompt_Create_WithAllConstructorTypes_PopulatesPromptMeta()
    {
        var method = typeof(TestPromptAllTypesMetaClass).GetMethod(nameof(TestPromptAllTypesMetaClass.PromptWithAllTypes))!;
        
        var prompt = McpServerPrompt.Create(method, target: null);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal(4, prompt.ProtocolPrompt.Meta.Count);
        Assert.Equal("user", prompt.ProtocolPrompt.Meta["role"]?.GetValue<string>());
        Assert.Equal(1.0, prompt.ProtocolPrompt.Meta["version"]?.GetValue<double>());
        Assert.False(prompt.ProtocolPrompt.Meta["experimental"]?.GetValue<bool>());
        Assert.Null(prompt.ProtocolPrompt.Meta["deprecated"]);
    }

    [Fact]
    public void McpServerResource_Create_WithStringMeta_PopulatesResourceMeta()
    {
        var method = typeof(TestResourceStringMetaClass).GetMethod(nameof(TestResourceStringMetaClass.ResourceWithStringMeta))!;
        
        var resource = McpServerResource.Create(method, target: null);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal(2, resource.ProtocolResourceTemplate.Meta.Count);
        Assert.Equal("text/html", resource.ProtocolResourceTemplate.Meta["contentType"]?.GetValue<string>());
        Assert.Equal("utf-8", resource.ProtocolResourceTemplate.Meta["encoding"]?.GetValue<string>());
    }

    [Fact]
    public void McpServerResource_Create_WithDoubleMeta_PopulatesResourceMeta()
    {
        var method = typeof(TestResourceDoubleMetaClass).GetMethod(nameof(TestResourceDoubleMetaClass.ResourceWithDoubleMeta))!;
        
        var resource = McpServerResource.Create(method, target: null);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal(2, resource.ProtocolResourceTemplate.Meta.Count);
        Assert.Equal(1.5, resource.ProtocolResourceTemplate.Meta["version"]?.GetValue<double>());
        Assert.Equal(3600.0, resource.ProtocolResourceTemplate.Meta["cacheDuration"]?.GetValue<double>());
    }

    [Fact]
    public void McpServerResource_Create_WithBoolMeta_PopulatesResourceMeta()
    {
        var method = typeof(TestResourceBoolMetaClass).GetMethod(nameof(TestResourceBoolMetaClass.ResourceWithBoolMeta))!;
        
        var resource = McpServerResource.Create(method, target: null);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal(2, resource.ProtocolResourceTemplate.Meta.Count);
        Assert.True(resource.ProtocolResourceTemplate.Meta["cacheable"]?.GetValue<bool>());
        Assert.False(resource.ProtocolResourceTemplate.Meta["requiresAuth"]?.GetValue<bool>());
    }

    [Fact]
    public void McpServerResource_Create_WithAllConstructorTypes_PopulatesResourceMeta()
    {
        var method = typeof(TestResourceAllTypesMetaClass).GetMethod(nameof(TestResourceAllTypesMetaClass.ResourceWithAllTypes))!;
        
        var resource = McpServerResource.Create(method, target: null);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal(4, resource.ProtocolResourceTemplate.Meta.Count);
        Assert.Equal("public", resource.ProtocolResourceTemplate.Meta["visibility"]?.GetValue<string>());
        Assert.Equal(2.0, resource.ProtocolResourceTemplate.Meta["apiVersion"]?.GetValue<double>());
        Assert.True(resource.ProtocolResourceTemplate.Meta["available"]?.GetValue<bool>());
        Assert.Null(resource.ProtocolResourceTemplate.Meta["owner"]);
    }

    [Fact]
    public void McpServerTool_Create_WithJsonValueMeta_PopulatesToolMetaWithComplexTypes()
    {
        var method = typeof(TestToolJsonValueMetaClass).GetMethod(nameof(TestToolJsonValueMetaClass.ToolWithJsonValueMeta))!;
        
        var tool = McpServerTool.Create(method, target: null);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        // Verify integer via JsonValue
        Assert.Equal(42, tool.ProtocolTool.Meta["count"]?.GetValue<int>());
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["count"]?.GetValueKind());
        
        // Verify array via JsonValue
        var tags = tool.ProtocolTool.Meta["tags"]?.AsArray();
        Assert.NotNull(tags);
        Assert.Equal(3, tags.Count);
        Assert.Equal("tag1", tags[0]?.GetValue<string>());
        
        // Verify object via JsonValue
        var config = tool.ProtocolTool.Meta["config"]?.AsObject();
        Assert.NotNull(config);
        Assert.Equal("high", config["priority"]?.GetValue<string>());
    }

    [Fact]
    public void McpServerPrompt_Create_WithJsonValueMeta_PopulatesPromptMetaWithComplexTypes()
    {
        var method = typeof(TestPromptJsonValueMetaClass).GetMethod(nameof(TestPromptJsonValueMetaClass.PromptWithJsonValueMeta))!;
        
        var prompt = McpServerPrompt.Create(method, target: null);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        
        var parameters = prompt.ProtocolPrompt.Meta["parameters"]?.AsObject();
        Assert.NotNull(parameters);
        Assert.Equal("string", parameters["type"]?.GetValue<string>());
        Assert.True(parameters["required"]?.GetValue<bool>());
    }

    [Fact]
    public void McpServerResource_Create_WithJsonValueMeta_PopulatesResourceMetaWithComplexTypes()
    {
        var method = typeof(TestResourceJsonValueMetaClass).GetMethod(nameof(TestResourceJsonValueMetaClass.ResourceWithJsonValueMeta))!;
        
        var resource = McpServerResource.Create(method, target: null);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        
        var metadata = resource.ProtocolResourceTemplate.Meta["metadata"]?.AsObject();
        Assert.NotNull(metadata);
        Assert.Equal("document", metadata["type"]?.GetValue<string>());
        
        var permissions = resource.ProtocolResourceTemplate.Meta["permissions"]?.AsArray();
        Assert.NotNull(permissions);
        Assert.Equal(2, permissions.Count);
    }

    #endregion

    #region Options Meta Interaction Tests

    [Fact]
    public void McpServerTool_Create_WithNullOptionsMeta_UsesAttributesOnly()
    {
        var method = typeof(TestToolStringMetaClass).GetMethod(nameof(TestToolStringMetaClass.ToolWithStringMeta))!;
        var options = new McpServerToolCreateOptions { Meta = null };
        
        var tool = McpServerTool.Create(method, target: null, options: options);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal(3, tool.ProtocolTool.Meta.Count);
        Assert.Equal("value1", tool.ProtocolTool.Meta["key1"]?.GetValue<string>());
        Assert.Equal("value2", tool.ProtocolTool.Meta["key2"]?.GetValue<string>());
        Assert.Null(tool.ProtocolTool.Meta["key3"]);
    }

    [Fact]
    public void McpServerTool_Create_WithEmptyOptionsMeta_UsesAttributesOnly()
    {
        var method = typeof(TestToolStringMetaClass).GetMethod(nameof(TestToolStringMetaClass.ToolWithStringMeta))!;
        var options = new McpServerToolCreateOptions { Meta = new JsonObject() };
        
        var tool = McpServerTool.Create(method, target: null, options: options);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal(3, tool.ProtocolTool.Meta.Count);
        Assert.Equal("value1", tool.ProtocolTool.Meta["key1"]?.GetValue<string>());
        Assert.Equal("value2", tool.ProtocolTool.Meta["key2"]?.GetValue<string>());
        Assert.Null(tool.ProtocolTool.Meta["key3"]);
    }

    [Fact]
    public void McpServerTool_Create_WithNonConflictingOptionsMeta_MergesBoth()
    {
        var method = typeof(TestToolStringMetaClass).GetMethod(nameof(TestToolStringMetaClass.ToolWithStringMeta))!;
        var options = new McpServerToolCreateOptions 
        { 
            Meta = new JsonObject 
            { 
                ["newKey1"] = "newValue1",
                ["newKey2"] = 42
            } 
        };
        
        var tool = McpServerTool.Create(method, target: null, options: options);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal(5, tool.ProtocolTool.Meta.Count);
        
        // From attributes
        Assert.Equal("value1", tool.ProtocolTool.Meta["key1"]?.GetValue<string>());
        Assert.Equal("value2", tool.ProtocolTool.Meta["key2"]?.GetValue<string>());
        Assert.Null(tool.ProtocolTool.Meta["key3"]);
        
        // From options
        Assert.Equal("newValue1", tool.ProtocolTool.Meta["newKey1"]?.GetValue<string>());
        Assert.Equal(42, tool.ProtocolTool.Meta["newKey2"]?.GetValue<int>());
    }

    [Fact]
    public void McpServerTool_Create_WithConflictingOptionsMeta_OptionsWin()
    {
        var method = typeof(TestToolStringMetaClass).GetMethod(nameof(TestToolStringMetaClass.ToolWithStringMeta))!;
        var options = new McpServerToolCreateOptions 
        { 
            Meta = new JsonObject 
            { 
                ["key1"] = "overridden",  // Conflicts with attribute
                ["newKey"] = "added"      // Non-conflicting
            } 
        };
        
        var tool = McpServerTool.Create(method, target: null, options: options);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal(4, tool.ProtocolTool.Meta.Count);
        
        // Options take precedence
        Assert.Equal("overridden", tool.ProtocolTool.Meta["key1"]?.GetValue<string>());
        
        // Attributes that don't conflict
        Assert.Equal("value2", tool.ProtocolTool.Meta["key2"]?.GetValue<string>());
        Assert.Null(tool.ProtocolTool.Meta["key3"]);
        
        // From options only
        Assert.Equal("added", tool.ProtocolTool.Meta["newKey"]?.GetValue<string>());
    }

    [Fact]
    public void McpServerPrompt_Create_WithNullOptionsMeta_UsesAttributesOnly()
    {
        var method = typeof(TestPromptStringMetaClass).GetMethod(nameof(TestPromptStringMetaClass.PromptWithStringMeta))!;
        var options = new McpServerPromptCreateOptions { Meta = null };
        
        var prompt = McpServerPrompt.Create(method, target: null, options: options);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal(2, prompt.ProtocolPrompt.Meta.Count);
        Assert.Equal("system", prompt.ProtocolPrompt.Meta["role"]?.GetValue<string>());
        Assert.Equal("instruction", prompt.ProtocolPrompt.Meta["type"]?.GetValue<string>());
    }

    [Fact]
    public void McpServerPrompt_Create_WithEmptyOptionsMeta_UsesAttributesOnly()
    {
        var method = typeof(TestPromptStringMetaClass).GetMethod(nameof(TestPromptStringMetaClass.PromptWithStringMeta))!;
        var options = new McpServerPromptCreateOptions { Meta = new JsonObject() };
        
        var prompt = McpServerPrompt.Create(method, target: null, options: options);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal(2, prompt.ProtocolPrompt.Meta.Count);
        Assert.Equal("system", prompt.ProtocolPrompt.Meta["role"]?.GetValue<string>());
        Assert.Equal("instruction", prompt.ProtocolPrompt.Meta["type"]?.GetValue<string>());
    }

    [Fact]
    public void McpServerPrompt_Create_WithNonConflictingOptionsMeta_MergesBoth()
    {
        var method = typeof(TestPromptStringMetaClass).GetMethod(nameof(TestPromptStringMetaClass.PromptWithStringMeta))!;
        var options = new McpServerPromptCreateOptions 
        { 
            Meta = new JsonObject 
            { 
                ["temperature"] = 0.7,
                ["maxTokens"] = 100.0
            } 
        };
        
        var prompt = McpServerPrompt.Create(method, target: null, options: options);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal(4, prompt.ProtocolPrompt.Meta.Count);
        
        // From attributes
        Assert.Equal("system", prompt.ProtocolPrompt.Meta["role"]?.GetValue<string>());
        Assert.Equal("instruction", prompt.ProtocolPrompt.Meta["type"]?.GetValue<string>());
        
        // From options
        Assert.Equal(0.7, prompt.ProtocolPrompt.Meta["temperature"]?.GetValue<double>());
        Assert.Equal(100.0, prompt.ProtocolPrompt.Meta["maxTokens"]?.GetValue<double>());
    }

    [Fact]
    public void McpServerPrompt_Create_WithConflictingOptionsMeta_OptionsWin()
    {
        var method = typeof(TestPromptStringMetaClass).GetMethod(nameof(TestPromptStringMetaClass.PromptWithStringMeta))!;
        var options = new McpServerPromptCreateOptions 
        { 
            Meta = new JsonObject 
            { 
                ["role"] = "user",       // Conflicts with attribute
                ["priority"] = 5         // Non-conflicting
            } 
        };
        
        var prompt = McpServerPrompt.Create(method, target: null, options: options);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal(3, prompt.ProtocolPrompt.Meta.Count);
        
        // Options take precedence
        Assert.Equal("user", prompt.ProtocolPrompt.Meta["role"]?.GetValue<string>());
        
        // Attributes that don't conflict
        Assert.Equal("instruction", prompt.ProtocolPrompt.Meta["type"]?.GetValue<string>());
        
        // From options only
        Assert.Equal(5, prompt.ProtocolPrompt.Meta["priority"]?.GetValue<int>());
    }

    [Fact]
    public void McpServerResource_Create_WithNullOptionsMeta_UsesAttributesOnly()
    {
        var method = typeof(TestResourceStringMetaClass).GetMethod(nameof(TestResourceStringMetaClass.ResourceWithStringMeta))!;
        var options = new McpServerResourceCreateOptions { Meta = null };
        
        var resource = McpServerResource.Create(method, target: null, options: options);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal(2, resource.ProtocolResourceTemplate.Meta.Count);
        Assert.Equal("text/html", resource.ProtocolResourceTemplate.Meta["contentType"]?.GetValue<string>());
        Assert.Equal("utf-8", resource.ProtocolResourceTemplate.Meta["encoding"]?.GetValue<string>());
    }

    [Fact]
    public void McpServerResource_Create_WithEmptyOptionsMeta_UsesAttributesOnly()
    {
        var method = typeof(TestResourceStringMetaClass).GetMethod(nameof(TestResourceStringMetaClass.ResourceWithStringMeta))!;
        var options = new McpServerResourceCreateOptions { Meta = new JsonObject() };
        
        var resource = McpServerResource.Create(method, target: null, options: options);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal(2, resource.ProtocolResourceTemplate.Meta.Count);
        Assert.Equal("text/html", resource.ProtocolResourceTemplate.Meta["contentType"]?.GetValue<string>());
        Assert.Equal("utf-8", resource.ProtocolResourceTemplate.Meta["encoding"]?.GetValue<string>());
    }

    [Fact]
    public void McpServerResource_Create_WithNonConflictingOptionsMeta_MergesBoth()
    {
        var method = typeof(TestResourceStringMetaClass).GetMethod(nameof(TestResourceStringMetaClass.ResourceWithStringMeta))!;
        var options = new McpServerResourceCreateOptions 
        { 
            Meta = new JsonObject 
            { 
                ["cacheable"] = true,
                ["version"] = 2.0
            } 
        };
        
        var resource = McpServerResource.Create(method, target: null, options: options);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal(4, resource.ProtocolResourceTemplate.Meta.Count);
        
        // From attributes
        Assert.Equal("text/html", resource.ProtocolResourceTemplate.Meta["contentType"]?.GetValue<string>());
        Assert.Equal("utf-8", resource.ProtocolResourceTemplate.Meta["encoding"]?.GetValue<string>());
        
        // From options
        Assert.True(resource.ProtocolResourceTemplate.Meta["cacheable"]?.GetValue<bool>());
        Assert.Equal(2.0, resource.ProtocolResourceTemplate.Meta["version"]?.GetValue<double>());
    }

    [Fact]
    public void McpServerResource_Create_WithConflictingOptionsMeta_OptionsWin()
    {
        var method = typeof(TestResourceStringMetaClass).GetMethod(nameof(TestResourceStringMetaClass.ResourceWithStringMeta))!;
        var options = new McpServerResourceCreateOptions 
        { 
            Meta = new JsonObject 
            { 
                ["encoding"] = "iso-8859-1",  // Conflicts with attribute
                ["size"] = 1024                // Non-conflicting
            } 
        };
        
        var resource = McpServerResource.Create(method, target: null, options: options);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal(3, resource.ProtocolResourceTemplate.Meta.Count);
        
        // Options take precedence
        Assert.Equal("iso-8859-1", resource.ProtocolResourceTemplate.Meta["encoding"]?.GetValue<string>());
        
        // Attributes that don't conflict
        Assert.Equal("text/html", resource.ProtocolResourceTemplate.Meta["contentType"]?.GetValue<string>());
        
        // From options only
        Assert.Equal(1024, resource.ProtocolResourceTemplate.Meta["size"]?.GetValue<int>());
    }

    [Fact]
    public void McpServerTool_Create_WithOptionsMetaOnly_NoAttributes_PopulatesMeta()
    {
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithoutMeta))!;
        var options = new McpServerToolCreateOptions 
        { 
            Meta = new JsonObject 
            { 
                ["optionsKey"] = "optionsValue",
                ["count"] = 10
            } 
        };
        
        var tool = McpServerTool.Create(method, target: null, options: options);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal(2, tool.ProtocolTool.Meta.Count);
        Assert.Equal("optionsValue", tool.ProtocolTool.Meta["optionsKey"]?.GetValue<string>());
        Assert.Equal(10, tool.ProtocolTool.Meta["count"]?.GetValue<int>());
    }

    [Fact]
    public void McpServerPrompt_Create_WithOptionsMetaOnly_NoAttributes_PopulatesMeta()
    {
        var method = typeof(TestPromptNoMetaClass).GetMethod(nameof(TestPromptNoMetaClass.PromptWithoutMeta))!;
        var options = new McpServerPromptCreateOptions 
        { 
            Meta = new JsonObject 
            { 
                ["model"] = "gpt-4",
                ["temperature"] = 0.5
            } 
        };
        
        var prompt = McpServerPrompt.Create(method, target: null, options: options);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal(2, prompt.ProtocolPrompt.Meta.Count);
        Assert.Equal("gpt-4", prompt.ProtocolPrompt.Meta["model"]?.GetValue<string>());
        Assert.Equal(0.5, prompt.ProtocolPrompt.Meta["temperature"]?.GetValue<double>());
    }

    [Fact]
    public void McpServerResource_Create_WithOptionsMetaOnly_NoAttributes_PopulatesMeta()
    {
        var method = typeof(TestResourceNoMetaClass).GetMethod(nameof(TestResourceNoMetaClass.ResourceWithoutMeta))!;
        var options = new McpServerResourceCreateOptions 
        { 
            Meta = new JsonObject 
            { 
                ["format"] = "json",
                ["compressed"] = false
            } 
        };
        
        var resource = McpServerResource.Create(method, target: null, options: options);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal(2, resource.ProtocolResourceTemplate.Meta.Count);
        Assert.Equal("json", resource.ProtocolResourceTemplate.Meta["format"]?.GetValue<string>());
        Assert.False(resource.ProtocolResourceTemplate.Meta["compressed"]?.GetValue<bool>());
    }

    #endregion

    [Fact]
    public void McpMetaAttribute_OnTool_PopulatesMeta()
    {
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithMeta))!;
        
        var tool = McpServerTool.Create(method, target: null);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("gpt-4o", tool.ProtocolTool.Meta["model"]?.ToString());
        Assert.Equal("1.0", tool.ProtocolTool.Meta["version"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_OnPrompt_PopulatesMeta()
    {
        var method = typeof(TestPromptClass).GetMethod(nameof(TestPromptClass.PromptWithMeta))!;
        
        var prompt = McpServerPrompt.Create(method, target: null);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal("reasoning", prompt.ProtocolPrompt.Meta["type"]?.ToString());
        Assert.Equal("claude-3", prompt.ProtocolPrompt.Meta["model"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_OnResource_PopulatesMeta()
    {
        var method = typeof(TestResourceClass).GetMethod(nameof(TestResourceClass.ResourceWithMeta))!;
        
        var resource = McpServerResource.Create(method, target: null);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal("text/plain", resource.ProtocolResourceTemplate.Meta["encoding"]?.ToString());
        Assert.Equal("cached", resource.ProtocolResourceTemplate.Meta["caching"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_WithoutAttributes_ReturnsNull()
    {
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithoutMeta))!;
        
        var tool = McpServerTool.Create(method, target: null);
        
        Assert.Null(tool.ProtocolTool.Meta);
    }

    [Fact]
    public void McpMetaAttribute_SingleAttribute_PopulatesMeta()
    {
        // Arrange
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithSingleMeta))!;
        
        // Act
        var tool = McpServerTool.Create(method, target: null);
        
        // Assert
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("test-value", tool.ProtocolTool.Meta["test-key"]?.ToString());
        Assert.Single(tool.ProtocolTool.Meta);
    }

    [Fact]
    public void McpMetaAttribute_OptionsMetaTakesPrecedence()
    {
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithMeta))!;
        var seedMeta = new JsonObject
        {
            ["model"] = "options-model",
            ["extra"] = "options-extra"
        };
        var options = new McpServerToolCreateOptions { Meta = seedMeta };
        
        var tool = McpServerTool.Create(method, target: null, options: options);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("options-model", tool.ProtocolTool.Meta["model"]?.ToString());
        Assert.Equal("1.0", tool.ProtocolTool.Meta["version"]?.ToString());
        Assert.Equal("options-extra", tool.ProtocolTool.Meta["extra"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_OptionsMetaOnly_NoAttributes()
    {
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithoutMeta))!;
        var seedMeta = new JsonObject
        {
            ["custom"] = "value"
        };
        var options = new McpServerToolCreateOptions { Meta = seedMeta };
        var tool = McpServerTool.Create(method, target: null, options: options);
        
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("value", tool.ProtocolTool.Meta["custom"]?.ToString());
        Assert.Single(tool.ProtocolTool.Meta);
    }

    [Fact]
    public void McpMetaAttribute_PromptOptionsMetaTakesPrecedence()
    {
        var method = typeof(TestPromptClass).GetMethod(nameof(TestPromptClass.PromptWithMeta))!;
        var seedMeta = new JsonObject
        {
            ["type"] = "options-type",
            ["extra"] = "options-extra"
        };
        var options = new McpServerPromptCreateOptions { Meta = seedMeta };
        
        var prompt = McpServerPrompt.Create(method, target: null, options: options);
        
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal("options-type", prompt.ProtocolPrompt.Meta["type"]?.ToString());
        Assert.Equal("claude-3", prompt.ProtocolPrompt.Meta["model"]?.ToString());
        Assert.Equal("options-extra", prompt.ProtocolPrompt.Meta["extra"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_ResourceOptionsMetaTakesPrecedence()
    {
        var method = typeof(TestResourceClass).GetMethod(nameof(TestResourceClass.ResourceWithMeta))!;
        var seedMeta = new JsonObject
        {
            ["encoding"] = "options-encoding",
            ["extra"] = "options-extra"
        };
        var options = new McpServerResourceCreateOptions { Meta = seedMeta };
        
        var resource = McpServerResource.Create(method, target: null, options: options);
        
        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal("options-encoding", resource.ProtocolResourceTemplate.Meta["encoding"]?.ToString());
        Assert.Equal("cached", resource.ProtocolResourceTemplate.Meta["caching"]?.ToString());
        Assert.Equal("options-extra", resource.ProtocolResourceTemplate.Meta["extra"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_ResourceOptionsMetaOnly_NoAttributes()
    {
        var method = typeof(TestResourceNoMetaClass).GetMethod(nameof(TestResourceNoMetaClass.ResourceWithoutMeta))!;
        var seedMeta = new JsonObject { ["only"] = "resource" };
        var options = new McpServerResourceCreateOptions { Meta = seedMeta };

        var resource = McpServerResource.Create(method, target: null, options: options);

        Assert.NotNull(resource.ProtocolResourceTemplate?.Meta);
        Assert.Equal("resource", resource.ProtocolResourceTemplate.Meta["only"]?.ToString());
        Assert.Single(resource.ProtocolResourceTemplate.Meta!);
    }

    [Fact]
    public void McpMetaAttribute_PromptWithoutMeta_ReturnsNull()
    {
        var method = typeof(TestPromptNoMetaClass).GetMethod(nameof(TestPromptNoMetaClass.PromptWithoutMeta))!;
        var prompt = McpServerPrompt.Create(method, target: null);
        Assert.Null(prompt.ProtocolPrompt.Meta);
    }

    [Fact]
    public void McpMetaAttribute_DuplicateKeys_IgnoresLaterAttributes()
    {
        var method = typeof(TestToolDuplicateMetaClass).GetMethod(nameof(TestToolDuplicateMetaClass.ToolWithDuplicateMeta))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        // "key" first attribute value should remain, second ignored
        Assert.Equal("first", tool.ProtocolTool.Meta["key"]?.ToString());
        // Ensure only two keys (key and other)
        Assert.Equal(2, tool.ProtocolTool.Meta!.Count);
        Assert.Equal("other-value", tool.ProtocolTool.Meta["other"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_DuplicateKeys_WithSeedMeta_SeedTakesPrecedence()
    {
        var method = typeof(TestToolDuplicateMetaClass).GetMethod(nameof(TestToolDuplicateMetaClass.ToolWithDuplicateMeta))!;
        var seedMeta = new JsonObject { ["key"] = "seed" };
        var options = new McpServerToolCreateOptions { Meta = seedMeta };
        var tool = McpServerTool.Create(method, target: null, options: options);
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("seed", tool.ProtocolTool.Meta["key"]?.ToString());
        Assert.Equal("other-value", tool.ProtocolTool.Meta["other"]?.ToString());
        Assert.Equal(2, tool.ProtocolTool.Meta!.Count);
    }

    [Fact]
    public void McpMetaAttribute_NullValue_SerializedAsNull()
    {
        var method = typeof(TestToolNullMetaClass).GetMethod(nameof(TestToolNullMetaClass.ToolWithNullMeta))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.True(tool.ProtocolTool.Meta.ContainsKey("nullable"));
        Assert.Null(tool.ProtocolTool.Meta["nullable"]);
    }

    [Fact]
    public void McpMetaAttribute_ClassLevelAttributesIgnored()
    {
        // Since McpMetaAttribute is only valid on methods, class-level attributes are not supported.
        // This test simply validates method-level attributes still function as expected.
        var method = typeof(TestToolMethodMetaOnlyClass).GetMethod(nameof(TestToolMethodMetaOnlyClass.ToolWithMethodMeta))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("method", tool.ProtocolTool.Meta["methodKey"]?.ToString());
        // Ensure only the method-level key exists
        Assert.Single(tool.ProtocolTool.Meta!);
    }

    [Fact]
    public void McpMetaAttribute_DelegateOverload_PopulatesMeta()
    {
        // Create tool using delegate overload instead of MethodInfo directly
        var del = new Func<string, string>(TestToolClass.ToolWithMeta);
        var tool = McpServerTool.Create(del);
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("gpt-4o", tool.ProtocolTool.Meta!["model"]?.ToString());
        Assert.Equal("1.0", tool.ProtocolTool.Meta["version"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_ComplexObject_SerializedAsJson()
    {
        var method = typeof(TestToolComplexMetaClass).GetMethod(nameof(TestToolComplexMetaClass.ToolWithComplexMeta))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        var configNode = tool.ProtocolTool.Meta["config"];
        Assert.NotNull(configNode);
        Assert.Equal(JsonValueKind.Object, configNode.GetValueKind());
        
        var configObj = configNode.AsObject();
        Assert.Equal("high", configObj["relevance"]?.ToString());
        Assert.Equal("noble", configObj["purpose"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_Array_SerializedAsJson()
    {
        var method = typeof(TestToolArrayMetaClass).GetMethod(nameof(TestToolArrayMetaClass.ToolWithArrayMeta))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        var tagsNode = tool.ProtocolTool.Meta["tags"];
        Assert.NotNull(tagsNode);
        Assert.Equal(JsonValueKind.Array, tagsNode.GetValueKind());
        
        var tagsArray = tagsNode.AsArray();
        Assert.Equal(3, tagsArray.Count);
        Assert.Equal("tag1", tagsArray[0]?.ToString());
        Assert.Equal("tag2", tagsArray[1]?.ToString());
        Assert.Equal("tag3", tagsArray[2]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_JsonValueOverride_UsesProvidedJson()
    {
        var method = typeof(TestToolJsonValueOverrideClass).GetMethod(nameof(TestToolJsonValueOverrideClass.ToolWithJsonValueOverride))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        var configNode = tool.ProtocolTool.Meta["config"];
        Assert.NotNull(configNode);
        Assert.Equal(JsonValueKind.Object, configNode.GetValueKind());
        
        var configObj = configNode.AsObject();
        Assert.Equal("custom", configObj["type"]?.ToString());
        Assert.Equal("123", configObj["value"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_MixedTypes_AllSerializedCorrectly()
    {
        var method = typeof(TestToolMixedTypesClass).GetMethod(nameof(TestToolMixedTypesClass.ToolWithMixedTypes))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        Assert.Equal("text", tool.ProtocolTool.Meta["stringValue"]?.ToString());
        Assert.Equal(JsonValueKind.String, tool.ProtocolTool.Meta["stringValue"]?.GetValueKind());
        
        Assert.Equal("42", tool.ProtocolTool.Meta["numberValue"]?.ToString());
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["numberValue"]?.GetValueKind());
        
        Assert.Equal("true", tool.ProtocolTool.Meta["boolValue"]?.ToString());
        Assert.Equal(JsonValueKind.True, tool.ProtocolTool.Meta["boolValue"]?.GetValueKind());
        
        Assert.Null(tool.ProtocolTool.Meta["nullValue"]);
        
        var objNode = tool.ProtocolTool.Meta["objectValue"];
        Assert.NotNull(objNode);
        Assert.Equal(JsonValueKind.Object, objNode.GetValueKind());
    }

    [Fact]
    public void McpMetaAttribute_DoubleValue_SerializedAsNumber()
    {
        var method = typeof(TestToolDoubleMetaClass).GetMethod(nameof(TestToolDoubleMetaClass.ToolWithDoubleMeta))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        var piNode = tool.ProtocolTool.Meta["pi"];
        Assert.NotNull(piNode);
        Assert.Equal(JsonValueKind.Number, piNode.GetValueKind());
        var piValue = piNode.GetValue<double>();
        Assert.True(Math.Abs(3.14159 - piValue) < 0.00001);
    }

    [Fact]
    public void McpMetaAttribute_SupportedTypes_SerializedCorrectly()
    {
        var method = typeof(TestToolSupportedTypesClass).GetMethod(nameof(TestToolSupportedTypesClass.ToolWithSupportedTypes))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        // string
        Assert.Equal("hello world", tool.ProtocolTool.Meta["stringValue"]?.ToString());
        Assert.Equal(JsonValueKind.String, tool.ProtocolTool.Meta["stringValue"]?.GetValueKind());
        
        // double (positive)
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["doubleValue"]?.GetValueKind());
        var doubleValue = tool.ProtocolTool.Meta["doubleValue"]?.GetValue<double>();
        Assert.NotNull(doubleValue);
        Assert.True(Math.Abs(2.71828 - doubleValue.Value) < 0.00001);
        
        // double (negative)
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["negativeDouble"]?.GetValueKind());
        Assert.Equal(-1.5, tool.ProtocolTool.Meta["negativeDouble"]?.GetValue<double>());
        
        // double (zero)
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["zeroDouble"]?.GetValueKind());
        Assert.Equal(0.0, tool.ProtocolTool.Meta["zeroDouble"]?.GetValue<double>());
        
        // double (integer value)
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["intAsDouble"]?.GetValueKind());
        Assert.Equal(42.0, tool.ProtocolTool.Meta["intAsDouble"]?.GetValue<double>());
        
        // bool true
        Assert.Equal("true", tool.ProtocolTool.Meta["boolTrueValue"]?.ToString());
        Assert.Equal(JsonValueKind.True, tool.ProtocolTool.Meta["boolTrueValue"]?.GetValueKind());
        
        // bool false
        Assert.Equal("false", tool.ProtocolTool.Meta["boolFalseValue"]?.ToString());
        Assert.Equal(JsonValueKind.False, tool.ProtocolTool.Meta["boolFalseValue"]?.GetValueKind());
    }

    [Fact]
    public void McpMetaAttribute_StringEdgeCases_SerializedCorrectly()
    {
        var method = typeof(TestToolStringEdgeCasesClass).GetMethod(nameof(TestToolStringEdgeCasesClass.ToolWithStringEdgeCases))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        // Empty string
        Assert.Equal("", tool.ProtocolTool.Meta["emptyString"]?.ToString());
        Assert.Equal(JsonValueKind.String, tool.ProtocolTool.Meta["emptyString"]?.GetValueKind());
        
        // String with special characters
        Assert.Equal("Line1\nLine2\tTabbed", tool.ProtocolTool.Meta["specialChars"]?.ToString());
        
        // String with quotes
        Assert.Equal("He said \"Hello\"", tool.ProtocolTool.Meta["withQuotes"]?.ToString());
        
        // Unicode string
        Assert.Equal("Hello 世界 🌍", tool.ProtocolTool.Meta["unicode"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_DoubleEdgeCases_SerializedCorrectly()
    {
        var method = typeof(TestToolDoubleEdgeCasesClass).GetMethod(nameof(TestToolDoubleEdgeCasesClass.ToolWithDoubleEdgeCases))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        // Zero
        Assert.Equal("0", tool.ProtocolTool.Meta["zero"]?.ToString());
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["zero"]?.GetValueKind());
        
        // Negative value
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["negative"]?.GetValueKind());
        Assert.Equal(-999.999, tool.ProtocolTool.Meta["negative"]?.GetValue<double>());
        
        // Large positive value
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["largePositive"]?.GetValueKind());
        Assert.Equal(1.7976931348623157E+308, tool.ProtocolTool.Meta["largePositive"]?.GetValue<double>());
        
        // Small positive value
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["smallPositive"]?.GetValueKind());
        var smallValue = tool.ProtocolTool.Meta["smallPositive"]?.GetValue<double>();
        Assert.NotNull(smallValue);
        Assert.True(Math.Abs(0.000001 - smallValue.Value) < 0.0000001);
    }

    [Fact]
    public void McpMetaAttribute_JsonValueForComplexTypes_SerializedCorrectly()
    {
        var method = typeof(TestToolJsonValueComplexClass).GetMethod(nameof(TestToolJsonValueComplexClass.ToolWithComplexTypes))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        
        // Integer via JsonValue
        Assert.Equal("42", tool.ProtocolTool.Meta["intValue"]?.ToString());
        Assert.Equal(JsonValueKind.Number, tool.ProtocolTool.Meta["intValue"]?.GetValueKind());
        Assert.Equal(42, tool.ProtocolTool.Meta["intValue"]?.GetValue<int>());
        
        // Array via JsonValue
        var arrayNode = tool.ProtocolTool.Meta["arrayValue"];
        Assert.NotNull(arrayNode);
        Assert.Equal(JsonValueKind.Array, arrayNode.GetValueKind());
        var array = arrayNode.AsArray();
        Assert.Equal(3, array.Count);
        Assert.Equal("a", array[0]?.ToString());
        Assert.Equal("b", array[1]?.ToString());
        Assert.Equal("c", array[2]?.ToString());
        
        // Object via JsonValue
        var objNode = tool.ProtocolTool.Meta["objectValue"];
        Assert.NotNull(objNode);
        Assert.Equal(JsonValueKind.Object, objNode.GetValueKind());
        var obj = objNode.AsObject();
        Assert.Equal("value", obj["key"]?.ToString());
        Assert.Equal("123", obj["num"]?.ToString());
    }

    private class TestToolClass
    {
        [McpServerTool]
        [McpMeta("model", "gpt-4o")]
        [McpMeta("version", "1.0")]
        public static string ToolWithMeta(string input)
        {
            return input;
        }

        [McpServerTool]
        public static string ToolWithoutMeta(string input)
        {
            return input;
        }

        [McpServerTool]
        [McpMeta("test-key", "test-value")]
        public static string ToolWithSingleMeta(string input)
        {
            return input;
        }
    }

    private class TestPromptClass
    {
        [McpServerPrompt]
        [McpMeta("type", "reasoning")]
        [McpMeta("model", "claude-3")]
        public static string PromptWithMeta(string input)
        {
            return input;
        }
    }

    private class TestResourceClass
    {
        [McpServerResource(UriTemplate = "resource://test/{id}")]
        [McpMeta("encoding", "text/plain")]
        [McpMeta("caching", "cached")]
        public static string ResourceWithMeta(string id)
        {
            return $"Resource content for {id}";
        }
    }

    private class TestResourceNoMetaClass
    {
        [McpServerResource(UriTemplate = "resource://test2/{id}")]
        public static string ResourceWithoutMeta(string id) => id;
    }

    private class TestPromptNoMetaClass
    {
        [McpServerPrompt]
        public static string PromptWithoutMeta(string input) => input;
    }

    private class TestToolDuplicateMetaClass
    {
        [McpServerTool]
        [McpMeta("key", "first")]
        [McpMeta("key", "second")]
        [McpMeta("other", "other-value")]
        public static string ToolWithDuplicateMeta(string input) => input;
    }

    private class TestToolNullMetaClass
    {
        [McpServerTool]
        [McpMeta("nullable", null)]
        public static string ToolWithNullMeta(string input) => input;
    }

    private class TestToolMethodMetaOnlyClass
    {
        [McpServerTool]
        [McpMeta("methodKey", "method")]
        public static string ToolWithMethodMeta(string input) => input;
    }

    private class TestToolComplexMetaClass
    {
        [McpServerTool]
        [McpMeta("config", JsonValue = """{"relevance": "high", "purpose": "noble"}""")]
        public static string ToolWithComplexMeta(string input) => input;
    }

    private class TestToolArrayMetaClass
    {
        [McpServerTool]
        [McpMeta("tags", JsonValue = """["tag1", "tag2", "tag3"]""")]
        public static string ToolWithArrayMeta(string input) => input;
    }

    private class TestToolJsonValueOverrideClass
    {
        [McpServerTool]
        [McpMeta("config", JsonValue = """{"type": "custom", "value": 123}""")]
        public static string ToolWithJsonValueOverride(string input) => input;
    }

    private class TestToolMixedTypesClass
    {
        [McpServerTool]
        [McpMeta("stringValue", "text")]
        [McpMeta("numberValue", 42.0)]
        [McpMeta("boolValue", true)]
        [McpMeta("nullValue", null)]
        [McpMeta("objectValue", JsonValue = """{"key": "value"}""")]
        public static string ToolWithMixedTypes(string input) => input;
    }

    private class TestToolDoubleMetaClass
    {
        [McpServerTool]
        [McpMeta("pi", 3.14159)]
        public static string ToolWithDoubleMeta(string input) => input;
    }

    private class TestToolSupportedTypesClass
    {
        [McpServerTool]
        [McpMeta("stringValue", "hello world")]
        [McpMeta("doubleValue", 2.71828)]
        [McpMeta("negativeDouble", -1.5)]
        [McpMeta("zeroDouble", 0.0)]
        [McpMeta("intAsDouble", 42.0)]
        [McpMeta("boolTrueValue", true)]
        [McpMeta("boolFalseValue", false)]
        public static string ToolWithSupportedTypes(string input) => input;
    }

    private class TestToolStringEdgeCasesClass
    {
        [McpServerTool]
        [McpMeta("emptyString", "")]
        [McpMeta("specialChars", "Line1\nLine2\tTabbed")]
        [McpMeta("withQuotes", "He said \"Hello\"")]
        [McpMeta("unicode", "Hello 世界 🌍")]
        public static string ToolWithStringEdgeCases(string input) => input;
    }

    private class TestToolDoubleEdgeCasesClass
    {
        [McpServerTool]
        [McpMeta("zero", 0.0)]
        [McpMeta("negative", -999.999)]
        [McpMeta("largePositive", double.MaxValue)]
        [McpMeta("smallPositive", 0.000001)]
        public static string ToolWithDoubleEdgeCases(string input) => input;
    }

    private class TestToolJsonValueComplexClass
    {
        [McpServerTool]
        [McpMeta("intValue", JsonValue = "42")]
        [McpMeta("arrayValue", JsonValue = """["a", "b", "c"]""")]
        [McpMeta("objectValue", JsonValue = """{"key": "value", "num": 123}""")]
        public static string ToolWithComplexTypes(string input) => input;
    }

    // Test classes for Meta Property Population Tests
    private class TestToolStringMetaClass
    {
        [McpServerTool]
        [McpMeta("key1", "value1")]
        [McpMeta("key2", "value2")]
        [McpMeta("key3", null)]
        public static string ToolWithStringMeta(string input) => input;
    }

    private class TestToolDoubleMetaClass2
    {
        [McpServerTool]
        [McpMeta("pi", 3.14)]
        [McpMeta("zero", 0.0)]
        [McpMeta("negative", -1.5)]
        public static string ToolWithDoubleMeta(string input) => input;
    }

    private class TestToolBoolMetaClass
    {
        [McpServerTool]
        [McpMeta("enabled", true)]
        [McpMeta("deprecated", false)]
        public static string ToolWithBoolMeta(string input) => input;
    }

    private class TestToolAllTypesMetaClass
    {
        [McpServerTool]
        [McpMeta("stringKey", "test")]
        [McpMeta("doubleKey", 42.5)]
        [McpMeta("boolKey", true)]
        [McpMeta("nullKey", null)]
        public static string ToolWithAllTypes(string input) => input;
    }

    private class TestPromptStringMetaClass
    {
        [McpServerPrompt]
        [McpMeta("role", "system")]
        [McpMeta("type", "instruction")]
        public static string PromptWithStringMeta(string input) => input;
    }

    private class TestPromptDoubleMetaClass
    {
        [McpServerPrompt]
        [McpMeta("temperature", 0.7)]
        [McpMeta("maxTokens", 100.0)]
        public static string PromptWithDoubleMeta(string input) => input;
    }

    private class TestPromptBoolMetaClass
    {
        [McpServerPrompt]
        [McpMeta("stream", true)]
        public static string PromptWithBoolMeta(string input) => input;
    }

    private class TestPromptAllTypesMetaClass
    {
        [McpServerPrompt]
        [McpMeta("role", "user")]
        [McpMeta("version", 1.0)]
        [McpMeta("experimental", false)]
        [McpMeta("deprecated", null)]
        public static string PromptWithAllTypes(string input) => input;
    }

    private class TestResourceStringMetaClass
    {
        [McpServerResource(UriTemplate = "resource://test/{id}")]
        [McpMeta("contentType", "text/html")]
        [McpMeta("encoding", "utf-8")]
        public static string ResourceWithStringMeta(string id) => id;
    }

    private class TestResourceDoubleMetaClass
    {
        [McpServerResource(UriTemplate = "resource://test/{id}")]
        [McpMeta("version", 1.5)]
        [McpMeta("cacheDuration", 3600.0)]
        public static string ResourceWithDoubleMeta(string id) => id;
    }

    private class TestResourceBoolMetaClass
    {
        [McpServerResource(UriTemplate = "resource://test/{id}")]
        [McpMeta("cacheable", true)]
        [McpMeta("requiresAuth", false)]
        public static string ResourceWithBoolMeta(string id) => id;
    }

    private class TestResourceAllTypesMetaClass
    {
        [McpServerResource(UriTemplate = "resource://test/{id}")]
        [McpMeta("visibility", "public")]
        [McpMeta("apiVersion", 2.0)]
        [McpMeta("available", true)]
        [McpMeta("owner", null)]
        public static string ResourceWithAllTypes(string id) => id;
    }

    private class TestToolJsonValueMetaClass
    {
        [McpServerTool]
        [McpMeta("count", JsonValue = "42")]
        [McpMeta("tags", JsonValue = """["tag1", "tag2", "tag3"]""")]
        [McpMeta("config", JsonValue = """{"priority": "high"}""")]
        public static string ToolWithJsonValueMeta(string input) => input;
    }

    private class TestPromptJsonValueMetaClass
    {
        [McpServerPrompt]
        [McpMeta("parameters", JsonValue = """{"type": "string", "required": true}""")]
        public static string PromptWithJsonValueMeta(string input) => input;
    }

    private class TestResourceJsonValueMetaClass
    {
        [McpServerResource(UriTemplate = "resource://test/{id}")]
        [McpMeta("metadata", JsonValue = """{"type": "document"}""")]
        [McpMeta("permissions", JsonValue = """["read", "write"]""")]
        public static string ResourceWithJsonValueMeta(string id) => id;
    }
}
