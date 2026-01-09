using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Net.ServerSentEvents;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for the <see cref="ISseEventStreamStore"/> interface and <see cref="TestSseEventStreamStore"/> implementation.
/// </summary>
public class SseEventStreamStoreTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    #region CreateStreamAsync Tests

    #endregion

    #region WriteEventAsync Tests

    [Fact]
    public async Task WriteEventAsync_AssignsEventId_WhenNotPresent()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message = new JsonRpcRequest { Method = "test", Id = new RequestId("1") };
        var item = new SseItem<JsonRpcMessage?>(message, "message");

        var result = await writer.WriteEventAsync(item, CancellationToken);

        Assert.NotNull(result.EventId);
        Assert.Equal("1", result.EventId);
    }

    [Fact]
    public async Task WriteEventAsync_SkipsAssigningEventId_WhenAlreadyPresent()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message = new JsonRpcRequest { Method = "test", Id = new RequestId("1") };
        var item = new SseItem<JsonRpcMessage?>(message, "message") { EventId = "existing-id" };

        var result = await writer.WriteEventAsync(item, CancellationToken);

        Assert.Equal("existing-id", result.EventId);
    }

    [Fact]
    public async Task WriteEventAsync_GeneratesSequentialEventIds()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "test1", Id = new RequestId("1") };
        var message2 = new JsonRpcRequest { Method = "test2", Id = new RequestId("2") };
        var message3 = new JsonRpcRequest { Method = "test3", Id = new RequestId("3") };

        var result1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);
        var result2 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message2, "message"), CancellationToken);
        var result3 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message3, "message"), CancellationToken);

        Assert.Equal("1", result1.EventId);
        Assert.Equal("2", result2.EventId);
        Assert.Equal("3", result3.EventId);
    }

    [Fact]
    public async Task WriteEventAsync_TracksStoredEventIds()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "test1", Id = new RequestId("1") };
        var message2 = new JsonRpcRequest { Method = "test2", Id = new RequestId("2") };

        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message2, "message"), CancellationToken);

        Assert.Equal(2, store.StoreEventCallCount);
        Assert.Equal(["1", "2"], store.StoredEventIds);
    }

    [Fact]
    public async Task WriteEventAsync_PreservesEventData()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message = new JsonRpcRequest { Method = "test-method", Id = new RequestId("req-1") };
        var item = new SseItem<JsonRpcMessage?>(message, "custom-event");

        var result = await writer.WriteEventAsync(item, CancellationToken);

        Assert.Same(message, result.Data);
        Assert.Equal("custom-event", result.EventType);
    }

    [Fact]
    public async Task WriteEventAsync_HandlesNullData()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var item = new SseItem<JsonRpcMessage?>(null, "priming");

        var result = await writer.WriteEventAsync(item, CancellationToken);

        Assert.NotNull(result.EventId);
        Assert.Null(result.Data);
    }

    #endregion

    #region GetStreamReaderAsync Tests

    [Fact]
    public async Task GetStreamReaderAsync_ReturnsNull_WhenEventIdNotFound()
    {
        var store = new TestSseEventStreamStore();

        var reader = await store.GetStreamReaderAsync("nonexistent-id", CancellationToken);

        Assert.Null(reader);
    }

    [Fact]
    public async Task GetStreamReaderAsync_ReturnsReader_WhenEventIdExists()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var message = new JsonRpcRequest { Method = "test", Id = new RequestId("1") };
        var result = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message, "message"), CancellationToken);

        var reader = await store.GetStreamReaderAsync(result.EventId!, CancellationToken);

        Assert.NotNull(reader);
        Assert.Equal("stream-1", reader.StreamId);
    }

    [Fact]
    public async Task GetStreamReaderAsync_Throws_WhenStreamReplaced()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Create a new stream with the same key, effectively replacing the old one
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken));
    }

    #endregion

    #region ReadEventsAsync Tests

    [Fact]
    public async Task ReadEventsAsync_ReturnsEventsAfterLastEventId()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "test1", Id = new RequestId("1") };
        var message2 = new JsonRpcRequest { Method = "test2", Id = new RequestId("2") };
        var message3 = new JsonRpcRequest { Method = "test3", Id = new RequestId("3") };

        var result1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message2, "message"), CancellationToken);
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message3, "message"), CancellationToken);

        var reader = await store.GetStreamReaderAsync(result1.EventId!, CancellationToken);
        Assert.NotNull(reader);

        var events = await reader.ReadEventsAsync(CancellationToken).ToListAsync(CancellationToken);

        Assert.Equal(2, events.Count);
        Assert.Equal("test2", ((JsonRpcRequest)events[0].Data!).Method);
        Assert.Equal("test3", ((JsonRpcRequest)events[1].Data!).Method);
    }

    [Fact]
    public async Task ReadEventsAsync_IncludesNullDataEvents()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "test1", Id = new RequestId("1") };
        var message2 = new JsonRpcRequest { Method = "test2", Id = new RequestId("2") };

        var result1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null, "priming"), CancellationToken); // null data event
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message2, "message"), CancellationToken);

        var reader = await store.GetStreamReaderAsync(result1.EventId!, CancellationToken);
        Assert.NotNull(reader);

        var events = await reader.ReadEventsAsync(CancellationToken).ToListAsync(CancellationToken);

        Assert.Equal(2, events.Count);
        Assert.Null(events[0].Data);
        Assert.Equal("priming", events[0].EventType);
        Assert.Equal("test2", ((JsonRpcRequest)events[1].Data!).Method);
    }

    [Fact]
    public async Task ReadEventsAsync_ReturnsEmpty_WhenNoEventsAfterLastEventId()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var message = new JsonRpcRequest { Method = "test", Id = new RequestId("1") };
        var result = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message, "message"), CancellationToken);

        var reader = await store.GetStreamReaderAsync(result.EventId!, CancellationToken);
        Assert.NotNull(reader);

        var events = await reader.ReadEventsAsync(CancellationToken).ToListAsync(CancellationToken);

        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadEventsAsync_InPollingMode_CompletesAfterStoredEvents()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "test1", Id = new RequestId("1") };
        var message2 = new JsonRpcRequest { Method = "test2", Id = new RequestId("2") };

        var result1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message2, "message"), CancellationToken);

        var reader = await store.GetStreamReaderAsync(result1.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // In polling mode, ReadEventsAsync should complete immediately after returning stored events
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(1));
        var events = await reader.ReadEventsAsync(cts.Token).ToListAsync(cts.Token);

        Assert.Single(events);
    }

    [Fact]
    public async Task ReadEventsAsync_InDefaultMode_WaitsForNewEvents()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "test1", Id = new RequestId("1") };
        var result1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);

        var reader = await store.GetStreamReaderAsync(result1.EventId!, CancellationToken);
        Assert.NotNull(reader);

        var readTask = Task.Run(async () =>
        {
            var events = new List<SseItem<JsonRpcMessage?>>();
            await foreach (var e in reader.ReadEventsAsync(CancellationToken))
            {
                events.Add(e);
                if (events.Count >= 2)
                {
                    break;
                }
            }
            return events;
        }, CancellationToken);

        // Give the read task time to start waiting
        await Task.Delay(50, CancellationToken);

        // Write new events
        var message2 = new JsonRpcRequest { Method = "test2", Id = new RequestId("2") };
        var message3 = new JsonRpcRequest { Method = "test3", Id = new RequestId("3") };
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message2, "message"), CancellationToken);
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message3, "message"), CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var events = await readTask.WaitAsync(cts.Token);

        Assert.Equal(2, events.Count);
        Assert.Equal("test2", ((JsonRpcRequest)events[0].Data!).Method);
        Assert.Equal("test3", ((JsonRpcRequest)events[1].Data!).Method);
    }

    [Fact]
    public async Task ReadEventsAsync_InDefaultMode_CompletesWhenWriterDisposed()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "test1", Id = new RequestId("1") };
        var result1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);

        var reader = await store.GetStreamReaderAsync(result1.EventId!, CancellationToken);
        Assert.NotNull(reader);

        var readTask = reader.ReadEventsAsync(CancellationToken).ToListAsync(CancellationToken).AsTask();

        // Give the read task time to start waiting
        await Task.Delay(50, CancellationToken);

        // Dispose the writer
        await writer.DisposeAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var events = await readTask.WaitAsync(cts.Token);

        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadEventsAsync_RespectsCancellation()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "test1", Id = new RequestId("1") };
        var result1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);

        var reader = await store.GetStreamReaderAsync(result1.EventId!, CancellationToken);
        Assert.NotNull(reader);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await reader.ReadEventsAsync(cts.Token).ToListAsync(cts.Token);
        });
    }

    #endregion

    #region Cross-Session Tests

    [Fact]
    public async Task EventIds_AreUniqueAcrossSessions()
    {
        var store = new TestSseEventStreamStore();

        var writer1 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var writer2 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-2",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "test1", Id = new RequestId("1") };
        var message2 = new JsonRpcRequest { Method = "test2", Id = new RequestId("2") };

        var result1 = await writer1.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);
        var result2 = await writer2.WriteEventAsync(new SseItem<JsonRpcMessage?>(message2, "message"), CancellationToken);

        Assert.NotEqual(result1.EventId, result2.EventId);
    }

    [Fact]
    public async Task GetStreamReaderAsync_ReturnsCorrectStream_ForDifferentSessions()
    {
        var store = new TestSseEventStreamStore();

        var writer1 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var writer2 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-2",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var message1 = new JsonRpcRequest { Method = "session1-test", Id = new RequestId("1") };
        var message2 = new JsonRpcRequest { Method = "session2-test", Id = new RequestId("2") };
        var message1b = new JsonRpcRequest { Method = "session1-test2", Id = new RequestId("3") };
        var message2b = new JsonRpcRequest { Method = "session2-test2", Id = new RequestId("4") };

        var result1 = await writer1.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1, "message"), CancellationToken);
        var result2 = await writer2.WriteEventAsync(new SseItem<JsonRpcMessage?>(message2, "message"), CancellationToken);
        await writer1.WriteEventAsync(new SseItem<JsonRpcMessage?>(message1b, "message"), CancellationToken);
        await writer2.WriteEventAsync(new SseItem<JsonRpcMessage?>(message2b, "message"), CancellationToken);

        var reader1 = await store.GetStreamReaderAsync(result1.EventId!, CancellationToken);
        var reader2 = await store.GetStreamReaderAsync(result2.EventId!, CancellationToken);

        Assert.NotNull(reader1);
        Assert.NotNull(reader2);

        var events1 = await reader1.ReadEventsAsync(CancellationToken).ToListAsync(CancellationToken);
        var events2 = await reader2.ReadEventsAsync(CancellationToken).ToListAsync(CancellationToken);

        Assert.Single(events1);
        Assert.Equal("session1-test2", ((JsonRpcRequest)events1[0].Data!).Method);

        Assert.Single(events2);
        Assert.Equal("session2-test2", ((JsonRpcRequest)events2[0].Data!).Method);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_CompletesWithoutError()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        await writer.DisposeAsync();
        // Should not throw
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var store = new TestSseEventStreamStore();
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        await writer.DisposeAsync();
        await writer.DisposeAsync();
        await writer.DisposeAsync();
        // Should not throw
    }

    #endregion
}
