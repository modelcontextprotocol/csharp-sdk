using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Base class for SSE resumability integration tests that can be run against different
/// <see cref="ISseEventStreamStore"/> implementations.
/// </summary>
/// <remarks>
/// <para>
/// Tests in this class verify resumability behavior without relying on implementation-specific
/// internals of the event store. Derived classes can override virtual tests to add additional
/// assertions specific to their event store implementation.
/// </para>
/// <para>
/// The <see cref="CreateEventStreamStoreAsync"/> method must be implemented by derived classes
/// to provide the specific event store implementation to test.
/// </para>
/// </remarks>
public abstract class ResumabilityIntegrationTestsBase(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    /// <summary>
    /// The initialize request JSON for the current protocol version.
    /// </summary>
    protected const string InitializeRequest = """
        {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0.0"}}}
        """;

    /// <summary>
    /// Gets the event stream store created for the current test.
    /// </summary>
    /// <remarks>
    /// This is set after <see cref="CreateServerAsync"/> is called.
    /// </remarks>
    protected ISseEventStreamStore? EventStreamStore { get; private set; }

    /// <summary>
    /// Creates the event stream store implementation to use for this test.
    /// </summary>
    /// <returns>The event stream store instance.</returns>
    protected abstract ValueTask<ISseEventStreamStore> CreateEventStreamStoreAsync();

    [Fact]
    public virtual async Task Server_StoresEvents_WhenEventStoreConfigured()
    {
        // Arrange
        await using var app = await CreateServerAsync();
        await using var client = await ConnectClientAsync();

        // Act - Make a tool call which generates events
        var result = await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - The call succeeded
        Assert.NotNull(result);
        var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Echo: test", textContent.Text);
    }

    [Fact]
    public virtual async Task Client_CanMakeMultipleRequests_WithResumabilityEnabled()
    {
        // Arrange
        await using var app = await CreateServerAsync();
        await using var client = await ConnectClientAsync();

        // Act - Make many requests to verify stability
        for (int i = 0; i < 5; i++)
        {
            var result = await client.CallToolAsync("echo",
                new Dictionary<string, object?> { ["message"] = $"test{i}" },
                cancellationToken: TestContext.Current.CancellationToken);

            var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
            Assert.Equal($"Echo: test{i}", textContent.Text);
        }
    }

    [Fact]
    public virtual async Task Ping_WorksWithResumabilityEnabled()
    {
        // Arrange
        await using var app = await CreateServerAsync();
        await using var client = await ConnectClientAsync();

        // Act & Assert - Ping should work
        await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public virtual async Task ListTools_WorksWithResumabilityEnabled()
    {
        // Arrange
        await using var app = await CreateServerAsync();
        await using var client = await ConnectClientAsync();

        // Act
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tools);
        Assert.Single(tools);
    }

    [Fact]
    public virtual async Task Client_CanPollResponse_FromServer()
    {
        const string ProgressToolName = "progress_tool";
        var clientReceivedInitialValueTcs = new TaskCompletionSource();
        var clientReceivedPolledValueTcs = new TaskCompletionSource();
        var progressTool = McpServerTool.Create(async (RequestContext<CallToolRequestParams> context, IProgress<ProgressNotificationValue> progress) =>
        {
            progress.Report(new() { Progress = 0, Message = "Initial value" });

            await clientReceivedInitialValueTcs.Task.WaitAsync(TestContext.Current.CancellationToken);

            await context.EnablePollingAsync(retryInterval: TimeSpan.FromSeconds(1));

            progress.Report(new() { Progress = 50, Message = "Polled value" });

            await clientReceivedPolledValueTcs.Task.WaitAsync(TestContext.Current.CancellationToken);

            return "Complete";
        }, options: new() { Name = ProgressToolName });

        await using var app = await CreateServerAsync(configureServer: builder =>
        {
            builder.WithTools([progressTool]);
        });
        await using var client = await ConnectClientAsync();

        var progressHandler = new Progress<ProgressNotificationValue>(value =>
        {
            switch (value.Message)
            {
                case "Initial value":
                    Assert.True(clientReceivedInitialValueTcs.TrySetResult(), "Received the initial value more than once.");
                    break;
                case "Polled value":
                    Assert.True(clientReceivedPolledValueTcs.TrySetResult(), "Received the polled value more than once.");
                    break;
                default:
                    throw new UnreachableException($"Unknown progress message '{value.Message}'");
            }
        });

        var result = await client.CallToolAsync(ProgressToolName, progress: progressHandler, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError is true);
        Assert.Equal("Complete", result.Content.OfType<TextContentBlock>().Single().Text);
    }

    [Fact]
    public virtual async Task Client_CanResumePostResponseStream_AfterDisconnection()
    {
        using var faultingStreamHandler = new FaultingStreamHandler()
        {
            InnerHandler = SocketsHttpHandler,
        };

        HttpClient = new(faultingStreamHandler);
        ConfigureHttpClient(HttpClient);

        const string ProgressToolName = "progress_tool";
        const string InitialMessage = "Initial notification";
        const string ReplayedMessage = "Replayed notification";
        const string ResultMessage = "Complete";

        var clientReceivedInitialValueTcs = new TaskCompletionSource();
        var clientReceivedReconnectValueTcs = new TaskCompletionSource();
        var progressTool = McpServerTool.Create(async (RequestContext<CallToolRequestParams> context, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken) =>
        {
            progress.Report(new() { Progress = 0, Message = InitialMessage });

            // Make sure the client receives one message before we disconnect.
            await clientReceivedInitialValueTcs.Task.WaitAsync(TestContext.Current.CancellationToken);

            // Simulate a network disconnection by faulting the response stream.
            var reconnectAttempt = await faultingStreamHandler.TriggerFaultAsync(TestContext.Current.CancellationToken);

            // Send another message that the client should receive after reconnecting.
            progress.Report(new() { Progress = 50, Message = ReplayedMessage });

            reconnectAttempt.Continue();

            // Wait for the client to receive the message via replay.
            await clientReceivedReconnectValueTcs.Task.WaitAsync(TestContext.Current.CancellationToken);

            // Return the final result with the client still connected.
            return ResultMessage;
        }, options: new() { Name = ProgressToolName });

        await using var app = await CreateServerAsync(configureServer: builder =>
        {
            builder.WithTools([progressTool]);
        });
        await using var client = await ConnectClientAsync();

        var initialNotificationReceivedCount = 0;
        var replayedNotificationReceivedCount = 0;
        var progressHandler = new Progress<ProgressNotificationValue>(value =>
        {
            switch (value.Message)
            {
                case InitialMessage:
                    initialNotificationReceivedCount++;
                    clientReceivedInitialValueTcs.TrySetResult();
                    break;
                case ReplayedMessage:
                    replayedNotificationReceivedCount++;
                    clientReceivedReconnectValueTcs.TrySetResult();
                    break;
                default:
                    throw new UnreachableException($"Unknown progress message '{value.Message}'");
            }
        });

        var result = await client.CallToolAsync(ProgressToolName, progress: progressHandler, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError is true);
        Assert.Equal(1, initialNotificationReceivedCount);
        Assert.Equal(1, replayedNotificationReceivedCount);
        Assert.Equal(ResultMessage, result.Content.OfType<TextContentBlock>().Single().Text);
    }

    [Fact]
    public virtual async Task Client_CanResumeUnsolicitedMessageStream_AfterDisconnection()
    {
        var timeout = TestConstants.DefaultTimeout;
        using var faultingStreamHandler = new FaultingStreamHandler()
        {
            InnerHandler = SocketsHttpHandler,
        };

        HttpClient = new(faultingStreamHandler);
        ConfigureHttpClient(HttpClient);

        // Capture the server instance via RunSessionHandler
        var serverTcs = new TaskCompletionSource<McpServer>();

        await using var app = await CreateServerAsync(configureTransport: options =>
        {
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
            options.RunSessionHandler = (httpContext, mcpServer, cancellationToken) =>
            {
                serverTcs.TrySetResult(mcpServer);
                return mcpServer.RunAsync(cancellationToken);
            };
#pragma warning restore MCPEXP002
        });

        await using var client = await ConnectClientAsync();

        // Get the server instance
        var server = await serverTcs.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);

        // Set up notification tracking with unique messages
        var clientReceivedInitialNotificationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientReceivedReplayedNotificationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientReceivedReconnectNotificationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        const string CustomNotificationMethod = "test/custom_notification";
        const string InitialMessage = "Initial notification";
        const string ReplayedMessage = "Replayed notification";
        const string ReconnectMessage = "Reconnect notification";

        var initialNotificationReceivedCount = 0;
        var replayedNotificationReceivedCount = 0;
        var reconnectNotificationReceivedCount = 0;

        await using var _ = client.RegisterNotificationHandler(CustomNotificationMethod, (notification, cancellationToken) =>
        {
            var message = notification.Params?["message"]?.GetValue<string>();
            switch (message)
            {
                case InitialMessage:
                    initialNotificationReceivedCount++;
                    clientReceivedInitialNotificationTcs.TrySetResult();
                    break;
                case ReplayedMessage:
                    replayedNotificationReceivedCount++;
                    clientReceivedReplayedNotificationTcs.TrySetResult();
                    break;
                case ReconnectMessage:
                    reconnectNotificationReceivedCount++;
                    clientReceivedReconnectNotificationTcs.TrySetResult();
                    break;
                default:
                    throw new UnreachableException($"Unknown notification message '{message}'");
            }
            return default;
        });

        // Wait for the client's unsolicited message stream to be established before sending notifications
        await faultingStreamHandler.WaitForUnsolicitedMessageStreamAsync(TestContext.Current.CancellationToken);

        // Send a custom notification to the client on the unsolicited message stream
        await server.SendNotificationAsync(CustomNotificationMethod, new JsonObject { ["message"] = InitialMessage }, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for client to receive the first notification
        await clientReceivedInitialNotificationTcs.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);

        // Fault the unsolicited message stream (GET SSE)
        var reconnectAttempt = await faultingStreamHandler.TriggerFaultAsync(TestContext.Current.CancellationToken);

        // Send another notification while the client is disconnected - this should be stored
        await server.SendNotificationAsync(CustomNotificationMethod, new JsonObject { ["message"] = ReplayedMessage }, cancellationToken: TestContext.Current.CancellationToken);

        // Allow the client to reconnect
        reconnectAttempt.Continue();

        // Wait for client to receive the notification via replay
        await clientReceivedReplayedNotificationTcs.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);

        // Send a final notification while the client has reconnected - this should be handled by the transport
        await server.SendNotificationAsync(CustomNotificationMethod, new JsonObject { ["message"] = ReconnectMessage }, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for the client to receive the final notification
        await clientReceivedReconnectNotificationTcs.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);

        // Assert each notification was received exactly once
        Assert.Equal(1, initialNotificationReceivedCount);
        Assert.Equal(1, replayedNotificationReceivedCount);
        Assert.Equal(1, reconnectNotificationReceivedCount);
    }

    [Fact]
    public virtual async Task Server_Returns400_WhenLastEventIdRefersToWrongSession()
    {
        // Arrange - Create server with event store
        await using var app = await CreateServerAsync();

        // First, initialize a session and make a call to generate some events
        using var initRequest = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Headers =
            {
                Accept = { new("application/json"), new("text/event-stream") }
            },
            Content = new StringContent(InitializeRequest, Encoding.UTF8, "application/json"),
        };
        var initResponse = await HttpClient.SendAsync(initRequest, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        initResponse.EnsureSuccessStatusCode();

        // Get the session ID from the response
        var sessionId = initResponse.Headers.GetValues("Mcp-Session-Id").First();

        // Read the SSE response to get an event ID
        await using var initStream = await initResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string? eventId = null;
        await foreach (var sseItem in SseParser.Create(initStream).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            if (!string.IsNullOrEmpty(sseItem.EventId))
            {
                eventId = sseItem.EventId;
            }
        }

        Assert.NotNull(eventId);

        // Act - Try to resume with a different session ID but the same event ID
        var wrongSessionId = "wrong-session-id";
        using var resumeRequest = new HttpRequestMessage(HttpMethod.Get, "/")
        {
            Headers =
            {
                Accept = { new("text/event-stream") },
            }
        };
        resumeRequest.Headers.Add("Mcp-Session-Id", wrongSessionId);
        resumeRequest.Headers.Add("Last-Event-ID", eventId);

        var resumeResponse = await HttpClient.SendAsync(resumeRequest, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        // Assert - First we get 404 because the wrong session doesn't exist
        Assert.Equal(HttpStatusCode.NotFound, resumeResponse.StatusCode);

        // Now test with an existing session but event ID from a different session
        // Create a second session
        using var initRequest2 = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Headers =
            {
                Accept = { new("application/json"), new("text/event-stream") }
            },
            Content = new StringContent(InitializeRequest, Encoding.UTF8, "application/json"),
        };
        var initResponse2 = await HttpClient.SendAsync(initRequest2, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        initResponse2.EnsureSuccessStatusCode();

        var sessionId2 = initResponse2.Headers.GetValues("Mcp-Session-Id").First();
        Assert.NotEqual(sessionId, sessionId2);

        // Read the second session's response
        await using var initStream2 = await initResponse2.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var _ in SseParser.Create(initStream2).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            // Consume the stream
        }

        // Try to use session 2's ID but with an event ID from session 1
        using var mismatchRequest = new HttpRequestMessage(HttpMethod.Get, "/")
        {
            Headers =
            {
                Accept = { new("text/event-stream") },
            }
        };
        mismatchRequest.Headers.Add("Mcp-Session-Id", sessionId2);
        mismatchRequest.Headers.Add("Last-Event-ID", eventId);  // This event ID belongs to session 1

        var mismatchResponse = await HttpClient.SendAsync(mismatchRequest, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        // Assert - Should get 400 Bad Request because the event ID doesn't match the session
        Assert.Equal(HttpStatusCode.BadRequest, mismatchResponse.StatusCode);

        // Verify the error message
        var responseBody = await mismatchResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var errorResponse = JsonNode.Parse(responseBody);
        Assert.NotNull(errorResponse);
        var errorMessage = errorResponse["error"]?["message"]?.GetValue<string>();
        Assert.Equal("Bad Request: The Last-Event-ID header refers to a session with a different session ID.", errorMessage);
    }

    [Fact]
    public virtual async Task EnablePollingAsync_SendsSseItemWithRetryField()
    {
        // Arrange
        const string PollingToolName = "polling_tool";
        var expectedRetryInterval = TimeSpan.FromSeconds(5);
        var pollingTool = McpServerTool.Create(async (RequestContext<CallToolRequestParams> context) =>
        {
            await context.EnablePollingAsync(retryInterval: expectedRetryInterval);
            return "Polling enabled";
        }, options: new() { Name = PollingToolName });

        await using var app = await CreateServerAsync(configureServer: builder =>
        {
            builder.WithTools([pollingTool]);
        });
        await using var client = await ConnectClientAsync();

        // Act - Call the tool that enables polling
        var result = await client.CallToolAsync(PollingToolName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - The result should be successful
        Assert.False(result.IsError is true);
        Assert.Equal("Polling enabled", result.Content.OfType<TextContentBlock>().Single().Text);
    }

    [McpServerToolType]
    protected class ResumabilityTestTools
    {
        [McpServerTool(Name = "echo"), Description("Echoes the message back")]
        public static string Echo(string message) => $"Echo: {message}";
    }

    /// <summary>
    /// Creates a server with the event stream store from <see cref="CreateEventStreamStoreAsync"/>.
    /// </summary>
    protected async Task<WebApplication> CreateServerAsync(
        Action<IMcpServerBuilder>? configureServer = null,
        Action<HttpServerTransportOptions>? configureTransport = null)
    {
        EventStreamStore = await CreateEventStreamStoreAsync();
        return await CreateServerAsync(EventStreamStore, configureServer, configureTransport);
    }

    /// <summary>
    /// Creates a server with the specified event stream store.
    /// </summary>
    protected async Task<WebApplication> CreateServerAsync(
        ISseEventStreamStore? eventStreamStore,
        Action<IMcpServerBuilder>? configureServer = null,
        Action<HttpServerTransportOptions>? configureTransport = null)
    {
        var serverBuilder = Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.EventStreamStore = eventStreamStore;
                configureTransport?.Invoke(options);
            })
            .WithTools<ResumabilityTestTools>();

        configureServer?.Invoke(serverBuilder);

        var app = Builder.Build();
        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    /// <summary>
    /// Connects a client to the server.
    /// </summary>
    protected async Task<McpClient> ConnectClientAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        return await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Sends an initialize request and reads the SSE response.
    /// </summary>
    protected async Task<SseResponse> SendInitializeAndReadSseResponseAsync(string initializeRequest)
    {
        using var requestContent = new StringContent(initializeRequest, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Headers =
            {
                Accept = { new("application/json"), new("text/event-stream") }
            },
            Content = requestContent,
        };

        var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var sseResponse = new SseResponse();
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var sseItem in SseParser.Create(stream).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            if (!string.IsNullOrEmpty(sseItem.EventId))
            {
                sseResponse.LastEventId = sseItem.EventId;
            }
        }

        return sseResponse;
    }

    /// <summary>
    /// Response data from an SSE stream.
    /// </summary>
    protected struct SseResponse
    {
        public string? LastEventId { get; set; }
    }
}
