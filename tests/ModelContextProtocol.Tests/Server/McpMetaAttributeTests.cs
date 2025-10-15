using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public partial class McpMetaAttributeTests
{
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
    public void McpMetaAttribute_NonStringValues_Serialized()
    {
        var method = typeof(TestToolNonStringMetaClass).GetMethod(nameof(TestToolNonStringMetaClass.ToolWithNonStringMeta))!;
        var tool = McpServerTool.Create(method, target: null);
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("42", tool.ProtocolTool.Meta["intValue"]?.ToString());
        Assert.Equal("True", tool.ProtocolTool.Meta["boolValue"]?.ToString());
        Assert.Equal("1", tool.ProtocolTool.Meta["enumValue"]?.ToString());
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

    private class TestToolClass
    {
        [McpServerTool]
        [McpMeta("model", "\"gpt-4o\"")]
        [McpMeta("version", "\"1.0\"")]
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
        [McpMeta("test-key", "\"test-value\"")]
        public static string ToolWithSingleMeta(string input)
        {
            return input;
        }
    }

    private class TestPromptClass
    {
        [McpServerPrompt]
        [McpMeta("type", "\"reasoning\"")]
        [McpMeta("model", "\"claude-3\"")]
        public static string PromptWithMeta(string input)
        {
            return input;
        }
    }

    private class TestResourceClass
    {
        [McpServerResource(UriTemplate = "resource://test/{id}")]
        [McpMeta("encoding", "\"text/plain\"")]
        [McpMeta("caching", "\"cached\"")]
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
        [McpMeta("key", "\"first\"")]
        [McpMeta("key", "\"second\"")]
        [McpMeta("other", "\"other-value\"")]
        public static string ToolWithDuplicateMeta(string input) => input;
    }

    private class TestToolNonStringMetaClass
    {
        [McpServerTool]
        [McpMeta("intValue", "42")]
        [McpMeta("boolValue", "true")]
        [McpMeta("enumValue", "1")]
        public static string ToolWithNonStringMeta(string input) => input;
    }

    private class TestToolNullMetaClass
    {
        [McpServerTool]
        [McpMeta("nullable", "null")]
        public static string ToolWithNullMeta(string input) => input;
    }

    private class TestToolMethodMetaOnlyClass
    {
        [McpServerTool]
        [McpMeta("methodKey", "\"method\"")]
        public static string ToolWithMethodMeta(string input) => input;
    }
}
