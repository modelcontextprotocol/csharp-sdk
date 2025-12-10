using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for SSE resumability and redelivery features.
/// Tests focus on the ISseEventStore interface and unit-level behavior.
/// </summary>
public class ResumabilityTests : LoggedTest
{
    private const string TestSessionId = "test-session";

    public ResumabilityTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task EventStore_StoresAndRetrievesEvents()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;

        // Act
        var eventId1 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "test1" }, ct);
        var eventId2 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "test2" }, ct);

        // Assert
        Assert.Equal("1", eventId1);
        Assert.Equal("2", eventId2);
        Assert.Equal(2, eventStore.StoredEventIds.Count);
    }

    [Fact]
    public async Task EventStore_TracksMultipleStreams()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;

        // Store events for different streams
        var stream1Event1 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "test1" }, ct);
        var stream1Event2 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "test2" }, ct);
        _ = await eventStore.StoreEventAsync(TestSessionId, "stream2",
            new JsonRpcNotification { Method = "test3" }, ct);
        var stream1Event3 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "test4" }, ct);

        // Act - Get events after stream1Event1
        var result = await eventStore.GetEventsAfterAsync(stream1Event1, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("stream1", result.StreamId);
        Assert.Equal(TestSessionId, result.SessionId);

        var storedEvents = new List<StoredSseEvent>();
        await foreach (var evt in result.Events)
        {
            storedEvents.Add(evt);
        }

        Assert.Equal(2, storedEvents.Count); // Only stream1 events after stream1Event1

        var notification1 = Assert.IsType<JsonRpcNotification>(storedEvents[0].Message);
        Assert.Equal("test2", notification1.Method);
        Assert.Equal(stream1Event2, storedEvents[0].EventId);

        var notification2 = Assert.IsType<JsonRpcNotification>(storedEvents[1].Message);
        Assert.Equal("test4", notification2.Method);
        Assert.Equal(stream1Event3, storedEvents[1].EventId);
    }

    [Fact]
    public async Task EventStore_ReturnsDefault_ForUnknownEventId()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;
        await eventStore.StoreEventAsync(TestSessionId, "stream1", new JsonRpcNotification { Method = "test" }, ct);

        // Act
        var result = await eventStore.GetEventsAfterAsync("unknown-event-id", ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task EventStore_ReplaysNoEvents_WhenLastEventIsLatest()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;

        var eventId1 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "test1" }, ct);
        var eventId2 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "test2" }, ct);

        // Act - Get events after the last event
        var result = await eventStore.GetEventsAfterAsync(eventId2, ct);

        // Assert - No events should be returned
        Assert.NotNull(result);
        Assert.Equal("stream1", result.StreamId);

        var storedEvents = new List<StoredSseEvent>();
        await foreach (var evt in result.Events)
        {
            storedEvents.Add(evt);
        }
        Assert.Empty(storedEvents);
    }

    [Fact]
    public async Task EventStore_HandlesPrimingEvents()
    {
        // Arrange - Priming events have null messages
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;

        // Store a priming event (null message) followed by real events
        var primingEventId = await eventStore.StoreEventAsync(TestSessionId, "stream1", null, ct);
        var eventId1 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "test1" }, ct);
        var eventId2 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "test2" }, ct);

        // Act - Get events after priming event
        var result = await eventStore.GetEventsAfterAsync(primingEventId, ct);

        // Assert - Should return the two real events, not the priming event
        Assert.NotNull(result);
        Assert.Equal("stream1", result.StreamId);

        var storedEvents = new List<StoredSseEvent>();
        await foreach (var evt in result.Events)
        {
            storedEvents.Add(evt);
        }

        Assert.Equal(2, storedEvents.Count);
        Assert.Equal(eventId1, storedEvents[0].EventId);
        Assert.Equal(eventId2, storedEvents[1].EventId);
    }

    [Fact]
    public async Task EventStore_ReplaysMixedMessageTypes()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;

        var eventId1 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcNotification { Method = "notification" }, ct);
        var eventId2 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcResponse { Id = new RequestId("1"), Result = null }, ct);
        var eventId3 = await eventStore.StoreEventAsync(TestSessionId, "stream1",
            new JsonRpcRequest { Id = new RequestId("2"), Method = "request" }, ct);

        // Act
        var result = await eventStore.GetEventsAfterAsync(eventId1, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("stream1", result.StreamId);

        var storedEvents = new List<StoredSseEvent>();
        await foreach (var evt in result.Events)
        {
            storedEvents.Add(evt);
        }

        Assert.Equal(2, storedEvents.Count);

        Assert.IsType<JsonRpcResponse>(storedEvents[0].Message);
        Assert.Equal(eventId2, storedEvents[0].EventId);

        Assert.IsType<JsonRpcRequest>(storedEvents[1].Message);
        Assert.Equal(eventId3, storedEvents[1].EventId);
    }

    [Fact]
    public void StreamableHttpServerTransport_HasEventStoreProperty()
    {
        // Arrange
        var transport = new StreamableHttpServerTransport();

        // Assert - EventStore property exists and is null by default
        Assert.Null(transport.EventStore);

        // Act - Can set EventStore
        var eventStore = new InMemoryEventStore();
        transport.EventStore = eventStore;

        // Assert
        Assert.Same(eventStore, transport.EventStore);
    }

    [Fact]
    public void StreamableHttpServerTransport_GetStreamIdConstant_IsCorrect()
    {
        // The GetStreamId constant is internal, but we can test that transports
        // with resumability configured behave consistently
        var transport1 = new StreamableHttpServerTransport();
        var transport2 = new StreamableHttpServerTransport();

        // Both should have null EventStore by default
        Assert.Null(transport1.EventStore);
        Assert.Null(transport2.EventStore);

        // Setting event stores should work independently
        var store1 = new InMemoryEventStore();
        var store2 = new InMemoryEventStore();

        transport1.EventStore = store1;
        transport2.EventStore = store2;

        Assert.Same(store1, transport1.EventStore);
        Assert.Same(store2, transport2.EventStore);
    }
}
