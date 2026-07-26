using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// HTTP-level tests for the 2026-07-28 protocol revision (SEP-2575 + SEP-2567): verify that the server
/// does not issue <c>Mcp-Session-Id</c> for those requests and returns structured
/// <see cref="McpErrorCode.UnsupportedProtocolVersion"/> errors instead of plain 400s.
/// </summary>
public class July2026ProtocolHttpHandlerTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    private async Task StartAsync(bool stateless = false)
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = nameof(July2026ProtocolHttpHandlerTests), Version = "1" };
        }).WithHttpTransport(options =>
        {
            // Stateless = false maps the GET/DELETE endpoints and opts the author into sessions. Starting with
            // the 2026-07-28 protocol revision, Streamable HTTP no longer supports sessions, so such a request is
            // refused on a session-enabled server. Stateless = true (the default) serves them natively.
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
    public async Task Request_OnStatelessServer_Succeeds_WithoutMcpSessionIdHeader()
    {
        await StartAsync(stateless: true);

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");

        // On a stateless server, server/discover succeeds without creating a session.
        var content = new StringContent(
            DiscoverRequestJuly2026Protocol,
            Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"), "Responses on the 2026-07-28 revision must not include Mcp-Session-Id");
    }

    [Fact]
    public async Task Request_OnStatefulServer_IsRefused_WithUnsupportedProtocolVersionError()
    {
        // Starting with the 2026-07-28 protocol revision, Streamable HTTP no longer supports sessions (SEP-2567),
        // so the server cannot honor it when configured with sessions (Stateless = false). The server refuses that
        // version with UnsupportedProtocolVersion (excluding it from Supported) so a dual-path client falls back
        // to the initialize handshake.
        await StartAsync(stateless: false);

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");

        var content = new StringContent(
            DiscoverRequestJuly2026Protocol,
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
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, errorData.Requested);
        // The 2026-07-28 protocol version is excluded from Supported so the client downgrades to an initialize-capable version.
        Assert.NotEmpty(errorData.Supported);
        Assert.DoesNotContain(McpProtocolVersions.July2026ProtocolVersion, errorData.Supported);
    }

    [Fact]
    public async Task RequestWithUnsupportedProtocolVersion_Returns_UnsupportedProtocolVersionError()
    {
        await StartAsync(stateless: true);

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2099-12-31");
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");

        var content = new StringContent(
            DiscoverRequestJuly2026Protocol,
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
    public async Task Request_WithMcpSessionIdHeader_IgnoresHeader_AndDoesNotEchoSessionId()
    {
        // Starting with the 2026-07-28 protocol revision, Streamable HTTP no longer supports sessions (SEP-2567):
        // a request carrying an Mcp-Session-Id is ignored, and the server must not mint or echo session IDs.
        await StartAsync(stateless: true);

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");
        HttpClient.DefaultRequestHeaders.Add("Mcp-Session-Id", "non-existent-session-id");

        var content = new StringContent(
            DiscoverRequestJuly2026Protocol,
            Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));
    }

    [Fact]
    public async Task Get_WithoutSessionId_IsRejected()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);

        using var response = await HttpClient.GetAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["POST"], response.Content.Headers.Allow);
    }

    [Fact]
    public async Task Get_WithSessionId_IsRejected()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Session-Id", "non-existent-session-id");

        using var response = await HttpClient.GetAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["POST"], response.Content.Headers.Allow);
    }

    [Fact]
    public async Task Delete_WithoutSessionId_IsRejected()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);

        using var response = await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["POST"], response.Content.Headers.Allow);
    }

    [Fact]
    public async Task Delete_WithSessionId_IsRejected()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Session-Id", "non-existent-session-id");

        using var response = await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["POST"], response.Content.Headers.Allow);
    }

    private static string DiscoverRequestJuly2026Protocol => """
        {"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"July2026HttpHandlerTestClient","version":"1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}}
        """;
}
