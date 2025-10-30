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
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        services.Configure<McpServerOptions>(options =>
        {
            options.JsonSerializerOptions = customOptions;
        });

        var builder = services.AddMcpServer();

        // Act - WithTools should pick up the server-wide options
        builder.WithTools<TestTools>();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify the tool was registered
        var tools = serviceProvider.GetServices<McpServerTool>().ToList();
        Assert.Single(tools);
        Assert.Equal("TestTool", tools[0].ProtocolTool.Name);
    }

    [McpServerToolType]
    private class TestTools
    {
        [McpServerTool]
        public static string TestTool() => "test";
    }
}
