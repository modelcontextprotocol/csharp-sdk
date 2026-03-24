using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// End-to-end tests for MRTR over stateful Streamable HTTP using McpClient.
/// Unlike MrtrProtocolTests (raw HttpClient) and MrtrIntegrationTests (in-memory streams),
/// these exercise the full McpClient → HTTP transport → server MRTR → HTTP response path.
/// Tests cover both experimental (native MRTR) and normal (backcompat via legacy JSON-RPC) modes.
/// </summary>
public class StreamableHttpMrtrTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    private readonly HttpClientTransportOptions _transportOptions = new()
    {
        Endpoint = new("http://localhost:5000/"),
        Name = "Streamable HTTP MRTR Test Client",
        TransportMode = HttpTransportMode.StreamableHttp,
    };

    private async Task StartAsync()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(StreamableHttpMrtrTests),
                Version = "1",
            };
            options.ExperimentalProtocolVersion = "2026-06-XX";
        })
        .WithHttpTransport()
        .WithTools([
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
                    Name = "elicit-tool",
                    Description = "Elicits from client"
                }),
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
                    Description = "Samples from client"
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
                    Description = "Requests roots from client"
                }),
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    var nameResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "What is your name?",
                        RequestedSchema = new()
                    }, ct);

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
                    Description = "Elicits twice in sequence"
                }),
            McpServerTool.Create(
                static string (McpServer server) => server.IsMrtrSupported.ToString(),
                new McpServerToolCreateOptions
                {
                    Name = "check-mrtr-tool",
                    Description = "Returns IsMrtrSupported"
                }),
            McpServerTool.Create(
                static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
                {
                    var requestState = context.Params!.RequestState;
                    var inputResponses = context.Params!.InputResponses;

                    if (requestState is not null && inputResponses is not null)
                    {
                        var elicitResult = inputResponses["user_confirm"].ElicitationResult;
                        return $"lowlevel-confirmed:{elicitResult?.Action}:{requestState}";
                    }

                    if (!server.IsMrtrSupported)
                    {
                        return "lowlevel-unsupported:MRTR is not available";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["user_confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Please confirm",
                                RequestedSchema = new()
                            })
                        },
                        requestState: "lowlevel-state-1");
                },
                new McpServerToolCreateOptions
                {
                    Name = "lowlevel-tool",
                    Description = "Low-level MRTR tool managing state directly"
                }),
        ]);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);
    }

    private Task<McpClient> ConnectExperimentalAsync()
    {
        var options = CreateClientOptions();
        options.ExperimentalProtocolVersion = "2026-06-XX";
        return McpClient.CreateAsync(
            new HttpClientTransport(_transportOptions, HttpClient, LoggerFactory),
            options, LoggerFactory, TestContext.Current.CancellationToken);
    }

    private Task<McpClient> ConnectNormalAsync()
    {
        var options = CreateClientOptions();
        return McpClient.CreateAsync(
            new HttpClientTransport(_transportOptions, HttpClient, LoggerFactory),
            options, LoggerFactory, TestContext.Current.CancellationToken);
    }

    private static McpClientOptions CreateClientOptions()
    {
        var options = new McpClientOptions();
        options.Handlers.ElicitationHandler = (request, ct) =>
        {
            // Return different content based on the elicitation message so multi-elicit
            // tests can distinguish which round trip they're in.
            var message = request?.Message ?? "";
            var answer = message.Contains("name", StringComparison.OrdinalIgnoreCase) ? "Alice"
                : message.Contains("greet", StringComparison.OrdinalIgnoreCase) ? "Hello"
                : "yes";

            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse($"\"{answer}\"").RootElement.Clone()
                }
            });
        };
        options.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var prompt = request?.Messages?.LastOrDefault()?.Content
                .OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"LLM:{prompt}" }],
                Model = "test-model"
            });
        };
        options.Handlers.RootsHandler = (request, ct) =>
        {
            return new ValueTask<ListRootsResult>(new ListRootsResult
            {
                Roots = [
                    new Root { Uri = "file:///project", Name = "Project" },
                    new Root { Uri = "file:///data", Name = "Data" }
                ]
            });
        };
        return options;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    // =====================================================================
    // Experimental mode: both client and server set ExperimentalProtocolVersion.
    // MRTR is negotiated natively — the server suspends the handler and returns
    // IncompleteResult over SSE; the client resolves inputRequests and retries.
    // =====================================================================

    [Fact]
    public async Task Experimental_Elicitation_CompletesViaMrtr()
    {
        await StartAsync();
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("elicit-tool",
            new Dictionary<string, object?> { ["message"] = "Please confirm" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("accept:yes", text);
    }

    [Fact]
    public async Task Experimental_Sampling_CompletesViaMrtr()
    {
        await StartAsync();
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("LLM:Hello", text);
    }

    [Fact]
    public async Task Experimental_Roots_CompletesViaMrtr()
    {
        await StartAsync();
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("roots-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("file:///project,file:///data", text);
    }

    [Fact]
    public async Task Experimental_MultiRoundTrip_CompletesAcrossRetries()
    {
        await StartAsync();
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("multi-elicit-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("Hello Alice!", text);
    }

    [Fact]
    public async Task Experimental_IsMrtrSupported_ReturnsTrue()
    {
        await StartAsync();
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("check-mrtr-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("True", text);
    }

    [Fact]
    public async Task Experimental_LowLevel_IncompleteResultException_WorksEndToEnd()
    {
        await StartAsync();
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("lowlevel-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("lowlevel-confirmed:accept:lowlevel-state-1", text);
    }

    // =====================================================================
    // Normal mode: server sets ExperimentalProtocolVersion but client does NOT.
    // The backcompat layer (InvokeWithIncompleteResultHandlingAsync) intercepts
    // IncompleteResult and resolves inputRequests via legacy JSON-RPC
    // server-to-client requests within the same HTTP response.
    // =====================================================================

    [Fact]
    public async Task Normal_Elicitation_ResolvedViaLegacyJsonRpc()
    {
        await StartAsync();
        await using var client = await ConnectNormalAsync();

        var result = await client.CallToolAsync("elicit-tool",
            new Dictionary<string, object?> { ["message"] = "Please confirm" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("accept:yes", text);
    }

    [Fact]
    public async Task Normal_Sampling_ResolvedViaLegacyJsonRpc()
    {
        await StartAsync();
        await using var client = await ConnectNormalAsync();

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("LLM:Hello", text);
    }

    [Fact]
    public async Task Normal_MultiRoundTrip_ResolvedViaLegacyJsonRpc()
    {
        await StartAsync();
        await using var client = await ConnectNormalAsync();

        var result = await client.CallToolAsync("multi-elicit-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("Hello Alice!", text);
    }

    [Fact]
    public async Task Normal_IsMrtrSupported_ReturnsTrue()
    {
        // IsMrtrSupported is true for stateful + non-MRTR client because
        // the backcompat layer can resolve IncompleteResult via legacy JSON-RPC.
        await StartAsync();
        await using var client = await ConnectNormalAsync();

        var result = await client.CallToolAsync("check-mrtr-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("True", text);
    }

    [Fact]
    public async Task Normal_LowLevel_IncompleteResultException_ResolvedViaBackcompat()
    {
        // Low-level IncompleteResultException also works through backcompat:
        // the server catches it, extracts inputRequests, sends legacy JSON-RPC
        // requests to the client, then re-invokes the tool with inputResponses.
        await StartAsync();
        await using var client = await ConnectNormalAsync();

        var result = await client.CallToolAsync("lowlevel-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("lowlevel-confirmed:accept:lowlevel-state-1", text);
    }
}
