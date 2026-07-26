using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that the server-to-client request methods (<see cref="McpServer.ElicitAsync(ElicitRequestParams, CancellationToken)"/>,
/// <see cref="McpServer.SampleAsync(CreateMessageRequestParams, CancellationToken)"/>,
/// <see cref="McpServer.RequestRootsAsync"/>) keep working when the negotiated protocol revision is
/// <c>2026-07-28</c> on a stateful transport - for example, stdio.
/// </summary>
/// <remarks>
/// Under <c>2026-07-28</c> the spec removes the corresponding server-to-client request methods, but
/// the SDK only fails fast in stateless mode (where the existing <c>ThrowIf*Unsupported</c> guards already
/// throw "X is not supported in stateless mode" because <see cref="McpServer.ClientCapabilities"/> is
/// <see langword="null"/>). Stdio is implicitly stateful - one <see cref="McpServer"/> per process - so the
/// legacy <c>elicitation/create</c> / <c>sampling/createMessage</c> / <c>roots/list</c> flow still works.
/// Starting with <c>2026-07-28</c>, Streamable HTTP servers are stateless by default, so those configurations
/// throw through the existing stateless guard unless the author explicitly opts back into sessions.
/// </remarks>
public sealed class July2026ProtocolBackcompatTests : ClientServerTestBase
{
    public July2026ProtocolBackcompatTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ProtocolVersion = "2026-07-28";
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(ElicitToolAsync, new() { Name = "elicit-tool" }),
            McpServerTool.Create(SampleToolAsync, new() { Name = "sample-tool" }),
            McpServerTool.Create(RootsToolAsync, new() { Name = "roots-tool" }),
        ]);
    }

    [Fact]
    public async Task ElicitAsync_OnStatefulTransport_ResolvesViaLegacyRequest()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = "2026-07-28",
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability(),
            },
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (_, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" }),
            },
        });

        var result = await client.CallToolAsync("elicit-tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("elicit-ok:accept", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task SampleAsync_OnStatefulTransport_ResolvesViaLegacyRequest()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = "2026-07-28",
            Capabilities = new ClientCapabilities
            {
                Sampling = new SamplingCapability(),
            },
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (_, _, _) => new ValueTask<CreateMessageResult>(new CreateMessageResult
                {
                    Model = "test-model",
                    Role = Role.Assistant,
                    Content = [new TextContentBlock { Text = "hello back" }],
                }),
            },
        });

        var result = await client.CallToolAsync("sample-tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("sample-ok:hello back", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task RequestRootsAsync_OnStatefulTransport_ResolvesViaLegacyRequest()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = "2026-07-28",
            Capabilities = new ClientCapabilities
            {
                Roots = new RootsCapability(),
            },
            Handlers = new McpClientHandlers
            {
                RootsHandler = (_, _) => new ValueTask<ListRootsResult>(new ListRootsResult
                {
                    Roots = [new Root { Uri = "file:///home", Name = "home" }],
                }),
            },
        });

        var result = await client.CallToolAsync("roots-tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("roots-ok:file:///home", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    private static async Task<string> ElicitToolAsync(McpServer server, CancellationToken cancellationToken)
    {
        var elicit = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Need input",
            RequestedSchema = new(),
        }, cancellationToken);
        return $"elicit-ok:{elicit.Action}";
    }

    private static async Task<string> SampleToolAsync(McpServer server, CancellationToken cancellationToken)
    {
        var sample = await server.SampleAsync(new CreateMessageRequestParams
        {
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new TextContentBlock { Text = "ping" }],
                },
            ],
            MaxTokens = 16,
        }, cancellationToken);
        var text = sample.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        return $"sample-ok:{text}";
    }

    private static async Task<string> RootsToolAsync(McpServer server, CancellationToken cancellationToken)
    {
        var roots = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken);
        return $"roots-ok:{roots.Roots.FirstOrDefault()?.Uri}";
    }
}
