using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Regression tests for issue #1721: the SDK-provided <c>resultType</c>, <c>ttlMs</c>, and
/// <c>cacheScope</c> result decorations are exclusive to the 2026-07-28 protocol revision and
/// must be absent from result objects on a session negotiated to an earlier revision
/// (e.g. <c>2025-11-25</c>). Emitting them breaks clients that strictly validate the 2025-11-25
/// schema.
/// </summary>
/// <remarks>
/// These drive the in-process <see cref="ClientServerTestBase"/> client/server pair and inspect the
/// raw JSON-RPC result wire shape (via <see cref="McpSession.SendRequestAsync(JsonRpcRequest, System.Threading.CancellationToken)"/>)
/// so the assertions are about the actual serialized fields rather than deserialized objects.
/// <c>tools/list</c> exercises the <c>SetHandler</c> decoration path (its result is both an
/// <see cref="ICacheableResult"/> and a <see cref="Result"/>), while <c>tools/call</c> exercises the
/// <c>SetWithAlternateHandler</c> path.
/// </remarks>
public class ProtocolVersionResultDecorationTests : ClientServerTestBase
{
    public ProtocolVersionResultDecorationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools([McpServerTool.Create(() => "ok", new() { Name = "echo" })]);
    }

    [Fact]
    public async Task ToolsList_On2025_11_25Session_OmitsResultTypeAndCacheHints()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsList },
            TestContext.Current.CancellationToken);

        var result = response.Result!.AsObject();
        Assert.False(result.ContainsKey("resultType"), "resultType must be absent on a 2025-11-25 tools/list result.");
        Assert.False(result.ContainsKey("ttlMs"), "ttlMs must be absent on a 2025-11-25 tools/list result.");
        Assert.False(result.ContainsKey("cacheScope"), "cacheScope must be absent on a 2025-11-25 tools/list result.");
    }

    [Fact]
    public async Task ToolsCall_On2025_11_25Session_OmitsResultType()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = System.Text.Json.JsonSerializer.SerializeToNode(
                    new CallToolRequestParams { Name = "echo" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        var result = response.Result!.AsObject();
        Assert.False(result.ContainsKey("resultType"), "resultType must be absent on a 2025-11-25 tools/call result.");
    }

    [Fact]
    public async Task ToolsList_On2026_07_28Session_IncludesResultTypeAndCacheHints()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsList },
            TestContext.Current.CancellationToken);

        var result = response.Result!.AsObject();
        Assert.Equal("complete", result["resultType"]!.GetValue<string>());
        Assert.True(result.ContainsKey("ttlMs"), "ttlMs must be present on a 2026-07-28 tools/list result.");
        Assert.True(result.ContainsKey("cacheScope"), "cacheScope must be present on a 2026-07-28 tools/list result.");
    }

    [Fact]
    public async Task ToolsCall_On2026_07_28Session_IncludesResultType()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = System.Text.Json.JsonSerializer.SerializeToNode(
                    new CallToolRequestParams { Name = "echo" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        var result = response.Result!.AsObject();
        Assert.Equal("complete", result["resultType"]!.GetValue<string>());
    }
}
