using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

public class McpClientMetaTests : ClientServerTestBase
{
    // InitializeMeta is carried on the legacy initialize request, which the 2026-07-28 protocol removes.
    // The two InitializeMeta_* tests pin to the latest stable version so the handshake actually runs.
    private const string LatestStableVersion = "2025-11-25";

    private readonly TaskCompletionSource<JsonNode?> _initializeMeta = new();

    private readonly TaskCompletionSource<(Implementation? Info, ClientCapabilities? Capabilities)> _outgoingFilterObserved =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public McpClientMetaTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(o =>
        {
            o.ServerInfo = new Implementation
            {
                Name = "test-server",
                Version = "1.0.0",
                Description = "A test server for unit testing",
                WebsiteUrl = "https://example.com",
            };
            o.ToolCollection = new ();
            o.ResourceCollection = new ();
            o.PromptCollection = new ();
        });

        // Capture the _meta the server receives on the initialize request so tests can
        // assert that McpClientOptions.InitializeMeta is threaded through the handshake.
        mcpServerBuilder.WithMessageFilters(filters =>
        {
            filters.AddIncomingFilter(next => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.Initialize } request)
                {
                    _initializeMeta.TrySetResult(request.Params?["_meta"]);
                }

                await next(context, cancellationToken);
            });

            // Capture the request-scoped client info/capabilities observed while an outgoing response flows
            // through the outgoing filter pipeline. Gated on a unique client name so only the dedicated test
            // triggers it. This exercises that DestinationBoundMcpServer resolves per-request _meta for
            // responses (whose Context is the originating request's Context), not just requests.
            filters.AddOutgoingFilter(next => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcResponse &&
                    context.Server.ClientInfo is { Name: "outgoing-filter-client" } info)
                {
                    _outgoingFilterObserved.TrySetResult((info, context.Server.ClientCapabilities));
                }

                await next(context, cancellationToken);
            });
        });
    }

    [Fact]
    public async Task InitializeMeta_IsSentToServer_WhenSet()
    {
        var clientOptions = new McpClientOptions
        {
            ProtocolVersion = LatestStableVersion,
            InitializeMeta = new JsonObject
            {
                { "foo", "bar baz" }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        var meta = await _initializeMeta.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.NotNull(meta);
        Assert.Equal("bar baz", meta["foo"]?.ToString());
    }

    [Fact]
    public async Task InitializeMeta_IsOmitted_WhenNotSet()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });

        var meta = await _initializeMeta.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Null(meta);
    }

    [Fact]
    public async Task ToolCallWithMetaFields()
    {
        Server.ServerOptions.ToolCollection?.Add(McpServerTool.Create(
            async (RequestContext<CallToolRequestParams> context) =>
            {
                // Access the foo property of _meta field from the request parameters
                var metaFoo = context.Params.Meta?["foo"]?.ToString();

                // Assert that the meta foo is correctly passed
                Assert.NotNull(metaFoo);

                return $"Meta foo is {metaFoo}";
            },
            new () { Name = "meta_tool" }));

        await using McpClient client = await CreateMcpClientForServer();

        var requestOptions = new RequestOptions()
        {
            Meta = new JsonObject()
            {
                { "foo", "bar baz" }
            }
        };

        var result = await client.CallToolAsync("meta_tool", options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result.IsError);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Contains("bar baz", textContent.Text);
    }

    [Fact]
    public async Task ConcurrentToolCalls_WithPerRequestClientCapabilities_UseRequestScopedCapabilities()
    {
        var withSamplingReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var withoutSamplingReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSamplingChecks = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Server.ServerOptions.ToolCollection?.Add(McpServerTool.Create(
            async (string requestId, RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken) =>
            {
                if (requestId == "with")
                {
                    withSamplingReady.TrySetResult(true);
                }
                else if (requestId == "without")
                {
                    withoutSamplingReady.TrySetResult(true);
                }
                else
                {
                    throw new ArgumentException($"Unexpected request id '{requestId}'.");
                }

                await allowSamplingChecks.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);

                return context.Server.ClientCapabilities?.Sampling is null ?
                    $"{requestId}:sampling-absent" :
                    $"{requestId}:sampling-present";
            },
            new() { Name = "meta_sampling_tool" }));

        await using McpClient client = await CreateMcpClientForServer();

        var withSamplingRequest = new CallToolRequestParams
        {
            Name = "meta_sampling_tool",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["requestId"] = JsonDocument.Parse("\"with\"").RootElement.Clone(),
            },
            Meta = new JsonObject
            {
                [MetaKeys.ClientCapabilities] = JsonSerializer.SerializeToNode(
                    new ClientCapabilities { Sampling = new SamplingCapability() },
                    McpJsonUtilities.DefaultOptions),
            },
        };

        var withoutSamplingRequest = new CallToolRequestParams
        {
            Name = "meta_sampling_tool",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["requestId"] = JsonDocument.Parse("\"without\"").RootElement.Clone(),
            },
            Meta = new JsonObject
            {
                [MetaKeys.ClientCapabilities] = JsonSerializer.SerializeToNode(
                    new ClientCapabilities(),
                    McpJsonUtilities.DefaultOptions),
            },
        };

        Task<CallToolResult> withSamplingTask = client.CallToolAsync(withSamplingRequest, TestContext.Current.CancellationToken).AsTask();
        Task<CallToolResult> withoutSamplingTask = client.CallToolAsync(withoutSamplingRequest, TestContext.Current.CancellationToken).AsTask();

        await Task.WhenAll(withSamplingReady.Task, withoutSamplingReady.Task).WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        allowSamplingChecks.TrySetResult(true);

        CallToolResult withSamplingResult = await withSamplingTask;
        CallToolResult withoutSamplingResult = await withoutSamplingTask;

        var withSamplingText = Assert.IsType<TextContentBlock>(Assert.Single(withSamplingResult.Content)).Text;
        var withoutSamplingText = Assert.IsType<TextContentBlock>(Assert.Single(withoutSamplingResult.Content)).Text;

        Assert.Equal("with:sampling-present", withSamplingText);
        Assert.Equal("without:sampling-absent", withoutSamplingText);
    }

    [Fact]
    public async Task ToolCall_UnderJuly2026Protocol_ObservesRequestScopedClientInfo()
    {
        Server.ServerOptions.ToolCollection?.Add(McpServerTool.Create(
            (RequestContext<CallToolRequestParams> context) =>
            {
                var clientInfo = context.Server.ClientInfo;
                return clientInfo is null ?
                    "client-info-absent" :
                    $"{clientInfo.Name}:{clientInfo.Version}";
            },
            new() { Name = "client_info_tool" }));

        // The 2026-07-28+ client stamps its ClientInfo onto every request's _meta, so the tool must observe
        // the per-request value resolved by DestinationBoundMcpServer rather than server-only session state.
        var clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "request-scoped-client", Version = "9.9.9" },
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("client_info_tool", cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("request-scoped-client:9.9.9", text);
    }

    [Fact]
    public async Task RootServer_UnderJuly2026Protocol_HasNoClientCapabilities_ButHandlerObservesThem()
    {
        ClientCapabilities? handlerObservedCapabilities = null;

        Server.ServerOptions.ToolCollection?.Add(McpServerTool.Create(
            (RequestContext<CallToolRequestParams> context) =>
            {
                handlerObservedCapabilities = context.Server.ClientCapabilities;
                return "ok";
            },
            new() { Name = "capability_probe_tool" }));

        var clientOptions = new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (_, _) => new ValueTask<ElicitResult>(new ElicitResult()),
            },
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Under the 2026-07-28 revision capabilities are request-scoped, so the root server (outside any
        // request) never exposes them, whereas a request handler observes the per-request _meta values.
        Assert.Null(Server.ClientCapabilities);

        await client.CallToolAsync("capability_probe_tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(handlerObservedCapabilities);
        Assert.NotNull(handlerObservedCapabilities!.Elicitation);
        Assert.Null(Server.ClientCapabilities);
    }

    [Fact]
    public async Task OutgoingMessageFilter_UnderJuly2026Protocol_ObservesRequestScopedClientInfoAndCapabilities()
    {
        Server.ServerOptions.ToolCollection?.Add(McpServerTool.Create(
            () => "ok",
            new() { Name = "outgoing_probe_tool" }));

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "outgoing-filter-client", Version = "3.2.1" },
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (_, _) => new ValueTask<ElicitResult>(new ElicitResult()),
            },
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        await client.CallToolAsync("outgoing_probe_tool", cancellationToken: TestContext.Current.CancellationToken);

        var (info, capabilities) = await _outgoingFilterObserved.Task
            .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.Equal("outgoing-filter-client", info!.Name);
        Assert.Equal("3.2.1", info.Version);
        Assert.NotNull(capabilities);
        Assert.NotNull(capabilities!.Elicitation);
    }

    [Fact]
    public async Task ResourceReadWithMetaFields()
    {
        Server.ServerOptions.ResourceCollection?.Add(McpServerResource.Create(
            (RequestContext<ReadResourceRequestParams> context) =>
            {
                // Access the foo property of _meta field from the request parameters
                var metaFoo = context.Params.Meta?["foo"]?.ToString();

                // Assert that the meta foo is correctly passed
                Assert.NotNull(metaFoo);

                return $"Resource with Meta foo is {metaFoo}";
            },
            new () { UriTemplate = "test://meta_resource" }));

        await using McpClient client = await CreateMcpClientForServer();

        var requestOptions = new RequestOptions()
        {
            Meta = new JsonObject()
            {
                { "foo", "bar baz" }
            }
        };

        var result = await client.ReadResourceAsync("test://meta_resource", options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        var textContent = result.Contents.OfType<TextResourceContents>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Contains("bar baz", textContent.Text);
    }


    [Fact]
    public async Task PromptGetWithMetaFields()
    {
        Server.ServerOptions.PromptCollection?.Add(McpServerPrompt.Create(
            (RequestContext<GetPromptRequestParams> context) =>
            {
                // Access the foo property of _meta field from the request parameters
                var metaFoo = context.Params.Meta?["foo"]?.ToString();

                // Assert that the meta foo is correctly passed
                Assert.NotNull(metaFoo);

                return $"Prompt with Meta foo is {metaFoo}";
            },
            new () { Name = "meta_prompt" }));

        await using McpClient client = await CreateMcpClientForServer();

        var requestOptions = new RequestOptions()
        {
            Meta = new JsonObject()
            {
                { "foo", "bar baz" }
            }
        };

        var result = await client.GetPromptAsync("meta_prompt", options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Messages);
        var message = result.Messages.First();
        Assert.NotNull(message.Content);
        var textContent = message.Content as TextContentBlock;
        Assert.NotNull(textContent);
        Assert.Contains("bar baz", textContent.Text);
    }
}