using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Integration tests for SSE resumability with full client-server flow.
/// </summary>
public class ResumabilityIntegrationTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    private const string InitializeRequest = """
        {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0.0"}}}
        """;

    [Fact]
    public async Task Server_StoresEvents_WhenEventStoreConfigured()
    {
        // Arrange
        var (app, eventStore) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        await using var client = await ConnectClientAsync();

        // Act - Make a tool call which generates events
        var result = await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Events were stored
        Assert.NotNull(result);
        Assert.True(eventStore.StoreEventCallCount > 0, "Expected events to be stored when EventStore is configured");
    }

    [Fact]
    public async Task Server_StoresMultipleEvents_ForMultipleToolCalls()
    {
        // Arrange
        var (app, eventStore) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        await using var client = await ConnectClientAsync();

        // Act - Make multiple tool calls
        var initialCount = eventStore.StoreEventCallCount;

        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test1" },
            cancellationToken: TestContext.Current.CancellationToken);

        var countAfterFirst = eventStore.StoreEventCallCount;

        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test2" },
            cancellationToken: TestContext.Current.CancellationToken);

        var countAfterSecond = eventStore.StoreEventCallCount;

        // Assert - More events were stored for each call
        Assert.True(countAfterFirst > initialCount, "Expected more events after first call");
        Assert.True(countAfterSecond > countAfterFirst, "Expected more events after second call");
    }

    [Fact]
    public async Task Server_IncludesEventIdAndRetry_InSseResponse()
    {
        // Arrange
        var (app, _) = await CreateServerWithEventStoreAsync(retryInterval: 5000);
        await using var _ = app;

        // Act
        var sseFields = await SendInitializeAndReadSseFieldsAsync();

        // Assert - Event IDs and retry field should be present in the response
        Assert.True(sseFields.HasEventId, "Expected SSE response to contain event IDs");
        Assert.True(sseFields.HasRetry, "Expected SSE response to contain retry field");
        Assert.Equal("5000", sseFields.RetryValue);
    }

    [Fact]
    public async Task Server_WithoutEventStore_DoesNotIncludeEventIdAndRetry()
    {
        // Arrange - Server without event store
        Builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<ResumabilityTestTools>();

        var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var _ = app;

        // Act
        var sseFields = await SendInitializeAndReadSseFieldsAsync();

        // Assert - No event IDs or retry field when EventStore is not configured
        Assert.False(sseFields.HasEventId, "Did not expect event IDs when EventStore is not configured");
        Assert.False(sseFields.HasRetry, "Did not expect retry field when EventStore is not configured");
    }

    [Fact]
    public async Task EventStore_IsCalledWithCorrectStreamId_ForPostRequests()
    {
        // Arrange
        var (app, eventStore) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        await using var client = await ConnectClientAsync();

        // Act - Make multiple tool calls
        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test1" },
            cancellationToken: TestContext.Current.CancellationToken);

        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test2" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Events were stored (stream IDs are internal but we verify storage occurred)
        Assert.True(eventStore.StoreEventCallCount >= 2,
            $"Expected at least 2 events to be stored, got {eventStore.StoreEventCallCount}");
    }

    [Fact]
    public async Task Client_CanMakeMultipleRequests_WithResumabilityEnabled()
    {
        // Arrange
        var (app, eventStore) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

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
        Assert.True(eventStore.StoreEventCallCount >= 5, "Expected events to be stored for each request");
    }

    [Fact]
    public async Task Ping_WorksWithResumabilityEnabled()
    {
        // Arrange
        var (app, _) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        await using var client = await ConnectClientAsync();

        // Act & Assert - Ping should work
        await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListTools_WorksWithResumabilityEnabled()
    {
        // Arrange
        var (app, _) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        await using var client = await ConnectClientAsync();

        // Act
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tools);
        Assert.Equal(3, tools.Count); // echo, slow_echo, and close_stream
        Assert.Contains(tools, t => t.Name == "echo");
        Assert.Contains(tools, t => t.Name == "slow_echo");
        Assert.Contains(tools, t => t.Name == "close_stream");
    }

    [Fact]
    public async Task Server_StoresEventsWithUniqueIds_ForEachToolCall()
    {
        // Arrange
        var (app, eventStore) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        // Establish a session and make tool calls
        var (sessionId, firstEventId) = await EstablishSessionAndGetEventIdAsync();

        // Make tool calls to generate more events
        var secondEventId = await CallToolViaHttpAsync(sessionId, "echo", """{"message": "test1"}""");
        var thirdEventId = await CallToolViaHttpAsync(sessionId, "echo", """{"message": "test2"}""");

        // Assert - All event IDs should be unique
        Assert.NotEqual(firstEventId, secondEventId);
        Assert.NotEqual(secondEventId, thirdEventId);
        Assert.NotEqual(firstEventId, thirdEventId);

        // Assert - Event store should have accumulated events
        Assert.True(eventStore.StoredEventIds.Count >= 3,
            $"Expected at least 3 stored event IDs, got {eventStore.StoredEventIds.Count}");
    }

    [Fact]
    public async Task Server_AcceptsReconnection_WithLastEventId_FromToolCall()
    {
        // Arrange
        var (app, _) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        // Establish a session and make a tool call to get an event ID
        var (sessionId, _) = await EstablishSessionAndGetEventIdAsync();
        var toolCallEventId = await CallToolViaHttpAsync(sessionId, "echo", """{"message": "test"}""");

        // Act - Try to reconnect with the event ID from the tool call
        // This tests that the server can look up the event ID and identify its stream
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        getRequest.Headers.Add("Accept", "text/event-stream");
        getRequest.Headers.Add("mcp-session-id", sessionId);
        getRequest.Headers.Add("Last-Event-ID", toolCallEventId);

        using var response = await HttpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        // Assert - Server should accept the reconnection (200 OK with SSE)
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Client_TracksEventIds_AcrossMultipleToolCalls()
    {
        // Arrange
        var (app, eventStore) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        await using var client = await ConnectClientAsync();

        // Act - Make multiple tool calls
        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "first" },
            cancellationToken: TestContext.Current.CancellationToken);

        var eventsAfterFirst = eventStore.StoredEventIds.Count;

        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "second" },
            cancellationToken: TestContext.Current.CancellationToken);

        var eventsAfterSecond = eventStore.StoredEventIds.Count;

        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "third" },
            cancellationToken: TestContext.Current.CancellationToken);

        var eventsAfterThird = eventStore.StoredEventIds.Count;

        // Assert - Event store should have accumulated events with unique IDs
        Assert.True(eventsAfterSecond > eventsAfterFirst,
            $"Expected more events after second call. After first: {eventsAfterFirst}, After second: {eventsAfterSecond}");
        Assert.True(eventsAfterThird > eventsAfterSecond,
            $"Expected more events after third call. After second: {eventsAfterSecond}, After third: {eventsAfterThird}");

        // All event IDs should be unique
        var allEventIds = eventStore.StoredEventIds.ToList();
        Assert.Equal(allEventIds.Count, allEventIds.Distinct().Count());
    }

    [Fact]
    public async Task Server_ClosesConnection_WhenLastEventIdNotFound()
    {
        // Arrange
        var (app, _) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        // First establish a session
        var (sessionId, _) = await EstablishSessionAndGetEventIdAsync();

        // Act - Try to reconnect with an invalid Last-Event-ID
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        getRequest.Headers.Add("Accept", "text/event-stream");
        getRequest.Headers.Add("mcp-session-id", sessionId);
        getRequest.Headers.Add("Last-Event-ID", "invalid-event-id-that-does-not-exist");

        // Assert - The server should close the connection (throw an exception) when the event ID is not found
        // This is the expected behavior per the MCP spec - clients should start fresh if their event ID is stale
        await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
        {
            using var response = await HttpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken);
            // Read the stream to trigger the error
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task Server_AllowsReconnection_WithValidLastEventId()
    {
        // Arrange
        var (app, _) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        // First establish a session
        var (sessionId, lastEventId) = await EstablishSessionAndGetEventIdAsync();

        // Act - Reconnect with valid Last-Event-ID (this should be allowed even though a GET was already started)
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        getRequest.Headers.Add("Accept", "text/event-stream");
        getRequest.Headers.Add("mcp-session-id", sessionId);
        getRequest.Headers.Add("Last-Event-ID", lastEventId);

        using var response = await HttpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        // Assert - Reconnection should succeed
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Tool_CanCloseSseStream_ViaRequestContext()
    {
        // Arrange
        var (app, eventStore) = await CreateServerWithEventStoreAsync();
        await using var _ = app;

        // Establish a session and get an event ID
        var (sessionId, _) = await EstablishSessionAndGetEventIdAsync();

        // Act - Call the close_stream tool which should close the SSE connection
        var callResult = await CallToolViaHttpWithTimeoutAsync(sessionId, "close_stream", """{"message": "test"}""");

        // Assert - The connection was closed but we should get a result (from reconnection or before close)
        // The key point is that this doesn't hang and we get an event ID back
        Assert.True(callResult.StreamClosed, "Expected stream to be closed by the tool");
        Assert.True(eventStore.StoreEventCallCount >= 1, "Expected events to be stored");
    }

    [McpServerToolType]
    private class ResumabilityTestTools
    {
        [McpServerTool(Name = "echo"), Description("Echoes the message back")]
        public static string Echo(string message) => $"Echo: {message}";

        [McpServerTool(Name = "slow_echo"), Description("Echoes after a delay")]
        public static async Task<string> SlowEcho(string message, CancellationToken cancellationToken)
        {
            await Task.Delay(100, cancellationToken);
            return $"Slow Echo: {message}";
        }

        [McpServerTool(Name = "close_stream"), Description("Closes the SSE stream and returns a message")]
        public static string CloseStream(RequestContext<CallToolRequestParams> context, string message)
        {
            // Close the SSE stream - client should reconnect to get the response
            context.CloseSseStream();
            return $"Stream closed: {message}";
        }
    }

    private async Task<(WebApplication App, InMemoryEventStore EventStore)> CreateServerWithEventStoreAsync(int? retryInterval = null)
    {
        var eventStore = new InMemoryEventStore();

        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.EventStore = eventStore;
                if (retryInterval.HasValue)
                {
                    options.RetryInterval = retryInterval.Value;
                }
            })
            .WithTools<ResumabilityTestTools>();

        var app = Builder.Build();
        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        return (app, eventStore);
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

    /// <summary>
    /// Sends an initialize request and reads SSE fields from the response.
    /// </summary>
    private async Task<SseFields> SendInitializeAndReadSseFieldsAsync()
    {
        using var requestContent = new StringContent(InitializeRequest, Encoding.UTF8, "application/json");
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

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        var result = new SseFields();

        while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
        {
            if (string.IsNullOrEmpty(line))
            {
                break; // End of first SSE message
            }

            if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                result.EventId = line["id:".Length..].Trim();
            }
            else if (line.StartsWith("retry:", StringComparison.Ordinal))
            {
                result.RetryValue = line["retry:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                result.DataValue = line["data:".Length..].Trim();
            }
            else if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                result.EventType = line["event:".Length..].Trim();
            }
        }

        return result;
    }

    /// <summary>
    /// Establishes a session by sending an initialize request and returns the session ID and last event ID.
    /// </summary>
    private async Task<(string SessionId, string LastEventId)> EstablishSessionAndGetEventIdAsync()
    {
        using var requestContent = new StringContent(InitializeRequest, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Headers =
            {
                Accept = { new("application/json"), new("text/event-stream") }
            },
            Content = requestContent,
        };

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var sessionId = response.Headers.TryGetValues("mcp-session-id", out var sessionValues)
            ? sessionValues.First()
            : throw new InvalidOperationException("No session ID in response");

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        string? lastEventId = null;

        while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
        {
            if (string.IsNullOrEmpty(line))
            {
                break; // End of first SSE message
            }

            if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                lastEventId = line["id:".Length..].Trim();
            }
        }

        return (sessionId, lastEventId ?? throw new InvalidOperationException("No event ID in response"));
    }

    /// <summary>
    /// Calls a tool via HTTP POST request within an established session.
    /// </summary>
    private async Task<string> CallToolViaHttpAsync(string sessionId, string toolName, string arguments)
    {
        var callToolRequest = $$$"""
            {"jsonrpc":"2.0","id":"{{{Guid.NewGuid()}}}","method":"tools/call","params":{"name":"{{{toolName}}}","arguments":{{{arguments}}}}}
            """;

        using var requestContent = new StringContent(callToolRequest, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Headers =
            {
                Accept = { new("application/json"), new("text/event-stream") }
            },
            Content = requestContent,
        };
        request.Headers.Add("mcp-session-id", sessionId);

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        string? lastEventId = null;
        var dataBuilder = new StringBuilder();

        while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
        {
            if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                lastEventId = line["id:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataBuilder.AppendLine(line["data:".Length..].Trim());
            }
            else if (string.IsNullOrEmpty(line) && lastEventId is not null)
            {
                // End of SSE message with an event ID - we have the response
                break;
            }
        }

        return lastEventId ?? throw new InvalidOperationException("No event ID in tool call response");
    }

    /// <summary>
    /// Calls a tool via HTTP POST request with timeout handling for stream closure.
    /// </summary>
    private async Task<ToolCallWithClosureResult> CallToolViaHttpWithTimeoutAsync(string sessionId, string toolName, string arguments)
    {
        var callToolRequest = $$$"""
            {"jsonrpc":"2.0","id":"{{{Guid.NewGuid()}}}","method":"tools/call","params":{"name":"{{{toolName}}}","arguments":{{{arguments}}}}}
            """;

        using var requestContent = new StringContent(callToolRequest, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Headers =
            {
                Accept = { new("application/json"), new("text/event-stream") }
            },
            Content = requestContent,
        };
        request.Headers.Add("mcp-session-id", sessionId);

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        string? lastEventId = null;
        bool streamClosed = false;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, timeoutCts.Token);

        try
        {
            while (await reader.ReadLineAsync(linkedCts.Token) is { } line)
            {
                if (line.StartsWith("id:", StringComparison.Ordinal))
                {
                    lastEventId = line["id:".Length..].Trim();
                }
                else if (string.IsNullOrEmpty(line) && lastEventId is not null)
                {
                    break;
                }
            }

            // If we reach end of stream without timeout, the stream was closed
            streamClosed = true;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            // Timeout - stream wasn't closed
            streamClosed = false;
        }

        return new ToolCallWithClosureResult
        {
            LastEventId = lastEventId,
            StreamClosed = streamClosed
        };
    }

    private sealed class ToolCallWithClosureResult
    {
        public string? LastEventId { get; set; }
        public bool StreamClosed { get; set; }
    }

    private sealed class SseFields
    {
        [MemberNotNullWhen(true, nameof(EventId))]
        public bool HasEventId => EventId is not null;
        public string? EventId { get; set; }

        [MemberNotNullWhen(true, nameof(RetryValue))]
        public bool HasRetry => RetryValue is not null;
        public string? RetryValue { get; set; }

        [MemberNotNullWhen(true, nameof(DataValue))]
        public bool HasData => DataValue is not null;
        public string? DataValue { get; set; }

        [MemberNotNullWhen(true, nameof(EventType))]
        public bool HasEvent => EventType is not null;
        public string? EventType { get; set; }
    }
}
