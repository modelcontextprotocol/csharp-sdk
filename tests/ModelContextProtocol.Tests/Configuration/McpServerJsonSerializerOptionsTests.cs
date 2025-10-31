using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerJsonSerializerOptionsTests
{
    [Fact]
    public void McpServerOptions_JsonSerializerOptions_DefaultsToNull()
    {
        // Arrange & Act
        var options = new McpServerOptions();

        // Assert
        Assert.Null(options.JsonSerializerOptions);
    }

    [Fact]
    public void McpServerOptions_JsonSerializerOptions_CanBeSet()
    {
        // Arrange
        var customOptions = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        var options = new McpServerOptions();

        // Act
        options.JsonSerializerOptions = customOptions;

        // Assert
        Assert.NotNull(options.JsonSerializerOptions);
        Assert.Equal(JsonNumberHandling.AllowNamedFloatingPointLiterals, options.JsonSerializerOptions.NumberHandling);
    }

    [Fact]
    public void WithTools_UsesServerWideOptions_WhenNoExplicitOptionsProvided()
    {
        // Arrange
        var services = new ServiceCollection();
        var customOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var builder = services.AddMcpServer(options =>
        {
            options.JsonSerializerOptions = customOptions;
        });

        // Act - WithTools should pick up the server-wide options with snake_case naming policy
        builder.WithTools<TestTools>();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify the tool schema uses snake_case property naming
        var tools = serviceProvider.GetServices<McpServerTool>().ToList();
        Assert.Single(tools);
        
        var tool = tools[0];
        Assert.Equal("ToolWithParameters", tool.ProtocolTool.Name);
        
        // Check that the input schema uses snake_case for property names
        var inputSchema = tool.ProtocolTool.InputSchema;
        
        // The schema should have a "properties" object with snake_case property names
        var propertiesElement = inputSchema.GetProperty("properties");
        Assert.True(propertiesElement.TryGetProperty("my_parameter", out _), "Schema should have 'my_parameter' property (snake_case)");
        Assert.False(propertiesElement.TryGetProperty("MyParameter", out _), "Schema should not have 'MyParameter' property (PascalCase)");
    }

    [Fact]
    public void WithPrompts_UsesServerWideOptions_WhenNoExplicitOptionsProvided()
    {
        // Arrange
        var services = new ServiceCollection();
        var customOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var builder = services.AddMcpServer(options =>
        {
            options.JsonSerializerOptions = customOptions;
        });

        // Act - WithPrompts should pick up the server-wide options with snake_case naming policy
        builder.WithPrompts<TestPrompts>();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify the prompt schema uses snake_case property naming
        var prompts = serviceProvider.GetServices<McpServerPrompt>().ToList();
        Assert.Single(prompts);
        
        var prompt = prompts[0];
        Assert.Equal("PromptWithParameters", prompt.ProtocolPrompt.Name);
        
        // Check that the arguments schema uses snake_case for property names
        var arguments = prompt.ProtocolPrompt.Arguments;
        Assert.NotNull(arguments);
        Assert.Single(arguments);
        Assert.Equal("my_argument", arguments[0].Name);
    }

    [Fact]
    public void WithResources_UsesServerWideOptions_WhenNoExplicitOptionsProvided()
    {
        // Arrange
        var services = new ServiceCollection();
        var customOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var builder = services.AddMcpServer(options =>
        {
            options.JsonSerializerOptions = customOptions;
        });

        // Act - WithResources should pick up the server-wide options with snake_case naming policy
        builder.WithResources<TestResources>();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify the resource was registered (resources don't expose schema in the same way)
        var resources = serviceProvider.GetServices<McpServerResource>().ToList();
        Assert.Single(resources);
        
        var resource = resources[0];
        Assert.Equal("resource://test/{myParameter}", resource.ProtocolResourceTemplate.UriTemplate);
    }

    [McpServerToolType]
    private class TestTools
    {
        [McpServerTool]
        public static string ToolWithParameters(string myParameter) => myParameter;
    }

    [McpServerPromptType]
    private class TestPrompts
    {
        [McpServerPrompt]
        public static ChatMessage PromptWithParameters(string myArgument) => 
            new(ChatRole.User, $"Prompt with: {myArgument}");
    }

    [McpServerResourceType]
    private class TestResources
    {
        [McpServerResource(UriTemplate = "resource://test/{myParameter}")]
        public static string ResourceWithParameters(string myParameter) => 
            $"Resource content: {myParameter}";
    }
}
