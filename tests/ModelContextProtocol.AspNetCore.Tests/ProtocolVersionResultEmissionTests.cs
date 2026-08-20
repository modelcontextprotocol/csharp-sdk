using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.AspNetCore.Tests;

public sealed class ProtocolVersionResultEmissionTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private readonly ListToolsResult _sharedResult = new()
    {
        ResultType = "shared",
        TimeToLive = TimeSpan.FromMilliseconds(4_321),
        CacheScope = CacheScope.Public,
    };
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
    public async Task SharedResult_UsesPerRequestAndSessionProtocolsWithoutMutation()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "result-emission-http-test", Version = "1" };
            options.Handlers.ListToolsHandler = (_, _) => new(_sharedResult);
        })
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        using var modernResponse = await SendModernListToolsAsync();
        Assert.Equal(HttpStatusCode.OK, modernResponse.StatusCode);
        var modernResult = (await ReadJsonResponseAsync(modernResponse))["result"]!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""
            {
              "resultType": "shared",
              "tools": [],
              "ttlMs": 4321,
              "cacheScope": "public",
              "_meta": {
                "io.modelcontextprotocol/serverInfo": {
                  "name": "result-emission-http-test",
                  "version": "1"
                }
              }
            }
            """), modernResult), $"Unexpected modern result: {modernResult}");

        using var initializeResponse = await SendAsync(InitializeRequest);
        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        var sessionId = Assert.Single(initializeResponse.Headers.GetValues("Mcp-Session-Id"));
        using var initializedResponse = await SendAsync(InitializedNotification, sessionId);
        Assert.Equal(HttpStatusCode.Accepted, initializedResponse.StatusCode);
        using var legacyResponse = await SendAsync(ListToolsRequest, sessionId);
        Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
        var legacyResult = (await ReadJsonResponseAsync(legacyResponse))["result"]!.AsObject();
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["tools"] = new JsonArray() }, legacyResult));

        Assert.Equal("shared", _sharedResult.ResultType);
        Assert.Equal(TimeSpan.FromMilliseconds(4_321), _sharedResult.TimeToLive);
        Assert.Equal(CacheScope.Public, _sharedResult.CacheScope);
    }

    [Fact]
    public async Task SharedResult_ConcurrentLegacyAndModernRequestsRemainIsolated()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "result-emission-http-test", Version = "1" };
            options.Handlers.ListToolsHandler = (_, _) => new(_sharedResult);
        })
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        using var initializeResponse = await SendAsync(InitializeRequest);
        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        var sessionId = Assert.Single(initializeResponse.Headers.GetValues("Mcp-Session-Id"));
        using var initializedResponse = await SendAsync(InitializedNotification, sessionId);
        Assert.Equal(HttpStatusCode.Accepted, initializedResponse.StatusCode);

        var modernTasks = Enumerable.Range(0, 8).Select(async i =>
        {
            using var response = await SendModernListToolsAsync(id: 100 + i);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await ReadJsonResponseAsync(response))["result"]!;
        }).ToArray();
        var legacyTasks = Enumerable.Range(0, 8).Select(async i =>
        {
            using var response = await SendAsync(
                ListToolsRequest.Replace("\"id\":2", $"\"id\":{200 + i}", StringComparison.Ordinal),
                sessionId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await ReadJsonResponseAsync(response))["result"]!;
        }).ToArray();

        var results = await Task.WhenAll(modernTasks.Concat(legacyTasks));
        var expectedModern = JsonNode.Parse("""
            {
              "resultType": "shared",
              "tools": [],
              "ttlMs": 4321,
              "cacheScope": "public",
              "_meta": {
                "io.modelcontextprotocol/serverInfo": {
                  "name": "result-emission-http-test",
                  "version": "1"
                }
              }
            }
            """);
        var expectedLegacy = new JsonObject { ["tools"] = new JsonArray() };

        Assert.All(results[..modernTasks.Length], result =>
            Assert.True(JsonNode.DeepEquals(expectedModern, result), $"Unexpected modern result: {result}"));
        Assert.All(results[modernTasks.Length..], result =>
            Assert.True(JsonNode.DeepEquals(expectedLegacy, result), $"Unexpected legacy result: {result}"));
        Assert.Single(MockLoggerProvider.LogMessages, message =>
            message.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning &&
            message.Message.Contains("protocol-incompatible result field", StringComparison.Ordinal) &&
            message.Message.Contains(RequestMethods.ToolsList, StringComparison.Ordinal));

        Assert.Equal("shared", _sharedResult.ResultType);
        Assert.Equal(TimeSpan.FromMilliseconds(4_321), _sharedResult.TimeToLive);
        Assert.Equal(CacheScope.Public, _sharedResult.CacheScope);
    }

    private Task<HttpResponseMessage> SendModernListToolsAsync(int id = 3)
    {
        var request = CreateRequest(
            ListToolsModernRequest.Replace("\"id\":3", $"\"id\":{id}", StringComparison.Ordinal));
        request.Headers.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", RequestMethods.ToolsList);
        return HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> SendAsync(string json, string? sessionId = null)
    {
        var request = CreateRequest(json);
        if (sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
            request.Headers.Add("MCP-Protocol-Version", McpProtocolVersions.November2025ProtocolVersion);
        }
        return HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static HttpRequestMessage CreateRequest(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    private static async Task<JsonNode> ReadJsonResponseAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        }

        var responseStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var item in SseParser.Create(responseStream).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            if (item.EventType == "message")
            {
                return JsonNode.Parse(item.Data)!;
            }
        }

        throw new InvalidOperationException("SSE response did not contain a message event.");
    }

    private const string InitializeRequest = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"result-emission-test","version":"1"}}}
        """;

    private const string ListToolsRequest = """
        {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
        """;

    private const string InitializedNotification = """
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """;

    private const string ListToolsModernRequest = """
        {"jsonrpc":"2.0","id":3,"method":"tools/list","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"result-emission-test","version":"1"},"io.modelcontextprotocol/clientCapabilities":{}}}}
        """;
}
