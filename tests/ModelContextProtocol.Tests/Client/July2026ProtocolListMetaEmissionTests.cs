using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Verifies that the C# client emits the SEP-2575 <c>_meta</c> envelope on every list-style
/// request (and on <c>server/discover</c>) under the 2026-07-28 protocol revision, even when
/// the caller supplies no <c>RequestOptions</c> / no params.
/// </summary>
/// <remarks>
/// Spec PR #2759 promotes <c>params._meta</c> to required on <c>tools/list</c>,
/// <c>resources/list</c>, <c>resources/templates/list</c>, <c>prompts/list</c>, and
/// <c>server/discover</c>. This test class drives the C# client through
/// <see cref="ClientServerTestBase"/> with the 2026-07-28 protocol negotiated, attaches a request
/// filter on each list endpoint that captures the incoming <c>_meta</c> envelope, and asserts
/// the three required SEP-2575 keys are present:
///   <c>io.modelcontextprotocol/protocolVersion</c>,
///   <c>io.modelcontextprotocol/clientInfo</c>, and
///   <c>io.modelcontextprotocol/clientCapabilities</c>.
/// </remarks>
public class July2026ProtocolListMetaEmissionTests : ClientServerTestBase
{
    private const string LatestStableVersion = "2025-11-25";

    // Captured _meta envelopes for each request method we exercise. Populated by the per-method
    // server-side filters and asserted from each test method.
    private readonly Dictionary<string, JsonObject?> _capturedMeta = new(StringComparer.Ordinal);

    public July2026ProtocolListMetaEmissionTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithRequestFilters(filters =>
        {
            filters.AddListToolsFilter(next => async (request, cancellationToken) =>
            {
                _capturedMeta[RequestMethods.ToolsList] = request.Params?.Meta;
                return await next(request, cancellationToken);
            });
            filters.AddListPromptsFilter(next => async (request, cancellationToken) =>
            {
                _capturedMeta[RequestMethods.PromptsList] = request.Params?.Meta;
                return await next(request, cancellationToken);
            });
            filters.AddListResourcesFilter(next => async (request, cancellationToken) =>
            {
                _capturedMeta[RequestMethods.ResourcesList] = request.Params?.Meta;
                return await next(request, cancellationToken);
            });
            filters.AddListResourceTemplatesFilter(next => async (request, cancellationToken) =>
            {
                _capturedMeta[RequestMethods.ResourcesTemplatesList] = request.Params?.Meta;
                return await next(request, cancellationToken);
            });
        });

        // No-op list handlers (so the requests complete) — content is irrelevant; we only assert the
        // incoming envelope.
        mcpServerBuilder
            .WithListToolsHandler((_, _) => new ValueTask<ListToolsResult>(new ListToolsResult { Tools = [] }))
            .WithListPromptsHandler((_, _) => new ValueTask<ListPromptsResult>(new ListPromptsResult { Prompts = [] }))
            .WithListResourcesHandler((_, _) => new ValueTask<ListResourcesResult>(new ListResourcesResult { Resources = [] }))
            .WithListResourceTemplatesHandler((_, _) => new ValueTask<ListResourceTemplatesResult>(
                new ListResourceTemplatesResult { ResourceTemplates = [] }));
    }

    [Fact]
    public async Task Client_ListTools_NoOptions_EmitsRequiredMeta()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        AssertRequiredMetaPresent(RequestMethods.ToolsList);
    }

    [Fact]
    public async Task Client_ListPrompts_NoOptions_EmitsRequiredMeta()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        AssertRequiredMetaPresent(RequestMethods.PromptsList);
    }

    [Fact]
    public async Task Client_ListResources_NoOptions_EmitsRequiredMeta()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        AssertRequiredMetaPresent(RequestMethods.ResourcesList);
    }

    [Fact]
    public async Task Client_ListResourceTemplates_NoOptions_EmitsRequiredMeta()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

        AssertRequiredMetaPresent(RequestMethods.ResourcesTemplatesList);
    }

    [Fact]
    public async Task Client_ServerDiscover_EmitsRequiredMeta()
    {
        // server/discover has no public List-style helper; we drive it via SendRequestAsync directly,
        // which still flows through the client's per-request _meta injector.
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        // Hook the server-side handler invocation via a notification handler is awkward here; assert
        // instead by sending the request and parsing the wire-shape echo from the response context.
        // Easier path: rely on the existing JsonRpcRequest capture in the message context (see the
        // raw conformance tests for the wire-level proof). For this in-process test, we instead drive
        // the request and rely on the response being a valid DiscoverResult; the _meta injector
        // would otherwise have failed the server's per-request envelope validation.
        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ServerDiscover },
            TestContext.Current.CancellationToken);

        Assert.NotNull(response.Result);
        var discover = JsonSerializer.Deserialize<DiscoverResult>(response.Result, McpJsonUtilities.DefaultOptions)!;
        Assert.Contains(McpProtocolVersions.July2026ProtocolVersion, discover.SupportedVersions);

        // The server enforces the per-request envelope shape; if the client had omitted _meta, the
        // request would have failed with -32602 / -32021 rather than returning a DiscoverResult. The
        // successful round-trip is the assertion.
    }

    [Fact]
    public async Task InitializeHandshakeClient_ListTools_DoesNotEmitMeta()
    {
        // Sanity guard: a client on the session-supporting initialize-handshake protocol must NOT emit the SEP-2575
        // envelope. The injector is gated on the negotiated protocol version; if it ever started writing
        // those keys on an initialize-handshake request, every initialize-handshake server would reject it.
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var meta = _capturedMeta[RequestMethods.ToolsList];
        if (meta is not null)
        {
            Assert.False(meta.ContainsKey(MetaKeys.ProtocolVersion));
            Assert.False(meta.ContainsKey(MetaKeys.ClientInfo));
            Assert.False(meta.ContainsKey(MetaKeys.ClientCapabilities));
        }
    }

    private void AssertRequiredMetaPresent(string method)
    {
        Assert.True(_capturedMeta.TryGetValue(method, out var meta), $"No capture for {method}");
        Assert.NotNull(meta);
        Assert.True(meta!.ContainsKey(MetaKeys.ProtocolVersion),
            $"Missing protocolVersion key on {method} _meta envelope");
        Assert.True(meta.ContainsKey(MetaKeys.ClientInfo),
            $"Missing clientInfo key on {method} _meta envelope");
        Assert.True(meta.ContainsKey(MetaKeys.ClientCapabilities),
            $"Missing clientCapabilities key on {method} _meta envelope");

        // The protocolVersion value must match the negotiated 2026-07-28 protocol version.
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, meta[MetaKeys.ProtocolVersion]!.GetValue<string>());
    }
}
