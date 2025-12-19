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

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Integration tests for SSE resumability with full client-server flow.
/// These tests use McpClient for end-to-end testing and only use raw HTTP
/// for SSE format verification where McpClient abstracts away the details.
/// </summary>
public class ResumabilityIntegrationTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    private const string InitializeRequest = """
        {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0.0"}}}
        """;

    [Fact]
    public async Task Server_StoresEvents_WhenEventStoreConfigured()
    {
        // Arrange
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore);
        await using var client = await ConnectClientAsync();

        // Act - Make a tool call which generates events
        var result = await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Events were stored
        Assert.NotNull(result);
        Assert.True(eventStreamStore.StoreEventCallCount > 0, "Expected events to be stored when EventStore is configured");
    }

    [Fact]
    public async Task Server_StoresMultipleEvents_ForMultipleToolCalls()
    {
        // Arrange
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore);
        await using var client = await ConnectClientAsync();

        // Act - Make multiple tool calls
        var initialCount = eventStreamStore.StoreEventCallCount;

        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test1" },
            cancellationToken: TestContext.Current.CancellationToken);

        var countAfterFirst = eventStreamStore.StoreEventCallCount;

        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test2" },
            cancellationToken: TestContext.Current.CancellationToken);

        var countAfterSecond = eventStreamStore.StoreEventCallCount;

        // Assert - More events were stored for each call
        Assert.True(countAfterFirst > initialCount, "Expected more events after first call");
        Assert.True(countAfterSecond > countAfterFirst, "Expected more events after second call");
    }

    [Fact]
    public async Task Client_CanMakeMultipleRequests_WithResumabilityEnabled()
    {
        // Arrange
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore);
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

        // Assert - All requests succeeded and events were stored
        Assert.True(eventStreamStore.StoreEventCallCount >= 5, "Expected events to be stored for each request");
    }

    [Fact]
    public async Task Ping_WorksWithResumabilityEnabled()
    {
        // Arrange
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore);
        await using var client = await ConnectClientAsync();

        // Act & Assert - Ping should work
        await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListTools_WorksWithResumabilityEnabled()
    {
        // Arrange
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore);
        await using var client = await ConnectClientAsync();

        // Act
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tools);
        Assert.Single(tools);
    }

    [Fact]
    public async Task Server_IncludesEventIdAndRetry_InSseResponse()
    {
        // Arrange
        var expectedRetryInterval = TimeSpan.FromSeconds(5);
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore, retryInterval: expectedRetryInterval);

        // Act
        var sseResponse = await SendInitializeAndReadSseResponseAsync(InitializeRequest);

        // Assert - Event IDs and retry field should be present in the response
        Assert.True(sseResponse.LastEventId is not null, "Expected SSE response to contain event IDs");
        Assert.Equal(expectedRetryInterval, sseResponse.RetryInterval);
    }

    [Fact]
    public async Task Server_WithoutEventStore_DoesNotIncludeEventIdAndRetry()
    {
        // Arrange - Server without event store
        await using var app = await CreateServerAsync();

        // Act
        var sseResponse = await SendInitializeAndReadSseResponseAsync(InitializeRequest);

        // Assert - No event IDs or retry field when EventStore is not configured
        Assert.True(sseResponse.LastEventId is null, "Did not expect event IDs when EventStore is not configured");
        Assert.True(sseResponse.RetryInterval is null, "Did not expect retry field when EventStore is not configured");
    }

    [Fact]
    public async Task Server_DoesNotSendPrimingEvents_ToOlderProtocolVersionClients()
    {
        // Arrange - Server with resumability enabled
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore, retryInterval: TimeSpan.FromSeconds(5));

        // Use an older protocol version that doesn't support resumability
        const string OldProtocolInitRequest = """
            {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"OldClient","version":"1.0.0"}}}
            """;

        var sseResponse = await SendInitializeAndReadSseResponseAsync(OldProtocolInitRequest);

        // Assert - Old clients should not receive event IDs or retry fields (no priming events)
        Assert.True(sseResponse.LastEventId is null, "Old protocol clients should not receive event IDs");
        Assert.True(sseResponse.RetryInterval is null, "Old protocol clients should not receive retry field");

        // Event store should not have been called for old clients
        Assert.Equal(0, eventStreamStore.StoreEventCallCount);
    }

    [Fact]
    public async Task Client_ReceivesRetryInterval_FromServer()
    {
        // Arrange - Server with specific retry interval
        var expectedRetry = TimeSpan.FromMilliseconds(3000);
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore, retryInterval: expectedRetry);

        // Act - Send initialize and read the retry field
        var sseItem = await SendInitializeAndReadSseResponseAsync(InitializeRequest);

        // Assert - Client receives the retry interval from server
        Assert.Equal(expectedRetry, sseItem.RetryInterval);
    }

    [Fact]
    public async Task Client_CanPollResponse_FromServer()
    {
        const string ProgressToolName = "progress_tool";
        var clientReceivedInitialValueTcs = new TaskCompletionSource();
        var clientReceivedPolledValueTcs = new TaskCompletionSource();
        var progressTool = McpServerTool.Create(async (RequestContext<CallToolRequestParams> context, IProgress<ProgressNotificationValue> progress) =>
        {
            progress.Report(new() { Progress = 0, Message = "Initial value" });

            await clientReceivedInitialValueTcs.Task;

            await context.EnablePollingAsync(retryInterval: TimeSpan.FromSeconds(1));

            progress.Report(new() { Progress = 50, Message = "Polled value" });

            await clientReceivedPolledValueTcs.Task;

            return "Complete";
        }, options: new() { Name = ProgressToolName });
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore, configureServer: builder =>
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
    public async Task Client_CanResumePostResponseStream_AfterDisconnection()
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
            await clientReceivedInitialValueTcs.Task;

            // Simulate a network disconnection by faulting the response stream.
            var reconnectAttempt = await faultingStreamHandler.TriggerFaultAsync(TestContext.Current.CancellationToken);

            // Send another message that the client should receive after reconnecting.
            progress.Report(new() { Progress = 50, Message = ReplayedMessage });

            reconnectAttempt.Continue();

            // Wait for the client to receive the message via replay.
            await clientReceivedReconnectValueTcs.Task;

            // Return the final result with the client still connected.
            return ResultMessage;
        }, options: new() { Name = ProgressToolName });
        var eventStreamStore = new TestSseEventStreamStore();
        await using var app = await CreateServerAsync(eventStreamStore, configureServer: builder =>
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
    public async Task Client_CanResumeUnsolicitedMessageStream_AfterDisconnection()
    {
        using var faultingStreamHandler = new FaultingStreamHandler()
        {
            InnerHandler = SocketsHttpHandler,
        };

        HttpClient = new(faultingStreamHandler);
        ConfigureHttpClient(HttpClient);

        var eventStreamStore = new TestSseEventStreamStore();

        // Capture the server instance via RunSessionHandler
        var serverTcs = new TaskCompletionSource<McpServer>();

        await using var app = await CreateServerAsync(eventStreamStore, configureTransport: options =>
        {
            options.RunSessionHandler = (httpContext, mcpServer, cancellationToken) =>
            {
                serverTcs.TrySetResult(mcpServer);
                return mcpServer.RunAsync(cancellationToken);
            };
        });

        await using var client = await ConnectClientAsync();

        // Get the server instance
        var server = await serverTcs.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Set up notification tracking with unique messages
        var clientReceivedInitialNotificationTcs = new TaskCompletionSource();
        var clientReceivedReplayedNotificationTcs = new TaskCompletionSource();
        var clientReceivedReconnectNotificationTcs = new TaskCompletionSource();

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

        // Send a custom notification to the client on the unsolicited message stream
        await server.SendNotificationAsync(CustomNotificationMethod, new JsonObject { ["message"] = InitialMessage }, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for client to receive the first notification
        await clientReceivedInitialNotificationTcs.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Fault the unsolicited message stream (GET SSE)
        var reconnectAttempt = await faultingStreamHandler.TriggerFaultAsync(TestContext.Current.CancellationToken);

        // Send another notification while the client is disconnected - this should be stored
        await server.SendNotificationAsync(CustomNotificationMethod, new JsonObject { ["message"] = ReplayedMessage }, cancellationToken: TestContext.Current.CancellationToken);

        // Allow the client to reconnect
        reconnectAttempt.Continue();

        // Wait for client to receive the notification via replay
        await clientReceivedReplayedNotificationTcs.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Send a final notification while the client has reconnected - this should be handled by the transport
        await server.SendNotificationAsync(CustomNotificationMethod, new JsonObject { ["message"] = ReconnectMessage }, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for the client to receive the final notification
        await clientReceivedReconnectNotificationTcs.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Assert each notification was received exactly once
        Assert.Equal(1, initialNotificationReceivedCount);
        Assert.Equal(1, replayedNotificationReceivedCount);
        Assert.Equal(1, reconnectNotificationReceivedCount);
    }

    [McpServerToolType]
    private class ResumabilityTestTools
    {
        [McpServerTool(Name = "echo"), Description("Echoes the message back")]
        public static string Echo(string message) => $"Echo: {message}";
    }

    private async Task<WebApplication> CreateServerAsync(
        ISseEventStreamStore? eventStreamStore = null,
        TimeSpan? retryInterval = null,
        Action<IMcpServerBuilder>? configureServer = null,
        Action<HttpServerTransportOptions>? configureTransport = null)
    {
        var serverBuilder = Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.EventStreamStore = eventStreamStore;
                if (retryInterval.HasValue)
                {
                    options.RetryInterval = retryInterval.Value;
                }
                configureTransport?.Invoke(options);
            })
            .WithTools<ResumabilityTestTools>();

        configureServer?.Invoke(serverBuilder);

        var app = Builder.Build();
        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private async Task<McpClient> ConnectClientAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        return await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<SseResponse> SendInitializeAndReadSseResponseAsync(string initializeRequest)
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
            if (sseItem.ReconnectionInterval.HasValue)
            {
                sseResponse.RetryInterval = sseItem.ReconnectionInterval.Value;
            }
        }

        return sseResponse;
    }

    private struct SseResponse
    {
        public string? LastEventId { get; set; }
        public TimeSpan? RetryInterval { get; set; }
    }
}
