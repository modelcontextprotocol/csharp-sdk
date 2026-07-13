using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Wire-format conformance tests for the Streamable HTTP server driven directly via <see cref="HttpClient"/>,
/// without going through <see cref="ModelContextProtocol.Client.McpClient"/>. These hand-craft HTTP
/// requests and assert the exact status codes / response bodies the server emits for the SEP-2575 +
/// SEP-2567 2026-07-28 protocol revision.
/// </summary>
public class RawHttpConformanceTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private const string ProtocolVersionHeader = "MCP-Protocol-Version";

    private WebApplication? _app;

    private async Task StartAsync(string? protocolVersion = null)
    {
        Builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = nameof(RawHttpConformanceTests), Version = "1.0" };
                options.ProtocolVersion = protocolVersion;
            })
            .WithHttpTransport()
            .WithTools([McpServerTool.Create((string text) => $"echo:{text}", new() { Name = "echo" })]);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>
    /// Reads either a direct JSON response or a single SSE message containing JSON-RPC and returns the
    /// parsed JsonNode. The Streamable HTTP server can return either content type depending on negotiation;
    /// raw HttpClient tests should accept either.
    /// </summary>
    private static async Task<JsonNode> ReadJsonResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (contentType == "text/event-stream")
        {
            // Pull the first non-empty data: line out of the SSE payload.
            foreach (var line in body.Split('\n'))
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var data = line.Substring("data:".Length).Trim();
                    if (data.Length > 0)
                    {
                        return JsonNode.Parse(data)!;
                    }
                }
            }
            throw new InvalidOperationException("SSE response did not contain a JSON data event. Body: " + body);
        }

        return JsonNode.Parse(body)!;
    }

    private static string July2026ProtocolMetaFragment(string protocolVersion = McpProtocolVersions.July2026ProtocolVersion) =>
        @"""_meta"":{""io.modelcontextprotocol/protocolVersion"":""" + protocolVersion +
        @""",""io.modelcontextprotocol/clientInfo"":{""name"":""raw"",""version"":""1.0""}," +
        @"""io.modelcontextprotocol/clientCapabilities"":{}}";

    [Fact]
    public async Task July2026ToolsCall_WithFullMeta_Succeeds_200()
    {
        await StartAsync();

        var body =
            @"{""jsonrpc"":""2.0"",""id"":1,""method"":""tools/call"",""params"":{""name"":""echo"",""arguments"":{""text"":""hi""}," +
            July2026ProtocolMetaFragment() + "}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add(ProtocolVersionHeader, McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "echo");
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal("echo:hi", json["result"]!["content"]![0]!["text"]!.GetValue<string>());

        // Per SEP-2567, starting with the 2026-07-28 protocol revision Streamable HTTP no longer
        // supports sessions: the server MUST NOT issue a Mcp-Session-Id.
        Assert.False(response.Headers.Contains("mcp-session-id"));
    }

    [Fact]
    public async Task ServerDiscover_RawPost_ReturnsDiscoverResult()
    {
        await StartAsync();

        var body = @"{""jsonrpc"":""2.0"",""id"":1,""method"":""server/discover"",""params"":{" + July2026ProtocolMetaFragment() + "}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add(ProtocolVersionHeader, McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", "server/discover");
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        var supported = json["result"]!["supportedVersions"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal([McpProtocolVersions.July2026ProtocolVersion], supported);

        // Spec PR #2855 makes ttlMs and cacheScope required on DiscoverResult; the server emits the
        // safest defaults (immediately stale, not shareable) when the application hasn't customized.
        Assert.Equal(JsonValueKind.Number, json["result"]!["ttlMs"]!.GetValueKind());
        Assert.Equal(0, json["result"]!["ttlMs"]!.GetValue<long>());
        Assert.Equal("private", json["result"]!["cacheScope"]!.GetValue<string>());
    }

    [Fact]
    public async Task ServerDiscover_WithConfiguredPerRequestMetadataProtocol_ReturnsOnlyConfiguredVersion()
    {
        await StartAsync(McpProtocolVersions.July2026ProtocolVersion);

        var body = @"{""jsonrpc"":""2.0"",""id"":1,""method"":""server/discover"",""params"":{" + July2026ProtocolMetaFragment() + "}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add(ProtocolVersionHeader, McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", "server/discover");
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        var supported = json["result"]!["supportedVersions"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal([McpProtocolVersions.July2026ProtocolVersion], supported);
    }

    [Fact]
    public async Task July2026Post_WithUnsupportedProtocolVersionHeader_Returns400_With_Minus32022()
    {
        await StartAsync();

        var body =
            @"{""jsonrpc"":""2.0"",""id"":1,""method"":""tools/call"",""params"":{""name"":""echo"",""arguments"":{""text"":""x""}," +
            July2026ProtocolMetaFragment("9999-99-99") + "}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add(ProtocolVersionHeader, "9999-99-99");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "echo");
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        // Per spec/streamable-http.mdx the server MUST return 400 Bad Request with -32022 and a data payload
        // listing the supported versions. The dual-path client uses this to switch versions without fallback.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal((int)McpErrorCode.UnsupportedProtocolVersion, json["error"]!["code"]!.GetValue<int>());

        var data = json["error"]!["data"];
        Assert.NotNull(data);
        Assert.Equal("9999-99-99", data!["requested"]!.GetValue<string>());
        var supported = data["supported"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains(McpProtocolVersions.July2026ProtocolVersion, supported);
    }

    [Fact]
    public async Task July2026Post_ProtocolVersionHeaderMetaMismatch_ReturnsHeaderMismatch_Minus32020()
    {
        await StartAsync();

        // The MCP-Protocol-Version header declares the 2026-07-28 protocol revision, but the per-request _meta declares a
        // different (still individually supported) version. Per SEP-2575 the server MUST reject the
        // disagreement. It uses -32020 HeaderMismatch (the same code as the Mcp-Method/Mcp-Name header-vs-body
        // checks) so a conformant client on this revision surfaces the error instead of mistaking the
        // per-request-metadata server for an initialize-handshake one and falling back to initialize.
        var body =
            @"{""jsonrpc"":""2.0"",""id"":4242,""method"":""server/discover"",""params"":{" +
            July2026ProtocolMetaFragment("2025-11-25") + "}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add(ProtocolVersionHeader, McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", "server/discover");
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal((int)McpErrorCode.HeaderMismatch, json["error"]!["code"]!.GetValue<int>());

        // The body parsed successfully, so per the base protocol responses section (and SEP-2243's error
        // response format) this error MUST echo the request id rather than emitting id=null (see #1677).
        Assert.Equal(4242, json["id"]!.GetValue<long>());
    }

    [Fact]
    public async Task July2026Post_MissingMcpNameHeader_ReturnsHeaderMismatch_EchoesRequestId()
    {
        await StartAsync();

        // A well-formed tools/call whose body parses, but the required Mcp-Name header is absent. The server
        // rejects it with -32020 HeaderMismatch, and because the request id was readable the JSON-RPC error
        // MUST carry that same id (regression guard for #1677).
        var body =
            @"{""jsonrpc"":""2.0"",""id"":4242,""method"":""tools/call"",""params"":{""name"":""echo"",""arguments"":{""text"":""hi""}," +
            July2026ProtocolMetaFragment() + "}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add(ProtocolVersionHeader, McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", "tools/call");
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal((int)McpErrorCode.HeaderMismatch, json["error"]!["code"]!.GetValue<int>());
        Assert.Equal(4242, json["id"]!.GetValue<long>());
    }

    [Fact]
    public async Task July2026Post_WithServerPinnedToInitializeHandshakeVersion_ReturnsUnsupportedProtocolVersion()
    {
        await StartAsync(McpProtocolVersions.November2025ProtocolVersion);

        var body =
            @"{""jsonrpc"":""2.0"",""id"":1,""method"":""tools/call"",""params"":{""name"":""echo"",""arguments"":{""text"":""x""}," +
            July2026ProtocolMetaFragment() + "}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add(ProtocolVersionHeader, McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "echo");
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal((int)McpErrorCode.UnsupportedProtocolVersion, json["error"]!["code"]!.GetValue<int>());
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, json["error"]!["data"]!["requested"]!.GetValue<string>());

        var supported = json["error"]!["data"]!["supported"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal([McpProtocolVersions.November2025ProtocolVersion], supported);
    }

    [Fact]
    public async Task July2026Post_MissingBodyProtocolVersion_ReturnsHeaderMismatch_Minus32020()
    {
        await StartAsync();

        var body = @"{""jsonrpc"":""2.0"",""id"":1,""method"":""server/discover"",""params"":{""_meta"":{""io.modelcontextprotocol/clientInfo"":{""name"":""raw"",""version"":""1.0""},""io.modelcontextprotocol/clientCapabilities"":{}}}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add(ProtocolVersionHeader, McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", "server/discover");
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal((int)McpErrorCode.HeaderMismatch, json["error"]!["code"]!.GetValue<int>());
        Assert.Contains(MetaKeys.ProtocolVersion, json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task July2026Post_MissingProtocolVersionHeader_ReturnsHeaderMismatch_Minus32020()
    {
        await StartAsync();

        var body = @"{""jsonrpc"":""2.0"",""id"":1,""method"":""server/discover"",""params"":{" + July2026ProtocolMetaFragment() + "}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add("Mcp-Method", "server/discover");
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal((int)McpErrorCode.HeaderMismatch, json["error"]!["code"]!.GetValue<int>());
        Assert.Contains(ProtocolVersionHeader, json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initialize_WithPerRequestMetadataProtocolHeaderAndInitializeBody_ReturnsHeaderMismatch_Minus32020()
    {
        await StartAsync();

        var body = @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{""protocolVersion"":""2025-11-25"",""capabilities"":{},""clientInfo"":{""name"":""initialize-handshake"",""version"":""1.0""}}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        request.Headers.Add(ProtocolVersionHeader, McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", RequestMethods.Initialize);
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal((int)McpErrorCode.HeaderMismatch, json["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task InitializeHandshake_StillSucceeds_OnDefaultServer()
    {
        await StartAsync();

        var body = @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{""protocolVersion"":""2025-11-25"",""capabilities"":{},""clientInfo"":{""name"":""initialize-handshake"",""version"":""1.0""}}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent(body) };
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonResponseAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal("2025-11-25", json["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetEndpoint_NotMapped_UnderDefaultStatelessConfiguration_Returns405()
    {
        await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "");
        request.Headers.Accept.Add(new("text/event-stream"));
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        // Stateless=true (the new default) doesn't map the GET endpoint - per SEP-2567 the standalone SSE
        // stream is replaced by subscriptions/listen POST requests. Existing routing in
        // McpEndpointRouteBuilderExtensions only maps GET when Stateless == false.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
