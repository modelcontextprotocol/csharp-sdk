using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Tests;

public class SessionMigrationTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private static McpServerTool[] Tools { get; } = [McpServerTool.Create(EchoAsync), McpServerTool.Create(GetClientInfoAsync)];

    private WebApplication? _app;

    private static string InitializeRequest => """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"IntegrationTestClient","version":"1.0.0"}}}
        """;

    private long _lastRequestId = 1;
    private string MakeEchoRequest()
    {
        var id = Interlocked.Increment(ref _lastRequestId);
        return $$$$"""
            {"jsonrpc":"2.0","id":{{{{id}}}},"method":"tools/call","params":{"name":"echo","arguments":{"message":"Hello world! ({{{{id}}}})"}}}
            """;
    }

    [Fact]
    public async Task OnSessionInitializedAsync_IsCalled_AfterInitializeHandshake()
    {
        InitializeRequestParams? capturedParams = null;
        string? capturedSessionId = null;

        var handler = new TestMigrationHandler
        {
            OnInitialized = (context, sessionId, initParams, ct) =>
            {
                capturedSessionId = sessionId;
                capturedParams = initParams;
                return default;
            },
        };

        await StartAsync(handler);

        var sessionId = await CallInitializeAndValidateAsync();

        Assert.NotNull(capturedParams);
        Assert.Equal(sessionId, capturedSessionId);
        Assert.Equal("IntegrationTestClient", capturedParams.ClientInfo.Name);
        Assert.Equal("1.0.0", capturedParams.ClientInfo.Version);
        Assert.NotNull(capturedParams.Capabilities);
    }

    [Fact]
    public async Task AllowSessionMigrationAsync_IsCalled_WhenSessionNotFound()
    {
        string? requestedSessionId = null;

        var handler = new TestMigrationHandler
        {
            OnInitialized = (_, _, _, _) => default,
            OnMigration = (context, sessionId, ct) =>
            {
                requestedSessionId = sessionId;
                return new ValueTask<InitializeRequestParams?>(new InitializeRequestParams
                {
                    ProtocolVersion = "2025-03-26",
                    Capabilities = new ClientCapabilities(),
                    ClientInfo = new Implementation { Name = "MigratedClient", Version = "2.0.0" },
                });
            },
        };

        await StartAsync(handler);

        // Send a request with a fake session ID that the server doesn't know about.
        SetSessionId("migratable-session-id");
        await CallEchoAndValidateAsync();

        Assert.Equal("migratable-session-id", requestedSessionId);

        // Verify the migrated client info was applied to the session.
        var clientInfo = await CallGetClientInfoAsync();
        Assert.NotNull(clientInfo);
        Assert.Equal("MigratedClient", clientInfo.Name);
        Assert.Equal("2.0.0", clientInfo.Version);
    }

    [Fact]
    public async Task MigratedSession_PreservesSessionId()
    {
        var handler = new TestMigrationHandler
        {
            OnInitialized = (_, _, _, _) => default,
            OnMigration = (context, sessionId, ct) =>
            {
                return new ValueTask<InitializeRequestParams?>(new InitializeRequestParams
                {
                    ProtocolVersion = "2025-03-26",
                    Capabilities = new ClientCapabilities(),
                    ClientInfo = new Implementation { Name = "MigratedClient", Version = "2.0.0" },
                });
            },
        };

        await StartAsync(handler);

        const string OriginalSessionId = "preserved-session-id";
        SetSessionId(OriginalSessionId);

        using var response = await HttpClient.PostAsync("", JsonContent(MakeEchoRequest()), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The response should echo back the same session ID.
        var returnedSessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        Assert.Equal(OriginalSessionId, returnedSessionId);
    }

    [Fact]
    public async Task MigratedSession_CanHandleSubsequentRequests()
    {
        var migrationCount = 0;
        var handler = new TestMigrationHandler
        {
            OnInitialized = (_, _, _, _) => default,
            OnMigration = (context, sessionId, ct) =>
            {
                Interlocked.Increment(ref migrationCount);
                return new ValueTask<InitializeRequestParams?>(new InitializeRequestParams
                {
                    ProtocolVersion = "2025-03-26",
                    Capabilities = new ClientCapabilities(),
                    ClientInfo = new Implementation { Name = "MigratedClient", Version = "2.0.0" },
                });
            },
        };

        await StartAsync(handler);

        SetSessionId("multi-request-session");

        // First request triggers migration
        await CallEchoAndValidateAsync();

        // Second request should use the now-local session without triggering another migration.
        await CallEchoAndValidateAsync();

        Assert.Equal(1, migrationCount);
    }

    [Fact]
    public async Task AllowSessionMigrationAsync_ReturnsNull_ResultsIn404()
    {
        var handler = new TestMigrationHandler
        {
            OnInitialized = (_, _, _, _) => default,
            OnMigration = (context, sessionId, ct) =>
                new ValueTask<InitializeRequestParams?>((InitializeRequestParams?)null),
        };

        await StartAsync(handler);

        SetSessionId("non-migratable-session");

        using var response = await HttpClient.PostAsync("", JsonContent(MakeEchoRequest()), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoMigrationHandler_UnknownSession_Returns404()
    {
        // Start without any migration handler — backward compatibility.
        await StartAsync(migrationHandler: null);

        SetSessionId("unknown-session");

        using var response = await HttpClient.PostAsync("", JsonContent(MakeEchoRequest()), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRequest_WithMigratedSession_Works()
    {
        var handler = new TestMigrationHandler
        {
            OnInitialized = (_, _, _, _) => default,
            OnMigration = (context, sessionId, ct) =>
            {
                return new ValueTask<InitializeRequestParams?>(new InitializeRequestParams
                {
                    ProtocolVersion = "2025-03-26",
                    Capabilities = new ClientCapabilities(),
                    ClientInfo = new Implementation { Name = "MigratedClient", Version = "2.0.0" },
                });
            },
        };

        await StartAsync(handler);

        // Migrate session via POST first
        SetSessionId("get-test-session");
        await CallEchoAndValidateAsync();

        // Now the GET request should work with the migrated session
        using var getResponse = await HttpClient.GetAsync("", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    private async Task StartAsync(ISessionMigrationHandler? migrationHandler = null)
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "SessionMigrationTestServer",
                Version = "1.0.0",
            };
        }).WithTools(Tools).WithHttpTransport();

        if (migrationHandler is not null)
        {
            Builder.Services.AddSingleton(migrationHandler);
        }

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
    private static JsonTypeInfo<T> GetJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private async Task<string> CallInitializeAndValidateAsync()
    {
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var sessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        SetSessionId(sessionId);
        return sessionId;
    }

    private void SetSessionId(string sessionId)
    {
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);
    }

    private async Task CallEchoAndValidateAsync()
    {
        using var response = await HttpClient.PostAsync("", JsonContent(MakeEchoRequest()), TestContext.Current.CancellationToken);
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var callToolResult = JsonSerializer.Deserialize(rpcResponse.Result, GetJsonTypeInfo<CallToolResult>());
        Assert.NotNull(callToolResult);
        var content = Assert.Single(callToolResult.Content);
        Assert.IsType<TextContentBlock>(content);
    }

    private async Task<Implementation?> CallGetClientInfoAsync()
    {
        var id = Interlocked.Increment(ref _lastRequestId);
        var request = $$$$"""
            {"jsonrpc":"2.0","id":{{{{id}}}},"method":"tools/call","params":{"name":"getClientInfo","arguments":{}}}
            """;

        using var response = await HttpClient.PostAsync("", JsonContent(request), TestContext.Current.CancellationToken);
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var callToolResult = JsonSerializer.Deserialize(rpcResponse.Result, GetJsonTypeInfo<CallToolResult>());
        Assert.NotNull(callToolResult);
        var textContent = Assert.IsType<TextContentBlock>(Assert.Single(callToolResult.Content));
        return JsonSerializer.Deserialize(textContent.Text, GetJsonTypeInfo<Implementation>());
    }

    private static async Task<JsonRpcResponse> AssertSingleSseResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var sseItems = new List<string>();
        var responseStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var sseItem in SseParser.Create(responseStream).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            if (sseItem.EventType == "message")
            {
                sseItems.Add(sseItem.Data);
            }
        }

        var data = Assert.Single(sseItems);
        var jsonRpcResponse = JsonSerializer.Deserialize(data, GetJsonTypeInfo<JsonRpcResponse>());
        Assert.NotNull(jsonRpcResponse);
        return jsonRpcResponse;
    }

    [McpServerTool(Name = "echo")]
    private static async Task<string> EchoAsync(string message)
    {
        await Task.Yield();
        return message;
    }

    [McpServerTool(Name = "getClientInfo")]
    private static string GetClientInfoAsync(McpServer server)
    {
        return JsonSerializer.Serialize(server.ClientInfo!, GetJsonTypeInfo<Implementation>());
    }

    private sealed class TestMigrationHandler : ISessionMigrationHandler
    {
        public Func<HttpContext, string, InitializeRequestParams, CancellationToken, ValueTask>? OnInitialized { get; set; }
        public Func<HttpContext, string, CancellationToken, ValueTask<InitializeRequestParams?>>? OnMigration { get; set; }

        public ValueTask OnSessionInitializedAsync(HttpContext context, string sessionId, InitializeRequestParams initializeParams, CancellationToken cancellationToken)
            => OnInitialized?.Invoke(context, sessionId, initializeParams, cancellationToken) ?? default;

        public ValueTask<InitializeRequestParams?> AllowSessionMigrationAsync(HttpContext context, string sessionId, CancellationToken cancellationToken)
            => OnMigration?.Invoke(context, sessionId, cancellationToken) ?? new ValueTask<InitializeRequestParams?>((InitializeRequestParams?)null);
    }
}
