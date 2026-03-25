using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests that message filters correctly observe MRTR protocol behavior — verifying that
/// IncompleteResult responses are visible to outgoing filters, and that no legacy
/// elicitation/sampling requests are sent when MRTR is active.
/// </summary>
public class MrtrMessageFilterTests : ClientServerTestBase
{
    private readonly ServerMessageTracker _messageTracker = new();

    public MrtrMessageFilterTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ExperimentalProtocolVersion = "2026-06-XX";
            _messageTracker.AddFilters(options.Filters.Message);
        });

        mcpServerBuilder
            .WithTools([
                McpServerTool.Create(
                    async (string message, McpServer server, CancellationToken ct) =>
                    {
                        var result = await server.ElicitAsync(new ElicitRequestParams
                        {
                            Message = message,
                            RequestedSchema = new()
                        }, ct);

                        return $"{result.Action}";
                    },
                    new McpServerToolCreateOptions
                    {
                        Name = "elicit-tool",
                        Description = "A tool that requests elicitation"
                    }),
                McpServerTool.Create(
                    async (string prompt, McpServer server, CancellationToken ct) =>
                    {
                        var result = await server.SampleAsync(new CreateMessageRequestParams
                        {
                            Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                            MaxTokens = 100
                        }, ct);

                        return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
                    },
                    new McpServerToolCreateOptions
                    {
                        Name = "sample-tool",
                        Description = "A tool that requests sampling"
                    }),
            ]);
    }

    [Fact]
    public async Task MrtrActive_NoOldStyleElicitationRequests_SentOverWire()
    {
        // When both sides are on the experimental protocol, the server should use MRTR
        // (IncompleteResult) instead of sending old-style elicitation/create JSON-RPC requests.
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.Equal("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("elicit-tool",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("accept", Assert.IsType<TextContentBlock>(content).Text);
        _messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task MrtrActive_NoOldStyleSamplingRequests_SentOverWire()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[^1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"Sampled: {text}" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.Equal("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("sample-tool",
            new Dictionary<string, object?> { ["prompt"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Sampled: test", Assert.IsType<TextContentBlock>(content).Text);
        _messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task OutgoingFilter_SeesIncompleteResultResponse()
    {
        // Verify that transport middleware can observe the raw IncompleteResult
        // in outgoing JSON-RPC responses (validates MRTR transport visibility).
        var sawIncompleteResult = false;

        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            // If we reach this handler, it means the client received an IncompleteResult
            // from the server, resolved the elicitation, and is retrying.
            sawIncompleteResult = true;
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        await client.CallToolAsync("elicit-tool",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        // The elicitation handler was called, confirming MRTR round-trip occurred
        // (IncompleteResult was sent by server and processed by client).
        Assert.True(sawIncompleteResult, "Expected MRTR round-trip with IncompleteResult");
        _messageTracker.AssertMrtrUsed();
    }
}
