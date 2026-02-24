using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Net.ServerSentEvents;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Tests for <see cref="DistributedCacheEventStreamStore"/>.
/// </summary>
public class DistributedCacheEventStreamStoreTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static IDistributedCache CreateMemoryCache()
    {
        var options = Options.Create(new MemoryDistributedCacheOptions());
        return new MemoryDistributedCache(options);
    }

    private static DistributedCacheEventStreamStore CreateStore(IDistributedCache? cache = null, DistributedCacheEventStreamStoreOptions? storeOptions = null)
    {
        storeOptions ??= new();
        storeOptions.Cache ??= cache ?? CreateMemoryCache();
        return new DistributedCacheEventStreamStore(Options.Create(storeOptions));
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>("options", () => new DistributedCacheEventStreamStore(null!));
    }

    [Fact]
    public void Constructor_ThrowsInvalidOperationException_WhenCacheIsNull()
    {
        var options = Options.Create(new DistributedCacheEventStreamStoreOptions());
        var ex = Assert.Throws<InvalidOperationException>(() => new DistributedCacheEventStreamStore(options));
        Assert.StartsWith($"The '{nameof(DistributedCacheEventStreamStoreOptions)}.{nameof(DistributedCacheEventStreamStoreOptions.Cache)}'", ex.Message);
    }

    [Fact]
    public async Task CreateStreamAsync_ThrowsArgumentNullException_WhenOptionsIsNull()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>("options",
            async () => await store.CreateStreamAsync(null!, CancellationToken));
    }

    [Fact]
    public async Task WriteEventAsync_AssignsUniqueEventId_WhenItemHasNoEventId()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var item = new SseItem<JsonRpcMessage?>(null);

        // Act
        var result = await writer.WriteEventAsync(item, CancellationToken);

        // Assert
        Assert.NotNull(result.EventId);
        Assert.NotEmpty(result.EventId);
    }

    [Fact]
    public async Task WriteEventAsync_SkipsAssigningEventId_WhenItemAlreadyHasEventId()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var existingEventId = "existing-event-id";
        var item = new SseItem<JsonRpcMessage?>(null) { EventId = existingEventId };

        // Act
        var result = await writer.WriteEventAsync(item, CancellationToken);

        // Assert
        Assert.Equal(existingEventId, result.EventId);
    }

    [Fact]
    public async Task WriteEventAsync_PreservesDataProperty_InReturnedItem()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var message = new JsonRpcNotification { Method = "test/notification" };
        var item = new SseItem<JsonRpcMessage?>(message);

        // Act
        var result = await writer.WriteEventAsync(item, CancellationToken);

        // Assert - Data should be preserved in the returned item (same reference)
        Assert.Same(message, result.Data);
    }

    [Fact]
    public async Task WriteEventAsync_PreservesEventTypeProperty_InReturnedItem()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var item = new SseItem<JsonRpcMessage?>(null, "custom-event-type");

        // Act
        var result = await writer.WriteEventAsync(item, CancellationToken);

        // Assert
        Assert.Equal("custom-event-type", result.EventType);
    }

    [Fact]
    public async Task WriteEventAsync_PreservesReconnectionIntervalProperty_InStoredEvent()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var expectedInterval = TimeSpan.FromSeconds(5);
        var item = new SseItem<JsonRpcMessage?>(null) { ReconnectionInterval = expectedInterval };

        // Act
        var result = await writer.WriteEventAsync(item, CancellationToken);

        // Assert - ReconnectionInterval should be preserved in returned item
        Assert.Equal(expectedInterval, result.ReconnectionInterval);

        // Get a reader and verify ReconnectionInterval is preserved after round-trip
        var reader = await store.GetStreamReaderAsync(result.EventId!, CancellationToken);
        Assert.NotNull(reader);

        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        // Reader should not return the event we just wrote (it starts after lastEventId)
        Assert.Empty(events);

        // Write another event and verify it can be read with correct ReconnectionInterval
        var secondItem = new SseItem<JsonRpcMessage?>(null) { ReconnectionInterval = TimeSpan.FromSeconds(10) };
        _ = await writer.WriteEventAsync(secondItem, CancellationToken);

        // Re-fetch reader using the first event ID to get the second event
        reader = await store.GetStreamReaderAsync(result.EventId!, CancellationToken);
        Assert.NotNull(reader);

        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        Assert.Single(events);
        Assert.Equal(TimeSpan.FromSeconds(10), events[0].ReconnectionInterval);
    }

    [Fact]
    public async Task WriteEventAsync_HandlesNullReconnectionInterval_InStoredEvent()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Write an event WITH a reconnection interval first
        var firstItem = new SseItem<JsonRpcMessage?>(null) { ReconnectionInterval = TimeSpan.FromSeconds(5) };
        var firstResult = await writer.WriteEventAsync(firstItem, CancellationToken);

        // Write an event WITHOUT a reconnection interval
        var secondItem = new SseItem<JsonRpcMessage?>(null);
        var secondResult = await writer.WriteEventAsync(secondItem, CancellationToken);
        Assert.Null(secondResult.ReconnectionInterval);

        // Get a reader starting after the first event
        var reader = await store.GetStreamReaderAsync(firstResult.EventId!, CancellationToken);
        Assert.NotNull(reader);

        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        // Should get the second event with null ReconnectionInterval
        Assert.Single(events);
        Assert.Null(events[0].ReconnectionInterval);
    }

    [Fact]
    public async Task WriteEventAsync_HandlesNullData_AssignsEventIdAndStoresEvent()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var item = new SseItem<JsonRpcMessage?>(null);

        // Act
        var result = await writer.WriteEventAsync(item, CancellationToken);

        // Assert - Event ID should be assigned
        Assert.NotNull(result.EventId);

        // Assert - Event should be retrievable
        var reader = await store.GetStreamReaderAsync(result.EventId, CancellationToken);
        Assert.NotNull(reader);
    }

    [Fact]
    public async Task WriteEventAsync_StoresEventWithCorrectSlidingExpiration()
    {
        // Arrange - Use a mock cache to verify expiration options
        var mockCache = new TestDistributedCache();
        var customOptions = new DistributedCacheEventStreamStoreOptions
        {
            EventSlidingExpiration = TimeSpan.FromMinutes(15)
        };
        var store = CreateStore(mockCache, customOptions);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var item = new SseItem<JsonRpcMessage?>(null);

        // Act
        await writer.WriteEventAsync(item, CancellationToken);

        // Assert - Verify at least one call used the expected sliding expiration
        Assert.Contains(mockCache.SetCalls, call =>
            call.Key.Contains("event:") &&
            call.Options.SlidingExpiration == TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task WriteEventAsync_StoresEventWithCorrectAbsoluteExpiration()
    {
        // Arrange
        var mockCache = new TestDistributedCache();
        var customOptions = new DistributedCacheEventStreamStoreOptions
        {
            EventAbsoluteExpiration = TimeSpan.FromHours(3)
        };
        var store = CreateStore(mockCache, customOptions);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var item = new SseItem<JsonRpcMessage?>(null);

        // Act
        await writer.WriteEventAsync(item, CancellationToken);

        // Assert
        Assert.Contains(mockCache.SetCalls, call =>
            call.Key.Contains("event:") &&
            call.Options.AbsoluteExpirationRelativeToNow == TimeSpan.FromHours(3));
    }

    [Fact]
    public async Task WriteEventAsync_UpdatesStreamMetadata_AfterEachWrite()
    {
        // Arrange
        var mockCache = new TestDistributedCache();
        var store = CreateStore(mockCache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        var item = new SseItem<JsonRpcMessage?>(null);

        // Act
        await writer.WriteEventAsync(item, CancellationToken);

        // Assert - Metadata should have been updated
        Assert.Contains(mockCache.SetCalls, call => call.Key.Contains("meta:"));
    }

    [Fact]
    public async Task SetModeAsync_PersistsModeChangeToMetadata()
    {
        // Arrange
        var mockCache = new TestDistributedCache();
        var store = CreateStore(mockCache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        mockCache.SetCalls.Clear(); // Clear calls from CreateStreamAsync setup

        // Act
        await writer.SetModeAsync(SseEventStreamMode.Polling, CancellationToken);

        // Assert - Metadata should have been updated with the new mode
        Assert.Contains(mockCache.SetCalls, call => call.Key.Contains("meta:"));
    }

    [Fact]
    public async Task SetModeAsync_ModeChangeReflectedInReader()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var customOptions = new DistributedCacheEventStreamStoreOptions
        {
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(10)
        };
        var store = CreateStore(cache, customOptions);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Write an event to have something to read
        var item = new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "test" });
        var writtenItem = await writer.WriteEventAsync(item, CancellationToken);

        // Get a reader based on the event ID (starting at sequence 1, reader will wait for seq 2+)
        var reader = await store.GetStreamReaderAsync(writtenItem.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Act - Change mode to Polling while reader exists
        await writer.SetModeAsync(SseEventStreamMode.Polling, CancellationToken);

        // Assert - Reader should complete immediately in polling mode (no new events to read)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(cts.Token))
        {
            events.Add(evt);
        }

        // In polling mode, reader should complete without waiting for new events
        Assert.Empty(events); // No events after the one we used to create the reader
    }

    [Fact]
    public async Task DisposeAsync_MarksStreamAsCompleted()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Write an event so we can get a reader
        var item = new SseItem<JsonRpcMessage?>(null);
        var writtenItem = await writer.WriteEventAsync(item, CancellationToken);

        // Act
        await writer.DisposeAsync();

        // Assert - Reader should see the stream as completed and exit immediately
        var reader = await store.GetStreamReaderAsync(writtenItem.EventId!, CancellationToken);
        Assert.NotNull(reader);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(cts.Token))
        {
            events.Add(evt);
        }

        // The reader should complete without waiting for new events because stream is completed
        Assert.Empty(events); // No new events after the one we used to create the reader
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Act - Call DisposeAsync multiple times
        await writer.DisposeAsync();
        await writer.DisposeAsync();
        await writer.DisposeAsync();

        // Assert - No exception thrown, operation is idempotent
        // If we got here without exception, the test passes
    }

    [Fact]
    public async Task DisposeAsync_UpdatesMetadata_WithIsCompletedFlag()
    {
        // Arrange
        var mockCache = new TestDistributedCache();
        var store = CreateStore(mockCache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        mockCache.SetCalls.Clear(); // Clear calls from CreateStreamAsync

        // Act
        await writer.DisposeAsync();

        // Assert - Metadata should have been updated
        Assert.Contains(mockCache.SetCalls, call => call.Key.Contains("meta:"));
    }

    [Fact]
    public async Task GetStreamReaderAsync_ThrowsArgumentNullException_WhenLastEventIdIsNull()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>("lastEventId",
            async () => await store.GetStreamReaderAsync(null!, CancellationToken));
    }

    [Fact]
    public async Task GetStreamReaderAsync_ReturnsNull_WhenEventIdIsUnparseable()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);

        // Act - Try various invalid event ID formats
        var result1 = await store.GetStreamReaderAsync("invalid-format", CancellationToken);
        var result2 = await store.GetStreamReaderAsync("only:two:parts:here", CancellationToken);
        var result3 = await store.GetStreamReaderAsync("", CancellationToken);

        // Assert
        Assert.Null(result1);
        Assert.Null(result2);
        Assert.Null(result3);
    }

    [Fact]
    public async Task GetStreamReaderAsync_ReturnsNull_WhenStreamMetadataDoesNotExist()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);

        // Create a valid-looking event ID for a stream that doesn't exist
        var fakeEventId = DistributedCacheEventIdFormatter.Format("nonexistent-session", "nonexistent-stream", 1);

        // Act
        var reader = await store.GetStreamReaderAsync(fakeEventId, CancellationToken);

        // Assert
        Assert.Null(reader);
    }

    [Fact]
    public async Task GetStreamReaderAsync_ReturnsReaderWithCorrectSessionIdAndStreamId()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "my-session",
            StreamId = "my-stream",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Write an event to get a valid event ID
        var item = new SseItem<JsonRpcMessage?>(null);
        var writtenItem = await writer.WriteEventAsync(item, CancellationToken);

        // Act
        var reader = await store.GetStreamReaderAsync(writtenItem.EventId!, CancellationToken);

        // Assert
        Assert.NotNull(reader);
        Assert.Equal("my-session", reader.SessionId);
        Assert.Equal("my-stream", reader.StreamId);
    }

    [Fact]
    public async Task ReadEventsAsync_ReturnsEventsInOrder()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Write multiple events
        var event1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "method1" }), CancellationToken);
        var event2 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "method2" }), CancellationToken);
        var event3 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "method3" }), CancellationToken);

        // Create a reader starting from before the first event (use a fake event ID with sequence 0)
        var startEventId = DistributedCacheEventIdFormatter.Format("session-1", "stream-1", 0);
        var reader = await store.GetStreamReaderAsync(startEventId, CancellationToken);
        Assert.NotNull(reader);

        // Act
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        // Assert - Events should be in order
        Assert.Equal(3, events.Count);
        Assert.Equal(event1.EventId, events[0].EventId);
        Assert.Equal(event2.EventId, events[1].EventId);
        Assert.Equal(event3.EventId, events[2].EventId);
    }

    [Fact]
    public async Task ReadEventsAsync_ReturnsEmpty_WhenNoNewEventsExist()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Write one event
        var writtenItem = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Create a reader starting from the last event (so there are no new events to read)
        var reader = await store.GetStreamReaderAsync(writtenItem.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Act
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadEventsAsync_PreservesCorrectDataEventTypeAndEventId()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var message = new JsonRpcNotification { Method = "test/method" };
        var writtenItem = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(message, "custom-event-type"), CancellationToken);

        // Create a reader starting from before the event
        var startEventId = DistributedCacheEventIdFormatter.Format("session-1", "stream-1", 0);
        var reader = await store.GetStreamReaderAsync(startEventId, CancellationToken);
        Assert.NotNull(reader);

        // Act
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        // Assert
        Assert.Single(events);
        var readEvent = events[0];
        Assert.Equal(writtenItem.EventId, readEvent.EventId);
        Assert.Equal("custom-event-type", readEvent.EventType);

        var readMessage = Assert.IsType<JsonRpcNotification>(readEvent.Data);
        Assert.Equal("test/method", readMessage.Method);
    }

    [Fact]
    public async Task ReadEventsAsync_HandlesNullData()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var writtenItem = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Create a reader starting from before the event
        var startEventId = DistributedCacheEventIdFormatter.Format("session-1", "stream-1", 0);
        var reader = await store.GetStreamReaderAsync(startEventId, CancellationToken);
        Assert.NotNull(reader);

        // Act
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        // Assert
        Assert.Single(events);
        Assert.Null(events[0].Data);
        Assert.Equal(writtenItem.EventId, events[0].EventId);
    }

    [Fact]
    public async Task ReadEventsAsync_InPollingMode_CompletesImmediatelyAfterReturningAvailableEvents()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Write events
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Create a reader from sequence 0
        var startEventId = DistributedCacheEventIdFormatter.Format("session-1", "stream-1", 0);
        var reader = await store.GetStreamReaderAsync(startEventId, CancellationToken);
        Assert.NotNull(reader);

        // Act - Should complete quickly without waiting for new events
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }
        stopwatch.Stop();

        // Assert - Should have returned both events and completed quickly
        Assert.Equal(2, events.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Polling mode should complete quickly, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ReadEventsAsync_InPollingMode_ReturnsOnlyEventsAfterLastEventId()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Write 3 events
        var event1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var event2 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var event3 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Create a reader starting from event2 (should only return event3)
        var reader = await store.GetStreamReaderAsync(event2.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Act
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        // Assert - Only event3 should be returned
        Assert.Single(events);
        Assert.Equal(event3.EventId, events[0].EventId);
    }

    [Fact]
    public async Task ReadEventsAsync_InPollingMode_ReturnsEmptyIfNoNewEvents()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Write one event and create a reader from that event (no events after it)
        var writtenEvent = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var reader = await store.GetStreamReaderAsync(writtenEvent.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Act
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        // Assert - No new events should be returned
        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadEventsAsync_InPollingMode_DoesNotWaitForNewEvents()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Write one event so we have a valid event ID, then create reader from it
        var writtenEvent = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var reader = await store.GetStreamReaderAsync(writtenEvent.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Act - Should complete immediately without waiting (no new events after the one we started from)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }
        stopwatch.Stop();

        // Assert - Should complete quickly with no events
        Assert.Empty(events);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Polling mode should complete quickly, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ReadEventsAsync_InStreamingMode_WaitsForNewEvents()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache, new DistributedCacheEventStreamStoreOptions
        {
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(50)
        });
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Write one event so we have a valid event ID
        var writtenEvent = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var reader = await store.GetStreamReaderAsync(writtenEvent.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Act - Start reading and then write a new event
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var events = new List<SseItem<JsonRpcMessage?>>();
        var readTask = Task.Run(async () =>
        {
            await foreach (var evt in reader.ReadEventsAsync(cts.Token))
            {
                events.Add(evt);
                if (events.Count >= 1)
                {
                    // Got the event we were waiting for, cancel to stop
                    await cts.CancelAsync();
                }
            }
        }, CancellationToken);

        // Write a new event - the reader should pick it up since it's in streaming mode
        // and won't complete until cancelled or the stream is disposed
        var newEvent = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Wait for read to complete (either event received or timeout)
        try
        {
            await readTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when we cancel after receiving event
        }

        // Assert - Should have received the new event
        Assert.Single(events);
        Assert.Equal(newEvent.EventId, events[0].EventId);
    }

    [Fact]
    public async Task ReadEventsAsync_InStreamingMode_YieldsNewlyWrittenEvents()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache, new DistributedCacheEventStreamStoreOptions
        {
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(50)
        });
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Write initial event
        var initialEvent = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var reader = await store.GetStreamReaderAsync(initialEvent.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Act - Write multiple events while reader is active
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var events = new List<SseItem<JsonRpcMessage?>>();
        var readTask = Task.Run(async () =>
        {
            await foreach (var evt in reader.ReadEventsAsync(cts.Token))
            {
                events.Add(evt);
                if (events.Count >= 3)
                {
                    await cts.CancelAsync();
                }
            }
        }, CancellationToken);

        // Write 3 new events - the reader should pick them up since it's in streaming mode
        var event1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var event2 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var event3 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        try
        {
            await readTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Should have received all 3 events in order
        Assert.Equal(3, events.Count);
        Assert.Equal(event1.EventId, events[0].EventId);
        Assert.Equal(event2.EventId, events[1].EventId);
        Assert.Equal(event3.EventId, events[2].EventId);
    }

    [Fact]
    public async Task ReadEventsAsync_InStreamingMode_CompletesWhenStreamIsDisposed()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache, new DistributedCacheEventStreamStoreOptions
        {
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(50)
        });
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Write event to create a valid reader
        var writtenEvent = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var reader = await store.GetStreamReaderAsync(writtenEvent.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Act - Start reading, then dispose the stream
        var readTask = Task.Run(async () =>
        {
            await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
            {
            }
        }, CancellationToken);

        // Dispose the writer - the reader should detect this and exit gracefully
        await writer.DisposeAsync();

        // Assert - The read should complete gracefully within timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        await readTask.WaitAsync(timeoutCts.Token);
    }

    [Fact]
    public async Task ReadEventsAsync_InStreamingMode_RespectsCancellation()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache, new DistributedCacheEventStreamStoreOptions
        {
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(50)
        });
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Write event to create a valid reader
        var writtenEvent = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var reader = await store.GetStreamReaderAsync(writtenEvent.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Act - Start reading and then cancel
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var events = new List<SseItem<JsonRpcMessage?>>();
        var messageReceivedTcs = new TaskCompletionSource<bool>();
        var continueReadingTcs = new TaskCompletionSource<bool>();
        OperationCanceledException? capturedException = null;

        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in reader.ReadEventsAsync(cts.Token))
                {
                    events.Add(evt);
                    messageReceivedTcs.SetResult(true);
                    await continueReadingTcs.Task;
                }
            }
            catch (OperationCanceledException ex)
            {
                capturedException = ex;
            }
        }, CancellationToken);

        // Write a message for the reader to consume
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Wait for the first message to be received
        await messageReceivedTcs.Task;

        // Cancel so that ReadEventsAsync throws before reading the next message
        await cts.CancelAsync();

        // Allow the message reader to continue
        continueReadingTcs.SetResult(true);

        // Wait for read task to complete
        await readTask;

        Assert.Single(events);
        Assert.NotNull(capturedException);
    }

    [Fact]
    public async Task ReadEventsAsync_RespectsModeSwitchFromStreamingToPolling()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache, new DistributedCacheEventStreamStoreOptions
        {
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(50)
        });
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Write an event to create a valid reader
        var writtenEvent = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var reader = await store.GetStreamReaderAsync(writtenEvent.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Start reading in streaming mode (will wait for new events)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var events = new List<SseItem<JsonRpcMessage?>>();
        var readCompleted = false;

        var readTask = Task.Run(async () =>
        {
            await foreach (var evt in reader.ReadEventsAsync(cts.Token))
            {
                events.Add(evt);
            }
            readCompleted = true;
        }, CancellationToken);

        // Switch to polling mode - the reader should detect this and exit
        await writer.SetModeAsync(SseEventStreamMode.Polling, CancellationToken);

        // Assert - Read should complete within timeout after switching to polling mode
        await readTask.WaitAsync(cts.Token);
        Assert.True(readCompleted);
        Assert.Empty(events); // No new events were written after the one we used to create the reader
    }

    [Fact]
    public async Task ReadEventsAsync_PollingModeReturnsEventsThenCompletes()
    {
        // Arrange - Start in default mode, write some events, switch to polling, reader should return remaining events
        var cache = CreateMemoryCache();
        var store = CreateStore(cache, new DistributedCacheEventStreamStoreOptions
        {
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(50)
        });
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming
        }, CancellationToken);

        // Write initial event and create reader from sequence 0
        var startEventId = DistributedCacheEventIdFormatter.Format("session-1", "stream-1", 0);

        // Write events first
        var event1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var event2 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Switch to polling mode
        await writer.SetModeAsync(SseEventStreamMode.Polling, CancellationToken);

        // Get reader
        var reader = await store.GetStreamReaderAsync(startEventId, CancellationToken);
        Assert.NotNull(reader);

        // Act - Read should return events and complete immediately
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }
        stopwatch.Stop();

        // Assert
        Assert.Equal(2, events.Count);
        Assert.Equal(event1.EventId, events[0].EventId);
        Assert.Equal(event2.EventId, events[1].EventId);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Should complete quickly, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task MultipleStreams_AreIsolated_EventsDoNotLeakBetweenStreams()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);

        // Create two streams with different session/stream IDs
        var writer1 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var writer2 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-2",
            StreamId = "stream-2",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Write events to each stream
        var event1 = await writer1.WriteEventAsync(new SseItem<JsonRpcMessage?>(null, "event-from-stream1"), CancellationToken);
        var event2 = await writer2.WriteEventAsync(new SseItem<JsonRpcMessage?>(null, "event-from-stream2"), CancellationToken);

        // Create readers for each stream from sequence 0
        var start1 = DistributedCacheEventIdFormatter.Format("session-1", "stream-1", 0);
        var start2 = DistributedCacheEventIdFormatter.Format("session-2", "stream-2", 0);

        var reader1 = await store.GetStreamReaderAsync(start1, CancellationToken);
        var reader2 = await store.GetStreamReaderAsync(start2, CancellationToken);
        Assert.NotNull(reader1);
        Assert.NotNull(reader2);

        // Act - Read from each reader
        var events1 = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader1.ReadEventsAsync(CancellationToken))
        {
            events1.Add(evt);
        }

        var events2 = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader2.ReadEventsAsync(CancellationToken))
        {
            events2.Add(evt);
        }

        // Assert - Each reader should only see its own stream's events
        Assert.Single(events1);
        Assert.Equal("event-from-stream1", events1[0].EventType);
        Assert.Equal(event1.EventId, events1[0].EventId);

        Assert.Single(events2);
        Assert.Equal("event-from-stream2", events2[0].EventType);
        Assert.Equal(event2.EventId, events2[0].EventId);
    }

    [Fact]
    public async Task MultipleStreams_SameSession_DifferentStreamIds_AreIsolated()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);

        // Create two streams with same session but different stream IDs
        var writer1 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "shared-session",
            StreamId = "stream-A",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var writer2 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "shared-session",
            StreamId = "stream-B",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Write events to each stream
        await writer1.WriteEventAsync(new SseItem<JsonRpcMessage?>(null, "from-A"), CancellationToken);
        await writer2.WriteEventAsync(new SseItem<JsonRpcMessage?>(null, "from-B"), CancellationToken);

        // Create readers from sequence 0
        var reader1 = await store.GetStreamReaderAsync(DistributedCacheEventIdFormatter.Format("shared-session", "stream-A", 0), CancellationToken);
        var reader2 = await store.GetStreamReaderAsync(DistributedCacheEventIdFormatter.Format("shared-session", "stream-B", 0), CancellationToken);
        Assert.NotNull(reader1);
        Assert.NotNull(reader2);

        // Act
        var events1 = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader1.ReadEventsAsync(CancellationToken))
        {
            events1.Add(evt);
        }

        var events2 = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader2.ReadEventsAsync(CancellationToken))
        {
            events2.Add(evt);
        }

        // Assert
        Assert.Single(events1);
        Assert.Equal("from-A", events1[0].EventType);

        Assert.Single(events2);
        Assert.Equal("from-B", events2[0].EventType);
    }

    [Fact]
    public async Task EventIds_AreGloballyUnique_AcrossStreams()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var store = CreateStore(cache);

        var writer1 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var writer2 = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-2",
            StreamId = "stream-2",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Act - Write events to each stream
        var event1a = await writer1.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var event1b = await writer1.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var event2a = await writer2.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);
        var event2b = await writer2.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Assert - All event IDs should be unique
        var allEventIds = new[] { event1a.EventId, event1b.EventId, event2a.EventId, event2b.EventId };
        Assert.Equal(4, allEventIds.Distinct().Count());
    }

    [Fact]
    public async Task WriteEventAsync_UsesConfiguredSlidingExpiration()
    {
        // Arrange
        var mockCache = new TestDistributedCache();
        var customOptions = new DistributedCacheEventStreamStoreOptions
        {
            EventSlidingExpiration = TimeSpan.FromMinutes(30)
        };
        var store = CreateStore(mockCache, customOptions);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        mockCache.SetCalls.Clear();

        // Act
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Assert - Event should be written with the configured sliding expiration
        Assert.Contains(mockCache.SetCalls, call =>
            call.Key.Contains("event:") &&
            call.Options.SlidingExpiration == TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task WriteEventAsync_UsesConfiguredAbsoluteExpiration()
    {
        // Arrange
        var mockCache = new TestDistributedCache();
        var customOptions = new DistributedCacheEventStreamStoreOptions
        {
            EventAbsoluteExpiration = TimeSpan.FromHours(6)
        };
        var store = CreateStore(mockCache, customOptions);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        mockCache.SetCalls.Clear();

        // Act
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Assert - Event should be written with the configured absolute expiration (relative to now)
        var eventCall = mockCache.SetCalls.FirstOrDefault(call => call.Key.Contains("event:"));
        Assert.NotNull(eventCall.Key);
        Assert.NotNull(eventCall.Options.AbsoluteExpirationRelativeToNow);
        Assert.Equal(TimeSpan.FromHours(6), eventCall.Options.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task WriteEventAsync_UsesConfiguredMetadataExpiration()
    {
        // Arrange - Metadata is written when events are written
        var mockCache = new TestDistributedCache();
        var customOptions = new DistributedCacheEventStreamStoreOptions
        {
            MetadataSlidingExpiration = TimeSpan.FromMinutes(45),
            MetadataAbsoluteExpiration = TimeSpan.FromHours(12)
        };
        var store = CreateStore(mockCache, customOptions);
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        // Act - Write an event, which also updates metadata
        await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(null), CancellationToken);

        // Assert
        var metadataCall = mockCache.SetCalls.FirstOrDefault(call => call.Key.Contains("meta:"));
        Assert.NotNull(metadataCall.Key);
        Assert.Equal(TimeSpan.FromMinutes(45), metadataCall.Options.SlidingExpiration);
        Assert.Equal(TimeSpan.FromHours(12), metadataCall.Options.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public void DefaultOptions_HaveReasonableDefaults()
    {
        // Arrange & Act
        var options = new DistributedCacheEventStreamStoreOptions();

        // Assert - Check that defaults are set reasonably
        Assert.True(options.StreamReaderPollingInterval >= TimeSpan.FromMilliseconds(50), "Polling interval should be at least 50ms");
        Assert.True(options.EventSlidingExpiration > TimeSpan.Zero, "Event sliding expiration should be positive");
        Assert.True(options.EventAbsoluteExpiration > TimeSpan.Zero, "Event absolute expiration should be positive");
        Assert.True(options.MetadataSlidingExpiration > TimeSpan.Zero, "Metadata sliding expiration should be positive");
        Assert.True(options.MetadataAbsoluteExpiration > TimeSpan.Zero, "Metadata absolute expiration should be positive");
    }

    [Fact]
    public async Task ReadEventsAsync_ThrowsMcpException_WhenMetadataExpires()
    {
        // Arrange - Use a cache that allows us to simulate metadata expiration
        var trackingCache = new TestDistributedCache();
        var customOptions = new DistributedCacheEventStreamStoreOptions
        {
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(10) // Fast polling to detect the bug quickly
        };
        var store = CreateStore(trackingCache, customOptions);

        // Create a stream and write an event
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Streaming // Non-polling mode to trigger the waiting loop
        }, CancellationToken);

        var item = new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "test" });
        var writtenItem = await writer.WriteEventAsync(item, CancellationToken);

        // Get a reader starting after the first event (so it will wait for more events)
        var reader = await store.GetStreamReaderAsync(writtenItem.EventId!, CancellationToken);
        Assert.NotNull(reader);

        // Now simulate metadata expiration
        trackingCache.ExpireMetadata();

        // Act & Assert - Reader should throw McpException when metadata expires
        var exception = await Assert.ThrowsAsync<McpException>(async () =>
        {
            await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
            {
                // Should not yield any events before throwing
            }
        });

        Assert.Contains("session-1", exception.Message);
        Assert.Contains("stream-1", exception.Message);
        Assert.Contains("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadEventsAsync_ThrowsMcpException_WhenEventExpires()
    {
        // Arrange - Use a cache that allows us to simulate event expiration
        var trackingCache = new TestDistributedCache();
        var store = CreateStore(trackingCache);

        // Create a stream and write multiple events
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var event1 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "method1" }), CancellationToken);
        var event2 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "method2" }), CancellationToken);
        var event3 = await writer.WriteEventAsync(new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "method3" }), CancellationToken);

        // Create a reader starting from before the first event
        var startEventId = DistributedCacheEventIdFormatter.Format("session-1", "stream-1", 0);
        var reader = await store.GetStreamReaderAsync(startEventId, CancellationToken);
        Assert.NotNull(reader);

        // Simulate event2 expiring from the cache
        trackingCache.ExpireEvent(event2.EventId!);

        // Act & Assert - Reader should throw McpException when an event is missing
        var exception = await Assert.ThrowsAsync<McpException>(async () =>
        {
            var events = new List<SseItem<JsonRpcMessage?>>();
            await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
            {
                events.Add(evt);
            }
        });

        Assert.Contains(event2.EventId!, exception.Message);
        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadEventsAsync_DoesNotReadMetadata_InPollingMode()
    {
        // Arrange - Use a tracking cache to count metadata reads
        var trackingCache = new TestDistributedCache();
        var customOptions = new DistributedCacheEventStreamStoreOptions
        {
            StreamReaderPollingInterval = TimeSpan.FromMilliseconds(10)
        };
        var store = CreateStore(trackingCache, customOptions);

        // Create a stream in POLLING mode - this allows the reader to exit after reading available events
        var writer = await store.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = "session-1",
            StreamId = "stream-1",
            Mode = SseEventStreamMode.Polling
        }, CancellationToken);

        var item1 = new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "test1" });
        var item2 = new SseItem<JsonRpcMessage?>(new JsonRpcNotification { Method = "test2" });
        await writer.WriteEventAsync(item1, CancellationToken);
        await writer.WriteEventAsync(item2, CancellationToken);

        // Get a reader starting before all events (use a fake event ID at sequence 0)
        var zeroSequenceEventId = DistributedCacheEventIdFormatter.Format("session-1", "stream-1", 0);
        var reader = await store.GetStreamReaderAsync(zeroSequenceEventId, CancellationToken);
        Assert.NotNull(reader);

        // GetStreamReaderAsync should have read metadata exactly once
        Assert.Equal(1, trackingCache.MetadataReadCount);

        // Act - Read all events
        var events = new List<SseItem<JsonRpcMessage?>>();
        await foreach (var evt in reader.ReadEventsAsync(CancellationToken))
        {
            events.Add(evt);
        }

        // Assert - In polling mode, the reader should:
        // 1. Use initial metadata from GetStreamReaderAsync (no additional read needed)
        // 2. Read all available events (2 events)
        // 3. Exit immediately because mode is Polling
        //
        // Metadata read count should remain at 1 (only the initial read from GetStreamReaderAsync)
        Assert.Equal(2, events.Count);
        Assert.Equal(1, trackingCache.MetadataReadCount);
    }

    [Fact]
    public void EventIdFormatter_Format_CreatesValidEventId()
    {
        // Act
        var eventId = DistributedCacheEventIdFormatter.Format("session-1", "stream-1", 42);

        // Assert
        Assert.NotNull(eventId);
        Assert.NotEmpty(eventId);
        Assert.Contains(":", eventId); // Should contain separators
    }

    [Fact]
    public void EventIdFormatter_TryParse_RoundTripsSuccessfully()
    {
        // Arrange
        var originalSessionId = "my-session-id";
        var originalStreamId = "my-stream-id";
        var originalSequence = 12345L;

        // Act
        var eventId = DistributedCacheEventIdFormatter.Format(originalSessionId, originalStreamId, originalSequence);
        var parsed = DistributedCacheEventIdFormatter.TryParse(eventId, out var sessionId, out var streamId, out var sequence);

        // Assert
        Assert.True(parsed);
        Assert.Equal(originalSessionId, sessionId);
        Assert.Equal(originalStreamId, streamId);
        Assert.Equal(originalSequence, sequence);
    }

    [Fact]
    public void EventIdFormatter_TryParse_HandlesEmptySessionAndStreamIds()
    {
        // Arrange
        var originalSessionId = "";
        var originalStreamId = "";
        var originalSequence = 42L;

        // Act
        var eventId = DistributedCacheEventIdFormatter.Format(originalSessionId, originalStreamId, originalSequence);
        var parsed = DistributedCacheEventIdFormatter.TryParse(eventId, out var sessionId, out var streamId, out var sequence);

        // Assert
        Assert.True(parsed);
        Assert.Equal(originalSessionId, sessionId);
        Assert.Equal(originalStreamId, streamId);
        Assert.Equal(originalSequence, sequence);
    }

    [Fact]
    public void EventIdFormatter_TryParse_HandlesSpecialCharactersInSessionId()
    {
        // Arrange - Session IDs can contain any visible ASCII character per MCP spec
        var originalSessionId = "session:with:colons:and|pipes";
        var originalStreamId = "stream-1";
        var originalSequence = 1L;

        // Act
        var eventId = DistributedCacheEventIdFormatter.Format(originalSessionId, originalStreamId, originalSequence);
        var parsed = DistributedCacheEventIdFormatter.TryParse(eventId, out var sessionId, out var streamId, out var sequence);

        // Assert
        Assert.True(parsed);
        Assert.Equal(originalSessionId, sessionId);
        Assert.Equal(originalStreamId, streamId);
        Assert.Equal(originalSequence, sequence);
    }

    [Fact]
    public void EventIdFormatter_TryParse_HandlesSpecialCharactersInStreamId()
    {
        // Arrange
        var originalSessionId = "session-1";
        var originalStreamId = "stream:with:colons:and|special!chars@#$%";
        var originalSequence = 1L;

        // Act
        var eventId = DistributedCacheEventIdFormatter.Format(originalSessionId, originalStreamId, originalSequence);
        var parsed = DistributedCacheEventIdFormatter.TryParse(eventId, out var sessionId, out var streamId, out var sequence);

        // Assert
        Assert.True(parsed);
        Assert.Equal(originalSessionId, sessionId);
        Assert.Equal(originalStreamId, streamId);
        Assert.Equal(originalSequence, sequence);
    }

    [Fact]
    public void EventIdFormatter_TryParse_HandlesUnicodeCharacters()
    {
        // Arrange
        var originalSessionId = "session-日本語-émojis-🎉";
        var originalStreamId = "stream-中文-العربية";
        var originalSequence = 999L;

        // Act
        var eventId = DistributedCacheEventIdFormatter.Format(originalSessionId, originalStreamId, originalSequence);
        var parsed = DistributedCacheEventIdFormatter.TryParse(eventId, out var sessionId, out var streamId, out var sequence);

        // Assert
        Assert.True(parsed);
        Assert.Equal(originalSessionId, sessionId);
        Assert.Equal(originalStreamId, streamId);
        Assert.Equal(originalSequence, sequence);
    }

    [Fact]
    public void EventIdFormatter_TryParse_HandlesZeroSequence()
    {
        // Act
        var eventId = DistributedCacheEventIdFormatter.Format("session", "stream", 0);
        var parsed = DistributedCacheEventIdFormatter.TryParse(eventId, out _, out _, out var sequence);

        // Assert
        Assert.True(parsed);
        Assert.Equal(0, sequence);
    }

    [Fact]
    public void EventIdFormatter_TryParse_HandlesLargeSequence()
    {
        // Act
        var eventId = DistributedCacheEventIdFormatter.Format("session", "stream", long.MaxValue);
        var parsed = DistributedCacheEventIdFormatter.TryParse(eventId, out _, out _, out var sequence);

        // Assert
        Assert.True(parsed);
        Assert.Equal(long.MaxValue, sequence);
    }

    [Fact]
    public void EventIdFormatter_TryParse_ReturnsFalse_ForEmptyString()
    {
        // Act
        var parsed = DistributedCacheEventIdFormatter.TryParse("", out var sessionId, out var streamId, out var sequence);

        // Assert
        Assert.False(parsed);
        Assert.Equal(string.Empty, sessionId);
        Assert.Equal(string.Empty, streamId);
        Assert.Equal(0, sequence);
    }

    [Fact]
    public void EventIdFormatter_TryParse_ReturnsFalse_ForInvalidFormat()
    {
        // Act & Assert - Various invalid formats
        Assert.False(DistributedCacheEventIdFormatter.TryParse("no-separators", out _, out _, out _));
        Assert.False(DistributedCacheEventIdFormatter.TryParse("only:one", out _, out _, out _));
        Assert.False(DistributedCacheEventIdFormatter.TryParse("too:many:parts:here", out _, out _, out _));
    }

    [Fact]
    public void EventIdFormatter_TryParse_ReturnsFalse_ForInvalidBase64()
    {
        // Act - Invalid base64 in first part
        var parsed = DistributedCacheEventIdFormatter.TryParse("!!!invalid!!!:c3RyZWFt:1", out _, out _, out _);

        // Assert
        Assert.False(parsed);
    }

    [Fact]
    public void EventIdFormatter_TryParse_ReturnsFalse_ForNonNumericSequence()
    {
        // Arrange - Valid base64 but non-numeric sequence
        var sessionBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("session"));
        var streamBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("stream"));
        var invalidEventId = $"{sessionBase64}:{streamBase64}:not-a-number";

        // Act
        var parsed = DistributedCacheEventIdFormatter.TryParse(invalidEventId, out _, out _, out _);

        // Assert
        Assert.False(parsed);
    }

    /// <summary>
    /// A distributed cache that tracks all operations for verification in tests.
    /// Supports tracking Set calls, counting metadata reads, and simulating metadata/event expiration.
    /// </summary>
    private sealed class TestDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _innerCache = new(Options.Create(new MemoryDistributedCacheOptions()));
        private int _metadataReadCount;
        private bool _metadataExpired;
        private readonly HashSet<string> _expiredEventIds = [];

        public List<(string Key, DistributedCacheEntryOptions Options)> SetCalls { get; } = [];
        public int MetadataReadCount => _metadataReadCount;

        public void ExpireMetadata() => _metadataExpired = true;
        public void ExpireEvent(string eventId) => _expiredEventIds.Add(eventId);

        public byte[]? Get(string key)
        {
            if (key.Contains("meta:"))
            {
                Interlocked.Increment(ref _metadataReadCount);
                if (_metadataExpired)
                {
                    return null;
                }
            }
            if (IsExpiredEvent(key))
            {
                return null;
            }
            return _innerCache.Get(key);
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            if (key.Contains("meta:"))
            {
                Interlocked.Increment(ref _metadataReadCount);
                if (_metadataExpired)
                {
                    return Task.FromResult<byte[]?>(null);
                }
            }
            if (IsExpiredEvent(key))
            {
                return Task.FromResult<byte[]?>(null);
            }
            return _innerCache.GetAsync(key, token);
        }

        private bool IsExpiredEvent(string key)
        {
            // Cache key format is "mcp:sse:event:{eventId}"
            foreach (var expiredEventId in _expiredEventIds)
            {
                if (key.EndsWith(expiredEventId))
                {
                    return true;
                }
            }
            return false;
        }

        public void Refresh(string key) => _innerCache.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => _innerCache.RefreshAsync(key, token);
        public void Remove(string key) => _innerCache.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => _innerCache.RemoveAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            SetCalls.Add((key, options));
            _innerCache.Set(key, value, options);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            SetCalls.Add((key, options));
            return _innerCache.SetAsync(key, value, options, token);
        }
    }
}
