using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpMetaAttributeTests
{
    [Fact]
    public void McpMetaAttribute_OnTool_PopulatesMeta()
    {
        // Arrange
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithMeta))!;
        
        // Act
        var tool = McpServerTool.Create(method, null);
        
        // Assert
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("gpt-4o", tool.ProtocolTool.Meta["model"]?.ToString());
        Assert.Equal("1.0", tool.ProtocolTool.Meta["version"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_OnPrompt_PopulatesMeta()
    {
        // Arrange
        var method = typeof(TestPromptClass).GetMethod(nameof(TestPromptClass.PromptWithMeta))!;
        
        // Act
        var prompt = McpServerPrompt.Create(method, null);
        
        // Assert
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        Assert.Equal("reasoning", prompt.ProtocolPrompt.Meta["type"]?.ToString());
        Assert.Equal("claude-3", prompt.ProtocolPrompt.Meta["model"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_OnResource_PopulatesMeta()
    {
        // Arrange
        var method = typeof(TestResourceClass).GetMethod(nameof(TestResourceClass.ResourceWithMeta))!;
        
        // Act
        var resource = McpServerResource.Create(method, null);
        
        // Assert
        Assert.NotNull(resource.ProtocolResource.Meta);
        Assert.Equal("text/plain", resource.ProtocolResource.Meta["encoding"]?.ToString());
        Assert.Equal("cached", resource.ProtocolResource.Meta["caching"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_WithoutAttributes_ReturnsNull()
    {
        // Arrange
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithoutMeta))!;
        
        // Act
        var tool = McpServerTool.Create(method, null);
        
        // Assert
        Assert.Null(tool.ProtocolTool.Meta);
    }

    [Fact]
    public void McpMetaAttribute_SingleAttribute_PopulatesMeta()
    {
        // Arrange
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithSingleMeta))!;
        
        // Act
        var tool = McpServerTool.Create(method, null);
        
        // Assert
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("test-value", tool.ProtocolTool.Meta["test-key"]?.ToString());
        Assert.Single(tool.ProtocolTool.Meta);
    }

    [Fact]
    public void McpMetaAttribute_OptionsMetaTakesPrecedence()
    {
        // Arrange
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithMeta))!;
        var seedMeta = new JsonObject
        {
            ["model"] = "options-model",
            ["extra"] = "options-extra"
        };
        var options = new McpServerToolCreateOptions { Meta = seedMeta };
        
        // Act
        var tool = McpServerTool.Create(method, options);
        
        // Assert
        Assert.NotNull(tool.ProtocolTool.Meta);
        // Options Meta should win for "model"
        Assert.Equal("options-model", tool.ProtocolTool.Meta["model"]?.ToString());
        // Attribute should add "version" since it's not in options
        Assert.Equal("1.0", tool.ProtocolTool.Meta["version"]?.ToString());
        // Options Meta should include "extra"
        Assert.Equal("options-extra", tool.ProtocolTool.Meta["extra"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_OptionsMetaOnly_NoAttributes()
    {
        // Arrange
        var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ToolWithoutMeta))!;
        var seedMeta = new JsonObject
        {
            ["custom"] = "value"
        };
        var options = new McpServerToolCreateOptions { Meta = seedMeta };
        
        // Act
        var tool = McpServerTool.Create(method, options);
        
        // Assert
        Assert.NotNull(tool.ProtocolTool.Meta);
        Assert.Equal("value", tool.ProtocolTool.Meta["custom"]?.ToString());
        Assert.Single(tool.ProtocolTool.Meta);
    }

    [Fact]
    public void McpMetaAttribute_PromptOptionsMetaTakesPrecedence()
    {
        // Arrange
        var method = typeof(TestPromptClass).GetMethod(nameof(TestPromptClass.PromptWithMeta))!;
        var seedMeta = new JsonObject
        {
            ["type"] = "options-type",
            ["extra"] = "options-extra"
        };
        var options = new McpServerPromptCreateOptions { Meta = seedMeta };
        
        // Act
        var prompt = McpServerPrompt.Create(method, options);
        
        // Assert
        Assert.NotNull(prompt.ProtocolPrompt.Meta);
        // Options Meta should win for "type"
        Assert.Equal("options-type", prompt.ProtocolPrompt.Meta["type"]?.ToString());
        // Attribute should add "model" since it's not in options
        Assert.Equal("claude-3", prompt.ProtocolPrompt.Meta["model"]?.ToString());
        // Options Meta should include "extra"
        Assert.Equal("options-extra", prompt.ProtocolPrompt.Meta["extra"]?.ToString());
    }

    [Fact]
    public void McpMetaAttribute_ResourceOptionsMetaTakesPrecedence()
    {
        // Arrange
        var method = typeof(TestResourceClass).GetMethod(nameof(TestResourceClass.ResourceWithMeta))!;
        var seedMeta = new JsonObject
        {
            ["encoding"] = "options-encoding",
            ["extra"] = "options-extra"
        };
        var options = new McpServerResourceCreateOptions { Meta = seedMeta };
        
        // Act
        var resource = McpServerResource.Create(method, options);
        
        // Assert
        Assert.NotNull(resource.ProtocolResource.Meta);
        // Options Meta should win for "encoding"
        Assert.Equal("options-encoding", resource.ProtocolResource.Meta["encoding"]?.ToString());
        // Attribute should add "caching" since it's not in options
        Assert.Equal("cached", resource.ProtocolResource.Meta["caching"]?.ToString());
        // Options Meta should include "extra"
        Assert.Equal("options-extra", resource.ProtocolResource.Meta["extra"]?.ToString());
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
}
