using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Tests;

public class JsonResponseModeTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        base.Dispose();
    }

    [Fact]
    public async Task JsonResponseMode_ReturnsRawJsonRpcResponse()
    {
        await StartAsync(enableJsonResponse: true);

        using var response = await SendAsync(InitializeRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.CacheControl);
        Assert.False(response.Headers.Contains("X-Accel-Buffering"));

        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("event:", responseBody, StringComparison.Ordinal);

        var rpcResponse = JsonSerializer.Deserialize(responseBody, GetJsonTypeInfo<JsonRpcMessage>());
        Assert.IsType<JsonRpcResponse>(rpcResponse);
        Assert.Equal(new RequestId(1), ((JsonRpcResponse)rpcResponse).Id);
    }

    [Fact]
    public async Task JsonResponseMode_SuppressesProgressAndReturnsToolResult()
    {
        await StartAsync(enableJsonResponse: true);
        var sessionId = await InitializeSessionAsync();

        using var response = await SendAsync(CallProgressToolRequest, sessionId, "2025-03-26");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("notifications/progress", responseBody, StringComparison.Ordinal);

        var rpcMessage = JsonSerializer.Deserialize(responseBody, GetJsonTypeInfo<JsonRpcMessage>());
        var rpcResponse = Assert.IsType<JsonRpcResponse>(rpcMessage);
        var result = JsonSerializer.Deserialize(rpcResponse.Result, GetJsonTypeInfo<CallToolResult>());
        Assert.NotNull(result);
        var content = Assert.Single(result.Content);
        Assert.Equal("done", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task JsonResponseMode_NotificationReturnsEmptyAcceptedResponse()
    {
        await StartAsync(enableJsonResponse: true);
        var sessionId = await InitializeSessionAsync();

        using var response = await SendAsync(InitializedNotification, sessionId, "2025-03-26");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task JsonResponseMode_RejectsPolling()
    {
        InvalidOperationException? pollingException = null;
        var pollingTool = McpServerTool.Create(async (RequestContext<CallToolRequestParams> context) =>
        {
            try
            {
                await context.EnablePollingAsync(TimeSpan.FromSeconds(1));
            }
            catch (InvalidOperationException ex)
            {
                pollingException = ex;
            }

            return "done";
        }, new() { Name = "polling" });

        await StartAsync(enableJsonResponse: true, tools: [pollingTool]);
        var sessionId = await InitializeSessionAsync();

        using var response = await SendAsync(CallPollingToolRequest, sessionId, "2025-03-26");
        response.EnsureSuccessStatusCode();

        Assert.NotNull(pollingException);
        Assert.Equal("Polling is not supported when JSON responses are enabled.", pollingException.Message);
    }

    [Fact]
    public async Task DefaultMode_StillReturnsSse()
    {
        await StartAsync(enableJsonResponse: false);

        using var response = await SendAsync(InitializeRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no", Assert.Single(response.Headers.GetValues("X-Accel-Buffering")));
    }

    private async Task StartAsync(bool enableJsonResponse, McpServerTool[]? tools = null)
    {
        tools ??=
        [
            McpServerTool.Create((IProgress<ProgressNotificationValue> progress) =>
            {
                progress.Report(new() { Progress = 1, Total = 1, Message = "working" });
                return "done";
            }, new() { Name = "progress" }),
        ];

        Builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = nameof(JsonResponseModeTests),
                    Version = "1.0.0",
                };
            })
            .WithHttpTransport(options =>
            {
                options.Stateless = false;
                options.EnableJsonResponse = enableJsonResponse;
            })
            .WithTools(tools);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
    }

    private async Task<string> InitializeSessionAsync()
    {
        using var response = await SendAsync(InitializeRequest);
        response.EnsureSuccessStatusCode();
        return Assert.Single(response.Headers.GetValues("mcp-session-id"));
    }

    private async Task<HttpResponseMessage> SendAsync(string json, string? sessionId = null, string? protocolVersion = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        if (sessionId is not null)
        {
            request.Headers.Add("mcp-session-id", sessionId);
        }

        if (protocolVersion is not null)
        {
            request.Headers.Add("mcp-protocol-version", protocolVersion);
        }

        return await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static JsonTypeInfo<T> GetJsonTypeInfo<T>() =>
        (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private const string InitializeRequest = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"JsonResponseModeTests","version":"1.0.0"}}}
        """;

    private const string InitializedNotification = """
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """;

    private const string CallProgressToolRequest = """
        {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"progress","arguments":{},"_meta":{"progressToken":"progress-token"}}}
        """;

    private const string CallPollingToolRequest = """
        {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"polling","arguments":{}}}
        """;
}
