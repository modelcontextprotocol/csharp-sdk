using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Integration tests for the Multi Round-Trip Requests (MRTR) flow.
/// These verify that when a server tool calls ElicitAsync/SampleAsync/RequestRootsAsync,
/// the SDK transparently returns an IncompleteResult to the client, the client resolves
/// the input requests via its handlers, and retries the original request.
/// </summary>
public class McpClientMrtrTests : ClientServerTestBase
{
    public McpClientMrtrTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ExperimentalProtocolVersion = "2026-06-XX";
        });

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
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    var result = await server.RequestRootsAsync(new ListRootsRequestParams(), ct);
                    return string.Join(",", result.Roots.Select(r => r.Uri));
                },
                new McpServerToolCreateOptions
                {
                    Name = "roots-tool",
                    Description = "A tool that requests roots from the client"
                }),
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    // First round-trip: elicit a name
                    var nameResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "What is your name?",
                        RequestedSchema = new()
                    }, ct);

                    // Second round-trip: elicit a greeting preference
                    var greetingResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "How should I greet you?",
                        RequestedSchema = new()
                    }, ct);

                    var name = nameResult.Content?.FirstOrDefault().Value;
                    var greeting = greetingResult.Content?.FirstOrDefault().Value;
                    return $"{greeting} {name}!";
                },
                new McpServerToolCreateOptions
                {
                    Name = "multi-elicit-tool",
                    Description = "A tool that elicits twice in sequence"
                }),
            McpServerTool.Create(
                async (string prompt, McpServer server, CancellationToken ct) =>
                {
                    // Sampling + elicitation in sequence
                    var sampleResult = await server.SampleAsync(new CreateMessageRequestParams
                    {
                        Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                        MaxTokens = 100
                    }, ct);

                    var sampleText = sampleResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

                    var elicitResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = $"Confirm: {sampleText}",
                        RequestedSchema = new()
                    }, ct);

                    return $"sample={sampleText},action={elicitResult.Action}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "sample-then-elicit-tool",
                    Description = "A tool that samples then elicits"
                })
        ]);
    }

    [Fact]
    public async Task CallToolAsync_WithSamplingTool_ResolvesViaMrtr()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[request.Messages.Count - 1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"Sampled: {text}" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello world" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Sampled: Hello world", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_WithElicitationTool_ResolvesViaMrtr()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "confirm",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse("\"yes\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("elicitation-tool",
            new Dictionary<string, object?> { ["message"] = "Do you agree?" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("confirm:yes", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_WithRootsTool_ResolvesViaMrtr()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.RootsHandler = (request, ct) =>
        {
            return new ValueTask<ListRootsResult>(new ListRootsResult
            {
                Roots = [new Root { Uri = "file:///project", Name = "Project" }]
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("roots-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("file:///project", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_WithMultipleElicitations_ResolvesMultipleMrtrRoundTrips()
    {
        StartServer();
        int callCount = 0;
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            var count = Interlocked.Increment(ref callCount);
            string value = count == 1 ? "Alice" : "Hello";
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "confirm",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("multi-elicit-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Hello Alice!", Assert.IsType<TextContentBlock>(content).Text);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task CallToolAsync_WithSamplingThenElicitation_ResolvesSequentialMrtrRoundTrips()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "AI response" }],
                Model = "test-model"
            });
        };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("sample-then-elicit-tool",
            new Dictionary<string, object?> { ["prompt"] = "Test" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("sample=AI response,action=accept", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_ServerExperimentalClientNot_UsesLegacyRequests()
    {
        // Server has ExperimentalProtocolVersion set (from ConfigureServices),
        // but client does NOT. Server negotiates to stable version.
        // ClientSupportsMrtr() returns false → standard JSON-RPC requests.
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

        // Verify the negotiated version is NOT the experimental one
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello from legacy client" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Legacy: Hello from legacy client", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_BothExperimental_UsesMrtr()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[request.Messages.Count - 1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"MRTR: {text}" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Verify the negotiated version IS the experimental one
        Assert.Equal("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello from both" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("MRTR: Hello from both", Assert.IsType<TextContentBlock>(content).Text);
    }
}
