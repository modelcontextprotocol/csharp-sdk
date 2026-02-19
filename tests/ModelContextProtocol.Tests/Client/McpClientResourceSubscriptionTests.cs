using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.ComponentModel;

namespace ModelContextProtocol.Tests.Client;

public class McpClientResourceSubscriptionTests : ClientServerTestBase
{
    public McpClientResourceSubscriptionTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithResources<SubscribableResources>();
    }

    [McpServerResourceType]
    private sealed class SubscribableResources
    {
        [McpServerResource(UriTemplate = "test://resource/{id}"), Description("A subscribable test resource")]
        public static string GetResource(string id) => $"Resource content: {id}";
    }

    [Fact]
    public async Task SubscribeToResourceAsync_WithHandler_ReceivesNotifications()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();
        const string resourceUri = "test://resource/1";
        var notificationReceived = new TaskCompletionSource<ResourceUpdatedNotificationParams>();

        // Act
        await using var subscription = await client.SubscribeToResourceAsync(
            resourceUri,
            (notification, ct) =>
            {
                notificationReceived.TrySetResult(notification);
                return default(ValueTask);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Send a notification from the server
        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = resourceUri },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var cts = new CancellationTokenSource(TestConstants.DefaultTimeout);
        var receivedNotification = await notificationReceived.Task.WaitAsync(cts.Token);
        Assert.NotNull(receivedNotification);
        Assert.Equal(resourceUri, receivedNotification.Uri);
    }

    [Fact]
    public async Task SubscribeToResourceAsync_WithHandler_FiltersNotificationsByUri()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();
        const string subscribedUri = "test://resource/1";
        const string otherUri = "test://resource/2";
        var notificationCount = 0;
        var correctNotificationReceived = new TaskCompletionSource<bool>();

        // Act
        await using var subscription = await client.SubscribeToResourceAsync(
            subscribedUri,
            (notification, ct) =>
            {
                Interlocked.Increment(ref notificationCount);
                if (notification.Uri == subscribedUri)
                {
                    correctNotificationReceived.TrySetResult(true);
                }
                return default(ValueTask);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Send notifications for different resources
        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = otherUri },
            cancellationToken: TestContext.Current.CancellationToken);

        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = subscribedUri },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var cts = new CancellationTokenSource(TestConstants.DefaultTimeout);
        await correctNotificationReceived.Task.WaitAsync(cts.Token);
        
        // Give a small delay to ensure no other notifications are processed
        await Task.Delay(100, TestContext.Current.CancellationToken);
        
        // Should only receive the notification for the subscribed URI
        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public async Task SubscribeToResourceAsync_WithHandler_DisposalUnsubscribes()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();
        const string resourceUri = "test://resource/1";
        var notificationCount = 0;

        // Act
        var subscription = await client.SubscribeToResourceAsync(
            resourceUri,
            (notification, ct) =>
            {
                Interlocked.Increment(ref notificationCount);
                return default(ValueTask);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Send a notification - should be received
        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = resourceUri },
            cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken); // Allow time for notification to be processed

        // Dispose the subscription
        await subscription.DisposeAsync();

        // Send another notification - should NOT be received
        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = resourceUri },
            cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken); // Allow time to ensure notification is not processed

        // Assert - only the first notification should have been received
        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public async Task SubscribeToResourceAsync_WithHandler_UriOverload_ReceivesNotifications()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();
        var resourceUri = new Uri("test://resource/1");
        var notificationReceived = new TaskCompletionSource<ResourceUpdatedNotificationParams>();

        // Act
        await using var subscription = await client.SubscribeToResourceAsync(
            resourceUri,
            (notification, ct) =>
            {
                notificationReceived.TrySetResult(notification);
                return default(ValueTask);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Send a notification from the server
        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = resourceUri.AbsoluteUri },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var cts = new CancellationTokenSource(TestConstants.DefaultTimeout);
        var receivedNotification = await notificationReceived.Task.WaitAsync(cts.Token);
        Assert.NotNull(receivedNotification);
        Assert.Equal(resourceUri.AbsoluteUri, receivedNotification.Uri);
    }

    [Fact]
    public async Task SubscribeToResourceAsync_WithNullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await client.SubscribeToResourceAsync(
                "test://resource/1",
                handler: null!,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubscribeToResourceAsync_WithNullUri_ThrowsArgumentNullException()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await client.SubscribeToResourceAsync(
                uri: (Uri)null!,
                handler: (notification, ct) => default,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubscribeToResourceAsync_WithEmptyUri_ThrowsArgumentException()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SubscribeToResourceAsync(
                uri: "",
                handler: (notification, ct) => default,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubscribeToResourceAsync_MultipleSubscriptions_BothReceiveNotifications()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();
        const string uri1 = "test://resource/1";
        const string uri2 = "test://resource/2";
        var notification1Received = new TaskCompletionSource<bool>();
        var notification2Received = new TaskCompletionSource<bool>();

        // Act
        await using var subscription1 = await client.SubscribeToResourceAsync(
            uri1,
            (notification, ct) =>
            {
                if (notification.Uri == uri1)
                {
                    notification1Received.TrySetResult(true);
                }
                return default(ValueTask);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        await using var subscription2 = await client.SubscribeToResourceAsync(
            uri2,
            (notification, ct) =>
            {
                if (notification.Uri == uri2)
                {
                    notification2Received.TrySetResult(true);
                }
                return default(ValueTask);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Send notifications
        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = uri1 },
            cancellationToken: TestContext.Current.CancellationToken);

        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = uri2 },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var cts = new CancellationTokenSource(TestConstants.DefaultTimeout);
        var combined = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, TestContext.Current.CancellationToken);
        await Task.WhenAll(
            notification1Received.Task.WaitAsync(combined.Token),
            notification2Received.Task.WaitAsync(combined.Token));

        Assert.True(await notification1Received.Task);
        Assert.True(await notification2Received.Task);
    }

    [Fact]
    public async Task SubscribeToResourceAsync_DisposalIsIdempotent()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();
        const string resourceUri = "test://resource/1";

        var subscription = await client.SubscribeToResourceAsync(
            resourceUri,
            (notification, ct) => default,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act - dispose multiple times
        await subscription.DisposeAsync();
        await subscription.DisposeAsync();
        await subscription.DisposeAsync();

        // Assert - no exception should be thrown
        Assert.True(true);
    }

    [Fact]
    public async Task SubscribeToResourceAsync_MultipleHandlersSameUri_BothReceiveNotifications()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();
        const string resourceUri = "test://resource/1";
        var handler1Called = new TaskCompletionSource<bool>();
        var handler2Called = new TaskCompletionSource<bool>();
        var handler2CalledAgain = new TaskCompletionSource<bool>();
        var handler1Count = 0;
        var handler2Count = 0;

        // Act - Create two subscriptions to the same URI
        await using var subscription1 = await client.SubscribeToResourceAsync(
            resourceUri,
            (notification, ct) =>
            {
                Interlocked.Increment(ref handler1Count);
                handler1Called.TrySetResult(true);
                return default;
            },
            cancellationToken: TestContext.Current.CancellationToken);

        await using var subscription2 = await client.SubscribeToResourceAsync(
            resourceUri,
            (notification, ct) =>
            {
                var count = Interlocked.Increment(ref handler2Count);
                if (count == 1)
                {
                    handler2Called.TrySetResult(true);
                }
                else if (count == 2)
                {
                    handler2CalledAgain.TrySetResult(true);
                }
                return default;
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Send a single notification
        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = resourceUri },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Both handlers should be invoked
        using var cts = new CancellationTokenSource(TestConstants.DefaultTimeout);
        var combined = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, TestContext.Current.CancellationToken);
        await Task.WhenAll(
            handler1Called.Task.WaitAsync(combined.Token),
            handler2Called.Task.WaitAsync(combined.Token));

        Assert.Equal(1, handler1Count);
        Assert.Equal(1, handler2Count);

        // Dispose one subscription
        await subscription1.DisposeAsync();

        // Send another notification
        await Server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = resourceUri },
            cancellationToken: TestContext.Current.CancellationToken);

        // Wait for handler2 to be called again
        using var cts2 = new CancellationTokenSource(TestConstants.DefaultTimeout);
        var combined2 = CancellationTokenSource.CreateLinkedTokenSource(cts2.Token, TestContext.Current.CancellationToken);
        await handler2CalledAgain.Task.WaitAsync(combined2.Token);

        // Assert - Only the second handler should still receive notifications
        // Handler1 should not have been called again (still 1)
        Assert.Equal(1, handler1Count);
        // Handler2 should have been called again (now 2)
        Assert.Equal(2, handler2Count);
    }
}
