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

    private class TestToolClass
    {
        [McpServerTool]
        [McpMeta(Name = "model", Value = "gpt-4o")]
        [McpMeta(Name = "version", Value = "1.0")]
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
        [McpMeta(Name = "test-key", Value = "test-value")]
        public static string ToolWithSingleMeta(string input)
        {
            return input;
        }
    }

    private class TestPromptClass
    {
        [McpServerPrompt]
        [McpMeta(Name = "type", Value = "reasoning")]
        [McpMeta(Name = "model", Value = "claude-3")]
        public static string PromptWithMeta(string input)
        {
            return input;
        }
    }

    private class TestResourceClass
    {
        [McpServerResource(UriTemplate = "resource://test/{id}")]
        [McpMeta(Name = "encoding", Value = "text/plain")]
        [McpMeta(Name = "caching", Value = "cached")]
        public static string ResourceWithMeta(string id)
        {
            return $"Resource content for {id}";
        }
    }
}
