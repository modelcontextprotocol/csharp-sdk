﻿using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils.Json;
using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Tests;

public class StreamableHttpTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private static McpServerTool[] Tools { get; } = [
        McpServerTool.Create(EchoAsync),
        McpServerTool.Create(LongRunningAsync),
        McpServerTool.Create(Progress),
        McpServerTool.Create(Throw),
    ];

    private WebApplication? _app;

    private async Task StartAsync()
    {
        AddDefaultHttpClientRequestHeaders();

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(StreamableHttpTests),
                Version = "73",
            };
        }).WithTools(Tools).WithHttpTransport();

        _app = Builder.Build();

        _app.MapMcp();

        await _app.StartAsync(TestContext.Current.CancellationToken);
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
    public async Task InitialPostResponse_Includes_McpSessionIdHeader()
    {
        await StartAsync();

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(response.Headers.GetValues("mcp-session-id"));
        Assert.Equal("text/event-stream", Assert.Single(response.Content.Headers.GetValues("content-type")));
    }

    [Fact]
    public async Task PostRequest_IsUnsupportedMediaType_WithoutJsonContentType()
    {
        await StartAsync();

        using var response = await HttpClient.PostAsync("", new StringContent(InitializeRequest, Encoding.UTF8, "text/javascript"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task PostRequest_IsNotAcceptable_WithoutApplicationJsonAcceptHeader()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }


    [Fact]
    public async Task PostRequest_IsNotAcceptable_WithoutTextEventStreamAcceptHeader()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/json"));

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }

    [Fact]
    public async Task GetRequest_IsNotAcceptable_WithoutTextEventStreamAcceptHeader()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/json"));

        using var response = await HttpClient.GetAsync("", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }

    [Fact]
    public async Task PostRequest_IsNotFound_WithUnrecognizedSessionId()
    {
        await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = JsonContent(EchoRequest),
            Headers =
            {
                { "mcp-session-id", "fakeSession" },
            },
        };
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InitializeRequest_Matches_CustomRoute()
    {
        AddDefaultHttpClientRequestHeaders();
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();

        app.MapMcp("/custom-route");

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await HttpClient.PostAsync("/custom-route", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostWithSingleNotification_IsAccepted_WithEmptyResponse()
    {
        await StartAsync();
        await CallInitializeAndValidateAsync();

        var response = await HttpClient.PostAsync("", JsonContent(ProgressNotification("1")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeJsonRpcRequest_IsHandled_WithCompleteSseResponse()
    {
        await StartAsync();
        await CallInitializeAndValidateAsync();
    }

    [Fact]
    public async Task BatchedJsonRpcRequests_IsHandled_WithCompleteSseResponse()
    {
        await StartAsync();

        using var response = await HttpClient.PostAsync("", JsonContent($"[{InitializeRequest},{EchoRequest}]"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var eventCount = 0;
        await foreach (var sseEvent in ReadSseAsync(response.Content))
        {
            var jsonRpcResponse = JsonSerializer.Deserialize(sseEvent, GetJsonTypeInfo<JsonRpcResponse>());
            Assert.NotNull(jsonRpcResponse);
            var responseId = Assert.IsType<long>(jsonRpcResponse.Id.Id);

            switch (responseId)
            {
                case 1:
                    AssertServerInfo(jsonRpcResponse);
                    break;
                case 2:
                    AssertEchoResponse(jsonRpcResponse);
                    break;
                default:
                    throw new Exception($"Unexpected response ID: {jsonRpcResponse.Id}");
            }

            eventCount++;
        }

        Assert.Equal(2, eventCount);
    }

    [Fact]
    public async Task SingleJsonRpcRequest_ThatThrowsIsHandled_WithCompleteSseResponse()
    {
        await StartAsync();
        await CallInitializeAndValidateAsync();

        var response = await HttpClient.PostAsync("", JsonContent(CallTool("throw")), TestContext.Current.CancellationToken);
        var rpcError = await AssertSingleSseResponseAsync(response);

        var error = AssertType<CallToolResponse>(rpcError.Result);
        var content = Assert.Single(error.Content);
        Assert.Contains("'throw'", content.Text);
    }

    [Fact]
    public async Task MultipleSerialJsonRpcRequests_IsHandled_OneAtATime()
    {
        await StartAsync();

        await CallInitializeAndValidateAsync();
        await CallEchoAndValidateAsync();
        await CallEchoAndValidateAsync();
    }

    [Fact]
    public async Task MultipleConcurrentJsonRpcRequests_IsHandled_InParallel()
    {
        await StartAsync();

        await CallInitializeAndValidateAsync();

        var echoTasks = new Task[100];
        for (int i = 0; i < echoTasks.Length; i++)
        {
            echoTasks[i] = CallEchoAndValidateAsync();
        }

        await Task.WhenAll(echoTasks);
    }

    [Fact]
    public async Task GetRequest_Receives_UnsolicitedNotifications()
    {
        IMcpServer? server = null;

        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.RunSessionHandler = (httpContext, mcpServer, cancellationToken) =>
                {
                    server = mcpServer;
                    return mcpServer.RunAsync(cancellationToken);
                };
            });

        await StartAsync();

        await CallInitializeAndValidateAsync();
        Assert.NotNull(server);

        // Headers should be sent even before any messages are ready on the GET endpoint.
        using var getResponse = await HttpClient.GetAsync("", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        async Task<string> GetFirstNotificationAsync()
        {
            await foreach (var sseEvent in ReadSseAsync(getResponse.Content))
            {
                var notification = JsonSerializer.Deserialize(sseEvent, GetJsonTypeInfo<JsonRpcNotification>());
                Assert.NotNull(notification);
                return notification.Method;
            }

            throw new Exception("No notifications received.");
        }

        await server.SendNotificationAsync("test-method", TestContext.Current.CancellationToken);
        Assert.Equal("test-method", await GetFirstNotificationAsync());
    }

    [Fact]
    public async Task SecondGetRequests_IsRejected_AsBadRequest()
    {
        await StartAsync();

        await CallInitializeAndValidateAsync();
        using var getResponse1 = await HttpClient.GetAsync("", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        using var getResponse2 = await HttpClient.GetAsync("", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, getResponse1.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, getResponse2.StatusCode);
    }

    [Fact]
    public async Task DeleteRequest_CompletesSession_WhichIsNoLongerFound()
    {
        await StartAsync();

        await CallInitializeAndValidateAsync();
        await CallEchoAndValidateAsync();
        await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);

        using var response = await HttpClient.PostAsync("", JsonContent(EchoRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRequest_CompletesSession_WhichCancelsLongRunningToolCalls()
    {
        await StartAsync();

        await CallInitializeAndValidateAsync();

        Task<HttpResponseMessage> CallLongRunningToolAsync() =>
            HttpClient.PostAsync("", JsonContent(CallTool("long-running")), TestContext.Current.CancellationToken);

        var longRunningToolTasks = new Task<HttpResponseMessage>[10];
        for (int i = 0; i < longRunningToolTasks.Length; i++)
        {
            longRunningToolTasks[i] = CallLongRunningToolAsync();
            Assert.False(longRunningToolTasks[i].IsCompleted);
        }
        await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);

        // Currently, the OCE thrown by the canceled session is unhandled and turned into a 500 error by Kestrel.
        // The spec suggests sending CancelledNotifications. That would be good, but we can do that later.
        // For now, the important thing is that request completes without indicating success.
        await Task.WhenAll(longRunningToolTasks);
        foreach (var task in longRunningToolTasks)
        {
            var response = await task;
            Assert.False(response.IsSuccessStatusCode);
        }
    }

    [Fact]
    public async Task Progress_IsReported_InSameSseResponseAsRpcResponse()
    {
        await StartAsync();

        await CallInitializeAndValidateAsync();

        using var response = await HttpClient.PostAsync("", JsonContent(CallToolWithProgressToken("progress")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var currentSseItem = 0;
        await foreach (var sseEvent in ReadSseAsync(response.Content))
        {
            currentSseItem++;

            if (currentSseItem <= 10)
            {
                var notification = JsonSerializer.Deserialize(sseEvent, GetJsonTypeInfo<JsonRpcNotification>());
                var progressNotification = AssertType<ProgressNotification>(notification?.Params);
                Assert.Equal($"Progress {currentSseItem - 1}", progressNotification.Progress.Message);
            }
            else
            {
                var rpcResponse = JsonSerializer.Deserialize(sseEvent, GetJsonTypeInfo<JsonRpcResponse>());
                var callToolResponse = AssertType<CallToolResponse>(rpcResponse?.Result);
                var callToolContent = Assert.Single(callToolResponse.Content);
                Assert.Equal("text", callToolContent.Type);
                Assert.Equal("done", callToolContent.Text);
            }
        }

        Assert.Equal(11, currentSseItem);
    }

    private void AddDefaultHttpClientRequestHeaders()
    {
        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
    }

    private static StringContent JsonContent(string json) => new StringContent(json, Encoding.UTF8, "application/json");
    private static JsonTypeInfo<T> GetJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private static T AssertType<T>(JsonNode? jsonNode)
    {
        var type = JsonSerializer.Deserialize<T>(jsonNode, GetJsonTypeInfo<T>());
        Assert.NotNull(type);
        return type;
    }

    private static async IAsyncEnumerable<string> ReadSseAsync(HttpContent responseContent)
    {
        var responseStream = await responseContent.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var sseItem in SseParser.Create(responseStream).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal("message", sseItem.EventType);
            yield return sseItem.Data;
        }
    }

    private static async Task<JsonRpcResponse> AssertSingleSseResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var sseItem = Assert.Single(await ReadSseAsync(response.Content).ToListAsync(TestContext.Current.CancellationToken));
        var jsonRpcResponse = JsonSerializer.Deserialize(sseItem, GetJsonTypeInfo<JsonRpcResponse>());

        Assert.NotNull(jsonRpcResponse);
        return jsonRpcResponse;
    }

    private static string InitializeRequest => """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"IntegrationTestClient","version":"1.0.0"}}}
        """;

    private long _lastRequestId = 1;
    private string EchoRequest
    {
        get
        {
            var id = Interlocked.Increment(ref _lastRequestId);
            return $$$$"""
                {"jsonrpc":"2.0","id":{{{{id}}}},"method":"tools/call","params":{"name":"echo","arguments":{"message":"Hello world! ({{{{id}}}})"}}}
                """;
        }
    }

    private string ProgressNotification(string progress)
    {
        return $$$"""
            {"jsonrpc":"2.0","method":"notifications/progress","params":{"progressToken":"","progress":{{{progress}}}}}
            """;
    }

    private string Request(string method, string parameters = "{}")
    {
        var id = Interlocked.Increment(ref _lastRequestId);
        return $$"""
            {"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{parameters}}}
            """;
    }

    private string CallTool(string toolName, string arguments = "{}") =>
        Request("tools/call", $$"""
            {"name":"{{toolName}}","arguments":{{arguments}}}
            """);

    private string CallToolWithProgressToken(string toolName, string arguments = "{}") =>
        Request("tools/call", $$$"""
            {"name":"{{{toolName}}}","arguments":{{{arguments}}}, "_meta":{"progressToken": "abc123"}}
            """);

    private static InitializeResult AssertServerInfo(JsonRpcResponse rpcResponse)
    {
        var initializeResult = AssertType<InitializeResult>(rpcResponse.Result);
        Assert.Equal(nameof(StreamableHttpTests), initializeResult.ServerInfo.Name);
        Assert.Equal("73", initializeResult.ServerInfo.Version);
        return initializeResult;
    }

    private static CallToolResponse AssertEchoResponse(JsonRpcResponse rpcResponse)
    {
        var callToolResponse = AssertType<CallToolResponse>(rpcResponse.Result);
        var callToolContent = Assert.Single(callToolResponse.Content);
        Assert.Equal("text", callToolContent.Type);
        Assert.Equal($"Hello world! ({rpcResponse.Id})", callToolContent.Text);
        return callToolResponse;
    }

    private async Task CallInitializeAndValidateAsync()
    {
        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        AssertServerInfo(rpcResponse);

        var sessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);
    }

    private async Task CallEchoAndValidateAsync()
    {
        using var response = await HttpClient.PostAsync("", JsonContent(EchoRequest), TestContext.Current.CancellationToken);
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        AssertEchoResponse(rpcResponse);
    }

    [McpServerTool(Name = "echo")]
    private static async Task<string> EchoAsync(string message)
    {
        // McpSession.ProcessMessagesAsync() already yields before calling any handlers, but this makes it even
        // more explicit that we're not relying on synchronous execution of the tool.
        await Task.Yield();
        return message;
    }

    [McpServerTool(Name = "long-running")]
    private static async Task LongRunningAsync(CancellationToken cancellation)
    {
        // McpSession.ProcessMessagesAsync() already yields before calling any handlers, but this makes it even
        // more explicit that we're not relying on synchronous execution of the tool.
        await Task.Delay(Timeout.Infinite, cancellation);
    }

    [McpServerTool(Name = "progress")]
    public static string Progress(IProgress<ProgressNotificationValue> progress)
    {
        for (int i = 0; i < 10; i++)
        {
            progress.Report(new() { Progress = i, Total = 10, Message = $"Progress {i}" });
        }

        return "done";
    }

    [McpServerTool(Name = "throw")]
    private static void Throw()
    {
        throw new Exception();
    }
}
