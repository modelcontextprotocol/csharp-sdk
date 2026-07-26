using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

public class McpClientToolRejectionTests : ClientServerTestBase
{
    private const string InvalidToolName = "InvalidHeaderTool";
    private const string ValidToolName = "ValidTool";

    public McpClientToolRejectionTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Register a valid tool.
        mcpServerBuilder.WithTools([McpServerTool.Create(
            (string input) => $"echo {input}",
            new() { Name = ValidToolName })]);

        // Register a tool whose InputSchema has an invalid x-mcp-header (colon in header name).
        var invalidTool = McpServerTool.Create(
            (string region) => $"result for {region}",
            new() { Name = InvalidToolName });

        // Manually inject an invalid x-mcp-header annotation into the schema.
        // The header name "Invalid:Header" contains a colon, which is prohibited.
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "region": {
                        "type": "string",
                        "x-mcp-header": "Invalid:Header"
                    }
                }
            }
            """;
        invalidTool.ProtocolTool.InputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone();

        mcpServerBuilder.WithTools([invalidTool]);
    }

    [Fact]
    public async Task ListToolsAsync_ExcludesToolWithInvalidXMcpHeader_AndLogsWarning()
    {
        // Act
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the valid tool is returned, the invalid one is excluded.
        Assert.Contains(tools, t => t.Name == ValidToolName);
        Assert.DoesNotContain(tools, t => t.Name == InvalidToolName);

        // Assert: a warning was logged about the rejected tool.
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Message.Contains(InvalidToolName) &&
            log.Message.Contains("excluded"));
    }
}
