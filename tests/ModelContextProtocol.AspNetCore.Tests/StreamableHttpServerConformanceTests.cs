using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Tests;

public class StreamableHttpServerConformanceTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
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
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(StreamableHttpServerConformanceTests),
                Version = "73",
            };
        }).WithTools(Tools).WithHttpTransport();

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
    public async Task NegativeNonInfiniteIdleTimeout_Throws_ArgumentOutOfRangeException()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(options =>
        {
            options.IdleTimeout = TimeSpan.MinValue;
        });

        var ex = await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(StartAsync);
        Assert.Contains("IdleTimeout", ex.Message);
    }

    [Fact]
    public async Task NegativeMaxIdleSessionCount_Throws_ArgumentOutOfRangeException()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(options =>
        {
            options.MaxIdleSessionCount = -1;
        });

        var ex = await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(StartAsync);
        Assert.Contains("MaxIdleSessionCount", ex.Message);
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
    public async Task SseResponse_Includes_XAccelBufferingHeader()
    {
        await StartAsync();

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no", Assert.Single(response.Headers.GetValues("X-Accel-Buffering")));
    }

    [Fact]
    public async Task PostRequest_IsUnsupportedMediaType_WithoutJsonContentType()
    {
        await StartAsync();

        using var response = await HttpClient.PostAsync("", new StringContent(InitializeRequest, Encoding.UTF8, "text/javascript"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("text/event-stream")]
    [InlineData("application/json")]
    [InlineData("application/json-text/event-stream")]
    public async Task PostRequest_IsNotAcceptable_WithSingleSpecificAcceptHeader(string singleAcceptValue)
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.TryAddWithoutValidation(HeaderNames.Accept, singleAcceptValue);

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }

    [Theory]
    [InlineData("*/*")]
    [InlineData("text/event-stream, application/json;q=0.9")]
    public async Task PostRequest_IsAcceptable_WithWildcardOrAddedQualityInAcceptHeader(string acceptHeaderValue)
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.TryAddWithoutValidation(HeaderNames.Accept, acceptHeaderValue);

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRequest_IsNotAcceptable_WithoutTextEventStreamAcceptHeader()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));

        using var response = await HttpClient.GetAsync("", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }

    [Theory]
    [InlineData("*/*")]
    [InlineData("application/json, text/event-stream;q=0.9")]
    public async Task GetRequest_IsAcceptable_WithWildcardOrAddedQualityInAcceptHeader(string acceptHeaderValue)
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.TryAddWithoutValidation(HeaderNames.Accept, acceptHeaderValue);

        await CallInitializeAndValidateAsync();

        using var response = await HttpClient.GetAsync("", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("invalid-version")]
    [InlineData("9999-01-01")]
    [InlineData("not-a-date")]
    public async Task PostRequest_IsBadRequest_WithInvalidProtocolVersionHeader(string invalidVersion)
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", invalidVersion);

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostRequest_Succeeds_WithoutProtocolVersionHeader()
    {
        await StartAsync();

        // No MCP-Protocol-Version header is set - this should be accepted for backwards compatibility
        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostRequest_Succeeds_WithValidProtocolVersionHeader()
    {
        await StartAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-03-26");

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRequest_IsBadRequest_WithInvalidProtocolVersionHeader()
    {
        await StartAsync();

        await CallInitializeAndValidateAsync();

        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "invalid-version");

        using var response = await HttpClient.GetAsync("", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
    public async Task PostWithoutSessionId_NonInitializeRequest_Returns400()
    {
        await StartAsync();

        using var response = await HttpClient.PostAsync("", JsonContent(ListToolsRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InitializeRequest_Matches_CustomRoute()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();

        app.MapMcp("/custom-route");

        await app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
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
    public async Task SingleJsonRpcRequest_ThatThrowsIsHandled_WithCompleteSseResponse()
    {
        await StartAsync();
        await CallInitializeAndValidateAsync();

        var response = await HttpClient.PostAsync("", JsonContent(CallTool("throw")), TestContext.Current.CancellationToken);
        var rpcError = await AssertSingleSseResponseAsync(response);

        var error = AssertType<CallToolResult>(rpcError.Result);
        var content = Assert.Single(error.Content);
        Assert.Contains("'throw'", Assert.IsType<TextContentBlock>(content).Text);
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
        McpServer? server = null;

        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
                options.RunSessionHandler = (httpContext, mcpServer, cancellationToken) =>
                {
                    server = mcpServer;
                    return mcpServer.RunAsync(cancellationToken);
                };
#pragma warning restore MCPEXP002
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
    public async Task SendNotificationAsync_DoesNotThrow_WhenNoGetRequestHasBeenMade()
    {
        // Clients are not required to make a GET request for unsolicited messages.
        // If no GET request has been made, the messages should be dropped rather than throwing.
        McpServer? server = null;

        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
                options.RunSessionHandler = (httpContext, mcpServer, cancellationToken) =>
                {
                    server = mcpServer;
                    return mcpServer.RunAsync(cancellationToken);
                };
#pragma warning restore MCPEXP002
            });

        await StartAsync();

        await CallInitializeAndValidateAsync();
        Assert.NotNull(server);

        // Calling SendNotificationAsync before a GET request should not throw.
        // The notification should be silently dropped.
        var exception = await Record.ExceptionAsync(() =>
            server.SendNotificationAsync("test-method", TestContext.Current.CancellationToken));
        Assert.Null(exception);
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
            HttpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "")
                {
                    Content = JsonContent(CallTool("long-running"))
                },
                HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken);

        var longRunningToolTasks = new Task<HttpResponseMessage>[10];
        for (int i = 0; i < longRunningToolTasks.Length; i++)
        {
            longRunningToolTasks[i] = CallLongRunningToolAsync();
        }

        var getResponse = await HttpClient.GetAsync("", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        // Wait for all long-running tool calls to receive 200 response headers before sending DELETE
        var responseHeaders = await Task.WhenAll(longRunningToolTasks);
        foreach (var response in responseHeaders)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Now send DELETE to cancel the session
        await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);

        // Get request should complete gracefully.
        var sseResponseBody = await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Empty(sseResponseBody);

        // Currently, responses are flushed immediately to prevent HttpClient timeouts for long-running requests.
        // This means the response starts with a 200 status code. When the session is canceled, Kestrel closes
        // the connection without writing the chunk terminator, causing an HttpRequestException when reading the response body.
        // The spec suggests sending CancelledNotifications. That would be good, but we can do that later.
        // For now, the important thing is that reading the response body fails.
        foreach (var response in responseHeaders)
        {
            await Assert.ThrowsAsync<HttpRequestException>(async () => await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
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
                var progressNotification = AssertType<ProgressNotificationParams>(notification?.Params);
                Assert.Equal($"Progress {currentSseItem - 1}", progressNotification.Progress.Message);
            }
            else
            {
                var rpcResponse = JsonSerializer.Deserialize(sseEvent, GetJsonTypeInfo<JsonRpcResponse>());
                var callToolResponse = AssertType<CallToolResult>(rpcResponse?.Result);
                var callToolContent = Assert.Single(callToolResponse.Content);
                Assert.Equal("done", Assert.IsType<TextContentBlock>(callToolContent).Text);
            }
        }

        Assert.Equal(11, currentSseItem);
    }

    [Fact]
    public async Task AsyncLocalSetInRunSessionHandlerCallback_Flows_ToAllToolCalls_IfPerSessionExecutionContextEnabled()
    {
        var asyncLocal = new AsyncLocal<string>();
        var totalSessionCount = 0;

        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.PerSessionExecutionContext = true;
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
                options.RunSessionHandler = async (httpContext, mcpServer, cancellationToken) =>
                {
                    asyncLocal.Value = $"RunSessionHandler ({totalSessionCount++})";
                    await mcpServer.RunAsync(cancellationToken);
                };
#pragma warning restore MCPEXP002
            });

        Builder.Services.AddSingleton(McpServerTool.Create([McpServerTool(Name = "async-local-session")] () => asyncLocal.Value));

        await StartAsync();

        var firstSessionId = await CallInitializeAndValidateAsync();

        async Task CallAsyncLocalToolAndValidateAsync(int expectedSessionIndex)
        {
            var response = await HttpClient.PostAsync("", JsonContent(CallTool("async-local-session")), TestContext.Current.CancellationToken);
            var rpcResponse = await AssertSingleSseResponseAsync(response);
            var callToolResponse = AssertType<CallToolResult>(rpcResponse.Result);
            var callToolContent = Assert.Single(callToolResponse.Content);
            Assert.Equal($"RunSessionHandler ({expectedSessionIndex})", Assert.IsType<TextContentBlock>(callToolContent).Text);
        }

        await CallAsyncLocalToolAndValidateAsync(expectedSessionIndex: 0);

        await CallInitializeAndValidateAsync();
        await CallAsyncLocalToolAndValidateAsync(expectedSessionIndex: 1);

        SetSessionId(firstSessionId);
        await CallAsyncLocalToolAndValidateAsync(expectedSessionIndex: 0);
    }

    [Fact]
    public async Task IdleSessions_ArePruned_AfterIdleTimeout()
    {
        var fakeTimeProvider = new FakeTimeProvider();
        Builder.Services.AddMcpServer().WithHttpTransport(options =>
        {
            Assert.Equal(TimeSpan.FromHours(2), options.IdleTimeout);
            options.TimeProvider = fakeTimeProvider;
        });

        await StartAsync();
        await CallInitializeAndValidateAsync();
        await CallEchoAndValidateAsync();

        // The background IdleTrackingBackgroundService prunes sessions asynchronously after
        // the PeriodicTimer (5s interval) tick fires. We advance past the 2-hour idle timeout
        // then poll until the session returns NotFound. Each HTTP POST also refreshes the
        // session's LastActivityTicks via AcquireReferenceAsync, so we must re-advance time
        // each iteration to ensure the session appears idle again for the next prune pass.
        var deadline = DateTime.UtcNow + TestConstants.DefaultTimeout;
        HttpStatusCode statusCode;
        do
        {
            fakeTimeProvider.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(5));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            using var response = await HttpClient.PostAsync("", JsonContent(EchoRequest), TestContext.Current.CancellationToken);
            statusCode = response.StatusCode;
        }
        while (statusCode != HttpStatusCode.NotFound && DateTime.UtcNow < deadline);

        Assert.Equal(HttpStatusCode.NotFound, statusCode);
    }

    [Fact]
    public async Task IdleSessions_AreNotPruned_WithInfiniteIdleTimeoutWhileUnderMaxIdleSessionCount()
    {
        var fakeTimeProvider = new FakeTimeProvider();
        Builder.Services.AddMcpServer().WithHttpTransport(options =>
        {
            options.IdleTimeout = Timeout.InfiniteTimeSpan;
            options.TimeProvider = fakeTimeProvider;
        });

        await StartAsync();
        await CallInitializeAndValidateAsync();
        await CallEchoAndValidateAsync();

        fakeTimeProvider.Advance(TimeSpan.FromDays(1));

        // Echo still works because the session has not been pruned.
        await CallEchoAndValidateAsync();
    }

    [Fact]
    public async Task IdleSessionsPastMaxIdleSessionCount_ArePruned_LongestIdleFirstDespiteIdleTimeout()
    {
        var fakeTimeProvider = new FakeTimeProvider();
        Builder.Services.AddMcpServer().WithHttpTransport(options =>
        {
            options.IdleTimeout = Timeout.InfiniteTimeSpan;
            options.MaxIdleSessionCount = 2;
            options.TimeProvider = fakeTimeProvider;
        });

        var mockLoggerProvider = new MockLoggerProvider();
        Builder.Logging.AddProvider(mockLoggerProvider);

        await StartAsync();

        // Start first session.
        var firstSessionId = await CallInitializeAndValidateAsync();

        // Start a second session to trigger pruning of the original session.
        fakeTimeProvider.Advance(TimeSpan.FromTicks(1));
        var secondSessionId = await CallInitializeAndValidateAsync();

        Assert.NotEqual(firstSessionId, secondSessionId);

        // First session ID still works, since we allow up to 2 idle sessions.
        fakeTimeProvider.Advance(TimeSpan.FromTicks(1));
        SetSessionId(firstSessionId);
        await CallEchoAndValidateAsync();

        // Start a third session to trigger pruning of the first session.
        fakeTimeProvider.Advance(TimeSpan.FromTicks(1));
        var thirdSessionId = await CallInitializeAndValidateAsync();

        Assert.NotEqual(secondSessionId, thirdSessionId);

        // Pruning of the second session results in a 404 since we used the first session more recently.
        SetSessionId(secondSessionId);
        using var response = await HttpClient.PostAsync("", JsonContent(EchoRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // But the first and third session IDs should still work.
        SetSessionId(firstSessionId);
        await CallEchoAndValidateAsync();

        SetSessionId(thirdSessionId);
        await CallEchoAndValidateAsync();

        var idleLimitLogMessage = Assert.Single(mockLoggerProvider.LogMessages, m => m.EventId.Name == "LogIdleSessionLimit");
        Assert.Equal(LogLevel.Information, idleLimitLogMessage.LogLevel);
        Assert.StartsWith("MaxIdleSessionCount of 2 exceeded. Closing idle session", idleLimitLogMessage.Message);
    }

    [Fact]
    public async Task ActiveSession_WithPeriodicRequests_DoesNotTimeout()
    {
        var fakeTimeProvider = new FakeTimeProvider();
        Builder.Services.AddMcpServer().WithHttpTransport(options =>
        {
            options.IdleTimeout = TimeSpan.FromHours(2);
            options.TimeProvider = fakeTimeProvider;
        });

        await StartAsync();
        await CallInitializeAndValidateAsync();

        // Simulate multiple POST requests over a period longer than IdleTimeout
        // Each request should update LastActivityTicks, preventing timeout
        for (int i = 0; i < 5; i++)
        {
            // Advance time by 1 hour between requests
            fakeTimeProvider.Advance(TimeSpan.FromHours(1));
            await CallEchoAndValidateAsync();
        }

        // Total time elapsed: 5 hours (> 2 hour IdleTimeout)
        // But session should still be alive because of periodic activity
        await CallEchoAndValidateAsync();
    }

    [Fact]
    public async Task McpServer_UsedOutOfScope_CanSendNotifications()
    {
        McpServer? capturedServer = null;
        Builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithListResourcesHandler((_, _) => ValueTask.FromResult(new ListResourcesResult()))
            .WithSubscribeToResourcesHandler((context, token) =>
            {
                capturedServer = context.Server;
                return ValueTask.FromResult(new EmptyResult());
            });

        await StartAsync();

        string sessionId = await CallInitializeAndValidateAsync();
        SetSessionId(sessionId);

        // Call the subscribe method to capture the McpServer instance.
        using var getResponse = await HttpClient.GetAsync("", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        using var response = await HttpClient.PostAsync("", JsonContent(SubscribeToResource("file:///test")), TestContext.Current.CancellationToken);
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        AssertType<EmptyResult>(rpcResponse.Result);
        Assert.NotNull(capturedServer);

        // Check the captured McpServer instance can send a notification.
        await capturedServer.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification, TestContext.Current.CancellationToken);
        JsonRpcMessage? firstSseMessage = await ReadSseAsync(getResponse.Content)
            .Select(data => JsonSerializer.Deserialize<JsonRpcMessage>(data, McpJsonUtilities.DefaultOptions))
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        var notification = Assert.IsType<JsonRpcNotification>(firstSseMessage);
        Assert.Equal(NotificationMethods.ResourceUpdatedNotification, notification.Method);
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");
    private static JsonTypeInfo<T> GetJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private static T AssertType<T>(JsonNode? jsonNode)
    {
        var type = JsonSerializer.Deserialize(jsonNode, GetJsonTypeInfo<T>());
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

    private static string ListToolsRequest => """
        {"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}
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
            {"name":"{{{toolName}}}","arguments":{{{arguments}}},"_meta":{"progressToken":"abc123"}}
            """);

    private string SubscribeToResource(string uri) =>
        Request("resources/subscribe", $$"""
            {"uri":"{{uri}}"}
            """);

    private static InitializeResult AssertServerInfo(JsonRpcResponse rpcResponse)
    {
        var initializeResult = AssertType<InitializeResult>(rpcResponse.Result);
        Assert.Equal(nameof(StreamableHttpServerConformanceTests), initializeResult.ServerInfo.Name);
        Assert.Equal("73", initializeResult.ServerInfo.Version);
        return initializeResult;
    }

    private static CallToolResult AssertEchoResponse(JsonRpcResponse rpcResponse)
    {
        var callToolResponse = AssertType<CallToolResult>(rpcResponse.Result);
        var callToolContent = Assert.Single(callToolResponse.Content);
        Assert.Equal($"Hello world! ({rpcResponse.Id})", Assert.IsType<TextContentBlock>(callToolContent).Text);
        return callToolResponse;
    }

    private async Task<string> CallInitializeAndValidateAsync()
    {
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        AssertServerInfo(rpcResponse);

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
