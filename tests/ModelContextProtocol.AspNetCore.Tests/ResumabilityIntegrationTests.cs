using System.ComponentModel;
using System.Net.ServerSentEvents;
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

    [Fact]
    public async Task Server_StoresEvents_WhenEventStoreConfigured()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        await using var app = await CreateServerAsync(eventStore);
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
        var eventStore = new InMemoryEventStore();
        await using var app = await CreateServerAsync(eventStore);
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
        var eventStore = new InMemoryEventStore();
        await using var app = await CreateServerAsync(eventStore);
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
        var eventStore = new InMemoryEventStore();
        await using var app = await CreateServerAsync(eventStore);
        await using var client = await ConnectClientAsync();

        // Act & Assert - Ping should work
        await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListTools_WorksWithResumabilityEnabled()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        await using var app = await CreateServerAsync(eventStore);
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
        var eventStore = new InMemoryEventStore();
        await using var app = await CreateServerAsync(eventStore, retryInterval: expectedRetryInterval);

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
        var eventStore = new InMemoryEventStore();
        await using var app = await CreateServerAsync(eventStore, retryInterval: TimeSpan.FromSeconds(5));

        // Use an older protocol version that doesn't support resumability
        const string OldProtocolInitRequest = """
            {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"OldClient","version":"1.0.0"}}}
            """;

        var sseResponse = await SendInitializeAndReadSseResponseAsync(OldProtocolInitRequest);

        // Assert - Old clients should not receive event IDs or retry fields (no priming events)
        Assert.True(sseResponse.LastEventId is null, "Old protocol clients should not receive event IDs");
        Assert.True(sseResponse.RetryInterval is null, "Old protocol clients should not receive retry field");

        // Event store should not have been called for old clients
        Assert.Equal(0, eventStore.StoreEventCallCount);
    }

    [Fact]
    public async Task Client_ReceivesRetryInterval_FromServer()
    {
        // Arrange - Server with specific retry interval
        var expectedRetry = TimeSpan.FromMilliseconds(3000);
        var eventStore = new InMemoryEventStore();
        await using var app = await CreateServerAsync(eventStore, retryInterval: expectedRetry);

        // Act - Send initialize and read the retry field
        var sseItem = await SendInitializeAndReadSseResponseAsync(InitializeRequest);

        // Assert - Client receives the retry interval from server
        Assert.Equal(expectedRetry, sseItem.RetryInterval);
    }

    [McpServerToolType]
    private class ResumabilityTestTools
    {
        [McpServerTool(Name = "echo"), Description("Echoes the message back")]
        public static string Echo(string message) => $"Echo: {message}";
    }

    private async Task<WebApplication> CreateServerAsync(ISseEventStore? eventStore = null, TimeSpan? retryInterval = null)
    {
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
