using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// HTTP-level tests for the draft protocol revision (SEP-2575 + SEP-2567): verify that the server
/// suppresses the <c>Mcp-Session-Id</c> header for draft requests and returns structured
/// <see cref="McpErrorCode.UnsupportedProtocolVersion"/> errors instead of plain 400s.
/// </summary>
public class DraftHttpHandlerTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private const string DraftVersion = McpHttpHeaders.DraftProtocolVersion;

    private WebApplication? _app;

    private async Task StartAsync(bool stateless = false)
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = nameof(DraftHttpHandlerTests), Version = "1" };
        }).WithHttpTransport(options =>
        {
            // Stateless = false maps the GET/DELETE endpoints and opts the author into sessions, which the
            // draft revision cannot honor (so sessionless draft requests are refused). Stateless = true (the
            // default) serves sessionless draft natively.
            options.Stateless = stateless;
        });

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

    [Fact]
    public async Task DraftRequest_OnStatelessServer_Succeeds_WithoutMcpSessionIdHeader()
    {
        await StartAsync(stateless: true);

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");

        // On a stateless server, sessionless draft server/discover succeeds without creating a session.
        var content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{}}""",
            Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"), "Draft responses must not include Mcp-Session-Id");
    }

    [Fact]
    public async Task DraftRequest_OnStatefulServer_IsRefused_WithUnsupportedProtocolVersionError()
    {
        // The draft revision is sessionless (SEP-2567), so it cannot honor a server configured with
        // sessions (Stateless = false). The server refuses the draft version with
        // UnsupportedProtocolVersion (excluding draft from Supported) so a dual-era client falls back
        // to the legacy initialize handshake.
        await StartAsync(stateless: false);

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");

        var content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{}}""",
            Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var rpcMessage = JsonSerializer.Deserialize<JsonRpcMessage>(body, McpJsonUtilities.DefaultOptions);
        var rpcError = Assert.IsType<JsonRpcError>(rpcMessage);
        Assert.Equal((int)McpErrorCode.UnsupportedProtocolVersion, rpcError.Error.Code);

        var dataElement = (JsonElement)rpcError.Error.Data!;
        var errorData = dataElement.Deserialize<UnsupportedProtocolVersionErrorData>(McpJsonUtilities.DefaultOptions);
        Assert.NotNull(errorData);
        Assert.Equal(DraftVersion, errorData.Requested);
        // The draft version is excluded from Supported so the client downgrades to a legacy version.
        Assert.NotEmpty(errorData.Supported);
        Assert.DoesNotContain(DraftVersion, errorData.Supported);
    }

    [Fact]
    public async Task RequestWithUnsupportedProtocolVersion_Returns_UnsupportedProtocolVersionError()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2099-12-31");
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");

        var content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{}}""",
            Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var rpcMessage = JsonSerializer.Deserialize<JsonRpcMessage>(body, McpJsonUtilities.DefaultOptions);
        var rpcError = Assert.IsType<JsonRpcError>(rpcMessage);
        Assert.Equal((int)McpErrorCode.UnsupportedProtocolVersion, rpcError.Error.Code);

        // Validate the structured data payload (SEP-2575 §"Unsupported Protocol Versions").
        var dataElement = (JsonElement)rpcError.Error.Data!;
        var errorData = dataElement.Deserialize<UnsupportedProtocolVersionErrorData>(McpJsonUtilities.DefaultOptions);
        Assert.NotNull(errorData);
        Assert.Equal("2099-12-31", errorData.Requested);
        Assert.NotEmpty(errorData.Supported);
    }

    [Fact]
    public async Task DraftRequest_WithMcpSessionIdHeader_IsRejected()
    {
        // The draft revision is sessionless (SEP-2567): a draft request carrying an Mcp-Session-Id is
        // non-conformant and is rejected with 400 regardless of the Stateless setting.
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");
        HttpClient.DefaultRequestHeaders.Add("Mcp-Session-Id", "non-existent-session-id");

        var content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{}}""",
            Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DraftGet_WithoutSessionId_IsRejected()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);

        using var response = await HttpClient.GetAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DraftGet_WithSessionId_IsRejected()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Session-Id", "non-existent-session-id");

        using var response = await HttpClient.GetAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DraftDelete_WithoutSessionId_IsRejected()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);

        using var response = await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DraftDelete_WithSessionId_IsRejected()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Session-Id", "non-existent-session-id");

        using var response = await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
