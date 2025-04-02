using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Tests for the cancelled notifications against an IMcpEndpoint.
/// </summary>
public class CancelledNotificationTests(
    ClientIntegrationTestFixture fixture, ITestOutputHelper testOutputHelper)
    : LoggedTest(testOutputHelper), IClassFixture<ClientIntegrationTestFixture>
{
    [Fact]
    public async Task NotifyCancelAsync_SendsCorrectNotification()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var clientId = "test_server";
        TaskCompletionSource<JsonRpcNotification> notificationCompletion = new();
        Task HandleCancel(JsonRpcNotification notification)
        {
            var cancelParams = notification.Params as CancelledNotification;
            Assert.NotNull(cancelParams);
            Assert.Equal("test-request-id-123", cancelParams.RequestId.ToString());
            Assert.Equal("Operation was cancelled by the user", cancelParams.Reason);
            notificationCompletion.SetResult(notification);
            return Task.CompletedTask;
        }
        var client = await fixture.CreateClientAsync(clientId, new()
        {
            ClientInfo = new Implementation
            {
                Name = "TestClient",
                Version = "1.0.0",
            },
            NotificationHandlers = new Dictionary<string, List<Func<JsonRpcNotification, Task>>>
            {
                [NotificationMethods.CancelledNotification] = [HandleCancel],
            }
        });
        var requestId = new RequestId("test-request-id-123");
        const string reason = "Operation was cancelled by the user";

        // Act
        await client.NotifyCancelAsync(requestId, reason, token);

        // Assert
        var notification = await notificationCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.NotNull(notification);
        Assert.Equal(NotificationMethods.CancelledNotification, notification.Method);
        var cancelParams = Assert.IsType<CancelledNotification>(notification.Params);
        Assert.Equal(requestId, cancelParams.RequestId);
        Assert.Equal(reason, cancelParams.Reason);
    }

    [Fact]
    public async Task SendRequestAsync_Cancellation_SendsNotification()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var clientId = "test_server";
        TaskCompletionSource<JsonRpcNotification> notificationCompletion = new();
        Task HandleCancel(JsonRpcNotification notification)
        {
            var cancelParams = notification.Params as CancelledNotification;
            Assert.NotNull(cancelParams);
            Assert.Equal("test-request-id-123", cancelParams.RequestId.ToString());
            notificationCompletion.SetResult(notification);
            return Task.CompletedTask;
        }
        var client = await fixture.CreateClientAsync(clientId, new()
        {
            ClientInfo = new Implementation
            {
                Name = "TestClient",
                Version = "1.0.0",
            },
            NotificationHandlers = new Dictionary<string, List<Func<JsonRpcNotification, Task>>>
            {
                [NotificationMethods.CancelledNotification] = [HandleCancel],
            }
        });
        var requestId = new RequestId("test-request-id-123");
        JsonRpcRequest request = new()
        {
            Id = requestId,
            Method = "test.method",
            Params = new { },
        };
        using CancellationTokenSource cancellationSource = new();
        await cancellationSource.CancelAsync();
        // Act
        try
        {
            await client.SendRequestAsync<EmptyResult>(request, cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected exception
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected exception: {ex.Message}");
        }

        // Assert
        var notification = await notificationCompletion.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        Assert.NotNull(notification);
        Assert.Equal(NotificationMethods.CancelledNotification, notification.Method);
        var cancelParams = Assert.IsType<CancelledNotification>(notification.Params);
        Assert.Equal(requestId, cancelParams.RequestId);
    }
}