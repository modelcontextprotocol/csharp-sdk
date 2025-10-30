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

        services.Configure<McpServerOptions>(options =>
        {
            options.JsonSerializerOptions = customOptions;
        });

        var builder = services.AddMcpServer();

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

    [McpServerToolType]
    private class TestTools
    {
        [McpServerTool]
        public static string ToolWithParameters(string myParameter) => myParameter;
    }
}
