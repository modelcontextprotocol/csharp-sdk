using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Tests for MRTR compatibility across different experimental/non-experimental combinations.
/// This test class configures the server WITHOUT ExperimentalProtocolVersion to test scenarios
/// where the server is not opted-in to the experimental protocol.
/// </summary>
public class McpClientMrtrCompatTests : ClientServerTestBase
{
    public McpClientMrtrCompatTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Deliberately NOT setting ExperimentalProtocolVersion on the server.
        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                async (string prompt, McpServer server, CancellationToken ct) =>
                {
                    var result = await server.SampleAsync(new CreateMessageRequestParams
                    {
                        Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                        MaxTokens = 100
                    }, ct);

                    return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No response";
                },
                new McpServerToolCreateOptions
                {
                    Name = "sampling-tool",
                    Description = "A tool that requests sampling from the client"
                }),
            McpServerTool.Create(
                async (string message, McpServer server, CancellationToken ct) =>
                {
                    var result = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = message,
                        RequestedSchema = new()
                    }, ct);

                    return $"{result.Action}:{result.Content?.FirstOrDefault().Value}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "elicitation-tool",
                    Description = "A tool that requests elicitation from the client"
                }),
        ]);
    }

    [Fact]
    public async Task CallToolAsync_NeitherExperimental_UsesLegacyRequests()
    {
        // Neither client nor server sets ExperimentalProtocolVersion.
        // Server sends standard JSON-RPC sampling/elicitation requests.
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[request.Messages.Count - 1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"Legacy: {text}" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Verify the negotiated version is a standard stable version
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Legacy: Hello", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_ClientExperimentalServerNot_FallsBackToLegacy()
    {
        // Client requests experimental version, server doesn't recognize it,
        // negotiates to stable. Everything works via legacy path.
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[request.Messages.Count - 1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"Legacy: {text}" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Verify the server did NOT negotiate to the experimental version
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "From exp client" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Legacy: From exp client", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_NeitherExperimental_ElicitationUsesLegacyRequests()
    {
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "confirm",
                Content = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["response"] = System.Text.Json.JsonDocument.Parse("\"yes\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("elicitation-tool",
            new Dictionary<string, object?> { ["message"] = "Agree?" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("confirm:yes", Assert.IsType<TextContentBlock>(content).Text);
    }
}
