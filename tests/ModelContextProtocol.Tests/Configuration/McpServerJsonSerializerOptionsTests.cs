using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerJsonSerializerOptionsTests : ClientServerTestBase
{
    public McpServerJsonSerializerOptionsTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    private class SpecialNumbers
    {
        public double PositiveInfinity { get; set; }
        public double NegativeInfinity { get; set; }
        public double NotANumber { get; set; }
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Configure server-wide JsonSerializerOptions to allow named floating point literals
        var customOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        services.Configure<McpServerOptions>(options =>
        {
            options.JsonSerializerOptions = customOptions;
        });

        // Register a tool that will use the server-wide JsonSerializerOptions
        // The null serializerOptions parameter should cause it to use McpServerOptions.JsonSerializerOptions
        services.AddSingleton(sp =>
        {
            var serverOptions = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;
            return McpServerTool.Create(
                () => new SpecialNumbers
                {
                    PositiveInfinity = double.PositiveInfinity,
                    NegativeInfinity = double.NegativeInfinity,
                    NotANumber = double.NaN
                },
                new McpServerToolCreateOptions
                {
                    Name = "GetSpecialNumbers",
                    Description = "Returns special floating point values",
                    UseStructuredContent = true,
                    SerializerOptions = serverOptions.JsonSerializerOptions,
                    Services = sp
                });
        });
    }

    [Fact]
    public async Task ServerWide_JsonSerializerOptions_Applied_To_Tools()
    {
        // Arrange
        McpClient client = await CreateMcpClientForServer();

        // Act
        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        CallToolResult result = await client.CallToolAsync("GetSpecialNumbers", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tools);
        Assert.Single(tools);
        Assert.Equal("GetSpecialNumbers", tools[0].Name);

        // Verify the result contains structured content with special numbers
        Assert.NotNull(result);
        Assert.NotNull(result.StructuredContent);
        
        var structuredContent = JsonSerializer.Deserialize<SpecialNumbers>(
            result.StructuredContent.ToString(),
            new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            });

        Assert.NotNull(structuredContent);
        Assert.True(double.IsPositiveInfinity(structuredContent.PositiveInfinity));
        Assert.True(double.IsNegativeInfinity(structuredContent.NegativeInfinity));
        Assert.True(double.IsNaN(structuredContent.NotANumber));
    }
}
