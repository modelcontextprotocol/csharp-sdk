using System.Net.ServerSentEvents;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Integration tests for SSE resumability using the in-memory <see cref="TestSseEventStreamStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class extends <see cref="ResumabilityIntegrationTestsBase"/> to add assertions specific to
/// <see cref="TestSseEventStreamStore"/>, such as verifying event counts and stored data.
/// </para>
/// <para>
/// Tests that don't require <see cref="TestSseEventStreamStore"/>-specific assertions are inherited
/// from the base class.
/// </para>
/// </remarks>
public class ResumabilityIntegrationTests(ITestOutputHelper testOutputHelper) : ResumabilityIntegrationTestsBase(testOutputHelper)
{
    /// <summary>
    /// Gets the test event stream store, cast to the concrete type for test-specific assertions.
    /// </summary>
    private TestSseEventStreamStore TestEventStreamStore => (TestSseEventStreamStore)EventStreamStore!;

    /// <inheritdoc />
    protected override ValueTask<ISseEventStreamStore> CreateEventStreamStoreAsync()
        => new(new TestSseEventStreamStore());

    [Fact]
    public override async Task Server_StoresEvents_WhenEventStoreConfigured()
    {
        await base.Server_StoresEvents_WhenEventStoreConfigured();

        // Additional assertion: verify events were actually stored
        Assert.True(TestEventStreamStore.StoreEventCallCount > 0, "Expected events to be stored when EventStore is configured");
    }

    [Fact]
    public override async Task Client_CanMakeMultipleRequests_WithResumabilityEnabled()
    {
        await base.Client_CanMakeMultipleRequests_WithResumabilityEnabled();

        // Additional assertion: verify events were stored for each request
        Assert.True(TestEventStreamStore.StoreEventCallCount >= 5, "Expected events to be stored for each request");
    }

    [Fact]
    public async Task Server_StoresMultipleEvents_ForMultipleToolCalls()
    {
        // Arrange
        await using var app = await CreateServerAsync();
        await using var client = await ConnectClientAsync();

        // Act - Make multiple tool calls
        var initialCount = TestEventStreamStore.StoreEventCallCount;

        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test1" },
            cancellationToken: TestContext.Current.CancellationToken);

        var countAfterFirst = TestEventStreamStore.StoreEventCallCount;

        await client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test2" },
            cancellationToken: TestContext.Current.CancellationToken);

        var countAfterSecond = TestEventStreamStore.StoreEventCallCount;

        // Assert - More events were stored for each call
        Assert.True(countAfterFirst > initialCount, "Expected more events after first call");
        Assert.True(countAfterSecond > countAfterFirst, "Expected more events after second call");
    }

    [Fact]
    public override async Task EnablePollingAsync_SendsSseItemWithRetryField()
    {
        await base.EnablePollingAsync_SendsSseItemWithRetryField();

        // Additional assertion: verify that the event store received the retry interval
        var expectedRetryInterval = TimeSpan.FromSeconds(5);
        Assert.Contains(expectedRetryInterval, TestEventStreamStore.StoredReconnectionIntervals);
    }

    [Fact]
    public async Task Server_WithoutEventStore_DoesNotIncludeEventId()
    {
        // Arrange - Server without event store (pass null explicitly)
        await using var app = await CreateServerAsync(eventStreamStore: null);

        // Act
        var sseResponse = await SendInitializeAndReadSseResponseAsync(InitializeRequest);

        // Assert - No event IDs or retry field when EventStore is not configured
        Assert.True(sseResponse.LastEventId is null, "Did not expect event IDs when EventStore is not configured");
    }

    [Fact]
    public async Task Server_DoesNotSendPrimingEvents_ToOlderProtocolVersionClients()
    {
        // Arrange - Server with resumability enabled
        await using var app = await CreateServerAsync();

        // Use an older protocol version that doesn't support resumability
        const string OldProtocolInitRequest = """
            {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"OldClient","version":"1.0.0"}}}
            """;

        var sseResponse = await SendInitializeAndReadSseResponseAsync(OldProtocolInitRequest);

        // Assert - Old clients should not receive event IDs or retry fields (no priming events)
        Assert.True(sseResponse.LastEventId is null, "Old protocol clients should not receive event IDs");

        // Event store should not have been called for old clients
        Assert.Equal(0, TestEventStreamStore.StoreEventCallCount);
    }

    [Fact]
    public async Task PostResponse_EndsAndSseEventStreamWriterIsDisposed_WhenWriteEventAsyncIsCanceled()
    {
        var blockingStore = new BlockingEventStreamStore();
        await using var app = await CreateServerAsync(blockingStore);
        await using var client = await ConnectClientAsync();

        // Enable blocking now that initialization is complete
        blockingStore.EnableBlocking();

        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Start calling the tool - this will eventually trigger WriteEventAsync for the response
        var callTask = client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: callCts.Token).AsTask();

        // Wait for the writer to block on WriteEventAsync for the response message
        await blockingStore.WriteEventBlockedTask.WaitAsync(TestContext.Current.CancellationToken);

        // Cancel the token while the writer is blocked - this causes an OCE to bubble up
        // to SendMessageAsync
        await callCts.CancelAsync();

        // The call should complete (with an error or cancellation) without hanging
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        // The call task should throw an OCE due to cancellation
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callTask).WaitAsync(timeoutCts.Token);

        // Wait for the writer to be disposed
        await blockingStore.DisposedTask.WaitAsync(timeoutCts.Token);
    }

    /// <summary>
    /// A test event stream store that blocks on WriteEventAsync for response messages,
    /// allowing the test to cancel the operation and verify proper cleanup.
    /// </summary>
    private sealed class BlockingEventStreamStore : ISseEventStreamStore
    {
        private readonly TaskCompletionSource _writeEventBlockedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _blockingEnabled;

        public Task WriteEventBlockedTask => _writeEventBlockedTcs.Task;
        public Task DisposedTask => _disposedTcs.Task;

        public void EnableBlocking() => _blockingEnabled = true;

        public ValueTask<ISseEventStreamWriter> CreateStreamAsync(SseEventStreamOptions options, CancellationToken cancellationToken = default)
            => new(new BlockingEventStreamWriter(this));

        public ValueTask<ISseEventStreamReader?> GetStreamReaderAsync(string lastEventId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test store does not support reading streams.");

        private sealed class BlockingEventStreamWriter : ISseEventStreamWriter
        {
            private readonly BlockingEventStreamStore _store;

            public BlockingEventStreamWriter(BlockingEventStreamStore store)
            {
                _store = store;
            }

            public ValueTask SetModeAsync(SseEventStreamMode mode, CancellationToken cancellationToken = default) => default;

            public async ValueTask<SseItem<JsonRpcMessage?>> WriteEventAsync(SseItem<JsonRpcMessage?> sseItem, CancellationToken cancellationToken = default)
            {
                // Skip if already has an event ID (replay)
                if (sseItem.EventId is not null)
                {
                    return sseItem;
                }

                // Block when we receive a response and blocking is enabled
                if (sseItem.Data is JsonRpcResponse && _store._blockingEnabled)
                {
                    // Signal that we're blocked
                    _store._writeEventBlockedTcs.TrySetResult();

                    // Wait to be canceled
                    await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
                }

                return sseItem with { EventId = "0" };
            }

            public ValueTask DisposeAsync()
            {
                _store._disposedTcs.TrySetResult();
                return default;
            }
        }
    }
}
