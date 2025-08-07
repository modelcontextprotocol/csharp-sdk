using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsCreateTargetHelperTests(ITestOutputHelper testOutputHelper) 
    : ClientServerTestBase(testOutputHelper)
{
    private const string ToolName = "Pets Service";
    private const string BaseAddress = "https://localhost:7387/pets";

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<PetsService>();

        services.AddHttpClient<PetsService>(client =>
        {
            client.BaseAddress = new Uri(BaseAddress);
        });
    }

    /// <summary>
    /// Verifies that a typed HttpClient registered in DI is correctly injected into a server tool
    /// and used by the tool implementation. This test covers the scenario described in
    /// https://github.com/modelcontextprotocol/csharp-sdk/issues/685.
    /// </summary>
    [Fact]
    public async Task Typed_HttpClient_Is_Used_By_Tool()
    {
        // Arrange
        await using var client = await CreateMcpClientForServer();

        // Act
        var result = await client.CallToolAsync(ToolName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        var text = (result.Content[0] as TextContentBlock)?.Text;
        Assert.Equal(BaseAddress, text);
    }

    [McpServerToolType]
    private sealed class PetsService(HttpClient httpClient)
    {
        [McpServerTool(Name = ToolName)]
        [Description("List all pets")]
        public string GetBaseAddress()
        {
            // Returning HttpClient.BaseAddress for verification
            return httpClient.BaseAddress?.ToString() ?? string.Empty;
        }
    }
}
