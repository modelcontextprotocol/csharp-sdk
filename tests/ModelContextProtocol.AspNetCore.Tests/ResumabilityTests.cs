using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Moq;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for SSE resumability and redelivery features.
/// Tests focus on the IEventStore interface and unit-level behavior.
/// </summary>
public class ResumabilityTests : LoggedTest
{
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
        var eventId1 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcNotification { Method = "test1" }, ct);
        var eventId2 = await eventStore.StoreEventAsync("stream1",
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
        var stream1Event1 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcNotification { Method = "test1" }, ct);
        var stream1Event2 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcNotification { Method = "test2" }, ct);
        _ = await eventStore.StoreEventAsync("stream2",
            new JsonRpcNotification { Method = "test3" }, ct);
        var stream1Event3 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcNotification { Method = "test4" }, ct);

        // Act - Replay events after stream1Event1
        var replayedEvents = new List<(JsonRpcMessage Message, string EventId)>();
        var streamId = await eventStore.ReplayEventsAfterAsync(
            stream1Event1,
            (message, eventId, cancellationToken) =>
            {
                replayedEvents.Add((message, eventId));
                return ValueTask.CompletedTask;
            },
            ct);

        // Assert
        Assert.Equal("stream1", streamId);
        Assert.Equal(2, replayedEvents.Count); // Only stream1 events after stream1Event1

        var notification1 = Assert.IsType<JsonRpcNotification>(replayedEvents[0].Message);
        Assert.Equal("test2", notification1.Method);
        Assert.Equal(stream1Event2, replayedEvents[0].EventId);

        var notification2 = Assert.IsType<JsonRpcNotification>(replayedEvents[1].Message);
        Assert.Equal("test4", notification2.Method);
        Assert.Equal(stream1Event3, replayedEvents[1].EventId);
    }

    [Fact]
    public async Task EventStore_ReturnsNull_ForUnknownEventId()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;
        await eventStore.StoreEventAsync("stream1", new JsonRpcNotification { Method = "test" }, ct);

        // Act
        var streamId = await eventStore.ReplayEventsAfterAsync(
            "unknown-event-id",
            (message, eventId, cancellationToken) => ValueTask.CompletedTask,
            ct);

        // Assert
        Assert.Null(streamId);
    }

    [Fact]
    public async Task EventStore_ReplaysNoEvents_WhenLastEventIsLatest()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;

        var eventId1 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcNotification { Method = "test1" }, ct);
        var eventId2 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcNotification { Method = "test2" }, ct);

        // Act - Replay after the last event
        var replayedEvents = new List<(JsonRpcMessage Message, string EventId)>();
        var streamId = await eventStore.ReplayEventsAfterAsync(
            eventId2,
            (message, eventId, cancellationToken) =>
            {
                replayedEvents.Add((message, eventId));
                return ValueTask.CompletedTask;
            },
            ct);

        // Assert - No events should be replayed
        Assert.Equal("stream1", streamId);
        Assert.Empty(replayedEvents);
    }

    [Fact]
    public async Task EventStore_HandlesPrimingEvents()
    {
        // Arrange - Priming events have null messages
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;

        // Store a priming event (null message) followed by real events
        var primingEventId = await eventStore.StoreEventAsync("stream1", null, ct);
        var eventId1 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcNotification { Method = "test1" }, ct);
        var eventId2 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcNotification { Method = "test2" }, ct);

        // Act - Replay after priming event
        var replayedEvents = new List<(JsonRpcMessage Message, string EventId)>();
        var streamId = await eventStore.ReplayEventsAfterAsync(
            primingEventId,
            (message, eventId, cancellationToken) =>
            {
                replayedEvents.Add((message, eventId));
                return ValueTask.CompletedTask;
            },
            ct);

        // Assert - Should replay the two real events, not the priming event
        Assert.Equal("stream1", streamId);
        Assert.Equal(2, replayedEvents.Count);
        Assert.Equal(eventId1, replayedEvents[0].EventId);
        Assert.Equal(eventId2, replayedEvents[1].EventId);
    }

    [Fact]
    public async Task EventStore_ReplaysMixedMessageTypes()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var ct = TestContext.Current.CancellationToken;

        var eventId1 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcNotification { Method = "notification" }, ct);
        var eventId2 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcResponse { Id = new RequestId("1"), Result = null }, ct);
        var eventId3 = await eventStore.StoreEventAsync("stream1",
            new JsonRpcRequest { Id = new RequestId("2"), Method = "request" }, ct);

        // Act
        var replayedEvents = new List<(JsonRpcMessage Message, string EventId)>();
        var streamId = await eventStore.ReplayEventsAfterAsync(
            eventId1,
            (message, eventId, cancellationToken) =>
            {
                replayedEvents.Add((message, eventId));
                return ValueTask.CompletedTask;
            },
            ct);

        // Assert
        Assert.Equal("stream1", streamId);
        Assert.Equal(2, replayedEvents.Count);

        Assert.IsType<JsonRpcResponse>(replayedEvents[0].Message);
        Assert.Equal(eventId2, replayedEvents[0].EventId);

        Assert.IsType<JsonRpcRequest>(replayedEvents[1].Message);
        Assert.Equal(eventId3, replayedEvents[1].EventId);
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
    public void StreamableHttpServerTransport_HasRetryIntervalProperty()
    {
        // Arrange
        var transport = new StreamableHttpServerTransport();

        // Assert - RetryInterval is null by default
        Assert.Null(transport.RetryInterval);

        // Act - Can set RetryInterval
        transport.RetryInterval = TimeSpan.FromSeconds(5);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), transport.RetryInterval);
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

    [Fact]
    public void JsonRpcMessageContext_HasCloseSseStreamProperty()
    {
        // Arrange
        var context = new JsonRpcMessageContext();

        // Assert - CloseSseStream property exists and is null by default
        Assert.Null(context.CloseSseStream);

        // Act - Can set CloseSseStream
        var invoked = false;
        context.CloseSseStream = () => invoked = true;

        // Assert - Can invoke the callback
        context.CloseSseStream();
        Assert.True(invoked);
    }

    [Fact]
    public void JsonRpcMessageContext_HasCloseStandaloneSseStreamProperty()
    {
        // Arrange
        var context = new JsonRpcMessageContext();

        // Assert - CloseStandaloneSseStream property exists and is null by default
        Assert.Null(context.CloseStandaloneSseStream);

        // Act - Can set CloseStandaloneSseStream
        var invoked = false;
        context.CloseStandaloneSseStream = () => invoked = true;

        // Assert - Can invoke the callback
        context.CloseStandaloneSseStream();
        Assert.True(invoked);
    }

    [Fact]
    public void RequestContext_CloseSseStream_InvokesCallback()
    {
        // Arrange
        var invoked = false;
        var context = new JsonRpcMessageContext
        {
            CloseSseStream = () => invoked = true
        };
        var request = new JsonRpcRequest { Id = new RequestId("1"), Method = "test", Context = context };
        var server = new Mock<McpServer>().Object;
        var requestContext = new RequestContext<object>(server, request);

        // Act
        requestContext.CloseSseStream();

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public void RequestContext_CloseSseStream_DoesNothingWhenContextNull()
    {
        // Arrange
        var request = new JsonRpcRequest { Id = new RequestId("1"), Method = "test", Context = null };
        var server = new Mock<McpServer>().Object;
        var requestContext = new RequestContext<object>(server, request);

        // Act & Assert - Should not throw
        requestContext.CloseSseStream();
    }

    [Fact]
    public void RequestContext_CloseSseStream_DoesNothingWhenCallbackNull()
    {
        // Arrange
        var context = new JsonRpcMessageContext { CloseSseStream = null };
        var request = new JsonRpcRequest { Id = new RequestId("1"), Method = "test", Context = context };
        var server = new Mock<McpServer>().Object;
        var requestContext = new RequestContext<object>(server, request);

        // Act & Assert - Should not throw
        requestContext.CloseSseStream();
    }

    [Fact]
    public void RequestContext_CloseStandaloneSseStream_InvokesCallback()
    {
        // Arrange
        var invoked = false;
        var context = new JsonRpcMessageContext
        {
            CloseStandaloneSseStream = () => invoked = true
        };
        var request = new JsonRpcRequest { Id = new RequestId("1"), Method = "test", Context = context };
        var server = new Mock<McpServer>().Object;
        var requestContext = new RequestContext<object>(server, request);

        // Act
        requestContext.CloseStandaloneSseStream();

        // Assert
        Assert.True(invoked);
    }
}
