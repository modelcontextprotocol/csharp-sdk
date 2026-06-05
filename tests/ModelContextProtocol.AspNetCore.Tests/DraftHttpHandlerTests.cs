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

    private async Task StartAsync()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = nameof(DraftHttpHandlerTests), Version = "1" };
        }).WithHttpTransport(options =>
        {
            // Map the GET/DELETE endpoints so we can exercise the draft-mode rejection paths
            // (these endpoints are not registered in stateless mode, which is the new default).
            options.Stateless = false;
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
    public async Task DraftRequest_DoesNotEmitMcpSessionIdHeader()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");

        // server/discover should succeed without creating a session.
        var content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{}}""",
            Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"), "Draft responses must not include Mcp-Session-Id");
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
    public async Task DraftRequest_WithMcpSessionIdHeader_RoutesThroughLegacyPath()
    {
        // For back-compat with clients that opted into the experimental version on top of the legacy
        // stateful session model (MRTR-as-extension-on-initialize), draft-version requests that DO
        // include an Mcp-Session-Id are still accepted via the legacy session lookup path.
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);
        HttpClient.DefaultRequestHeaders.Add("Mcp-Method", "server/discover");
        HttpClient.DefaultRequestHeaders.Add("Mcp-Session-Id", "non-existent-session-id");

        var content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{}}""",
            Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);

        // Legacy path returns 404 for unknown sessions.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
    public async Task DraftDelete_WithoutSessionId_IsRejected()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", DraftVersion);

        using var response = await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
