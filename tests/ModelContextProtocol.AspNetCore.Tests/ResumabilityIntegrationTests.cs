using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
/// These tests use McpClient for end-to-end testing and only use raw HTTP
/// for SSE format verification where McpClient abstracts away the details.
/// </summary>
public class ResumabilityIntegrationTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    private const string InitializeRequest = """
        {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0.0"}}}
        """;

    #region McpClient-based End-to-End Tests

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
        Assert.Equal(2, tools.Count); // echo and slow_echo
        Assert.Contains(tools, t => t.Name == "echo");
        Assert.Contains(tools, t => t.Name == "slow_echo");
    }

    [Fact]
    public async Task Tool_CanCloseStandaloneSseStream_ViaRequestContext()
    {
        // Arrange
        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.EventStore = new InMemoryEventStore();
            })
            .WithTools([McpServerTool.Create(
                (RequestContext<CallToolRequestParams> context, string message) =>
                {
                    // Close the standalone (GET) SSE stream
                    context.CloseStandaloneSseStream();
                    return $"Standalone stream closed: {message}";
                },
                new() { Name = "close_standalone" })]);

        var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var _ = app;

        await using var client = await ConnectClientAsync();

        // Act - Call the tool that closes the standalone stream
        var result = await client.CallToolAsync("close_standalone",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Tool call should complete successfully
        Assert.NotNull(result);
        var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Contains("Standalone stream closed: test", textContent.Text);
    }

    #endregion

    #region SSE Format Verification Tests (require raw HTTP)

    [Fact]
    public async Task Server_IncludesEventIdAndRetry_InSseResponse()
    {
        // Arrange
        var (app, _) = await CreateServerWithEventStoreAsync(retryInterval: TimeSpan.FromSeconds(5));
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
    public async Task Server_DoesNotSendPrimingEvents_ToOlderProtocolVersionClients()
    {
        // Arrange - Server with resumability enabled
        var (app, eventStore) = await CreateServerWithEventStoreAsync(retryInterval: TimeSpan.FromSeconds(5));
        await using var _ = app;

        // Use an older protocol version that doesn't support resumability
        var oldProtocolInitRequest = """
            {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"OldClient","version":"1.0.0"}}}
            """;

        using var requestContent = new StringContent(oldProtocolInitRequest, Encoding.UTF8, "application/json");
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

        // Read SSE fields with timeout
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        var sseFields = new SseFields();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            while (await reader.ReadLineAsync(timeoutCts.Token) is { } line)
            {
                if (string.IsNullOrEmpty(line)) break;

                if (line.StartsWith("id:", StringComparison.Ordinal))
                    sseFields.EventId = line["id:".Length..].Trim();
                else if (line.StartsWith("retry:", StringComparison.Ordinal))
                    sseFields.RetryValue = line["retry:".Length..].Trim();
            }
        }
        catch (OperationCanceledException) { /* Expected - stream may stay open */ }

        // Assert - Old clients should not receive event IDs or retry fields (no priming events)
        Assert.False(sseFields.HasEventId, "Old protocol clients should not receive event IDs");
        Assert.False(sseFields.HasRetry, "Old protocol clients should not receive retry field");

        // Event store should not have been called for old clients
        Assert.Equal(0, eventStore.StoreEventCallCount);
    }

    [Fact]
    public async Task Client_ReceivesRetryInterval_FromServer()
    {
        // Arrange - Server with specific retry interval
        var expectedRetryMs = 3000;
        var (app, _) = await CreateServerWithEventStoreAsync(retryInterval: TimeSpan.FromMilliseconds(expectedRetryMs));
        await using var _ = app;

        // Act - Send initialize and read the retry field
        var sseFields = await SendInitializeAndReadSseFieldsAsync();

        // Assert - Client receives the retry interval from server
        Assert.True(sseFields.HasRetry, "Expected retry field in SSE response");
        Assert.Equal(expectedRetryMs.ToString(), sseFields.RetryValue);
    }

    #endregion

    #region Test Tools

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
    }

    #endregion

    #region Helpers

    private async Task<(WebApplication App, InMemoryEventStore EventStore)> CreateServerWithEventStoreAsync(TimeSpan? retryInterval = null)
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
    /// This is needed for tests that verify SSE format details that McpClient abstracts away.
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
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            while (await reader.ReadLineAsync(timeoutCts.Token) is { } line)
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
        }
        catch (OperationCanceledException) { /* Expected - stream may stay open */ }

        return result;
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

    #endregion
}
