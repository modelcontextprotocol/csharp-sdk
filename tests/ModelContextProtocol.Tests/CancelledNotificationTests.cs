using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Tests for the cancelled notifications against an IMcpEndpoint.
/// </summary>
public class CancelledNotificationTests(
    McpEndpointTestFixture fixture, ITestOutputHelper testOutputHelper)
    : LoggedTest(testOutputHelper), IClassFixture<McpEndpointTestFixture>
{
    [Fact]
    public async Task NotifyCancelAsync_SendsCorrectNotification()
    {
        // Arrange
        await using var endpoint = fixture.CreateEndpoint();
        await using var transport = fixture.CreateTransport();
        var cancellationToken = TestContext.Current.CancellationToken;
        endpoint.Start(transport, cancellationToken);
        
        var requestId = new RequestId("test-request-id-123");
        const string reason = "Operation was cancelled by the user";

        // Act
        await endpoint.NotifyCancelAsync(requestId, reason, cancellationToken);

        // Assert
        Assert.Single(transport.SentMessages);
        var notification = Assert.IsType<JsonRpcNotification>(transport.SentMessages[0]);
        Assert.Equal(NotificationMethods.CancelledNotification, notification.Method);
        
        var cancelParams = Assert.IsType<CancelledNotification>(notification.Params);
        Assert.Equal(requestId, cancelParams.RequestId);
        Assert.Equal(reason, cancelParams.Reason);
    }

    [Fact]
    public async Task SendRequestAsync_Cancellation_SendsNotification()
    {
        // Arrange
        await using var endpoint = fixture.CreateEndpoint();
        await using var transport = fixture.CreateTransport();
        endpoint.Start(transport, CancellationToken.None);
        
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
            await endpoint.SendRequestAsync<EmptyResult>(request, cancellationSource.Token);
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
        Assert.NotEmpty(transport.SentMessages);
        Assert.Equal(2, transport.SentMessages.Count);
        var notification = Assert.IsType<JsonRpcNotification>(transport.SentMessages[0]);
        Assert.Equal(NotificationMethods.CancelledNotification, notification.Method);
        
        var cancelParams = Assert.IsType<CancelledNotification>(notification.Params);
        Assert.Equal(requestId, cancelParams.RequestId);

        var requestMessage = Assert.IsType<JsonRpcRequest>(transport.SentMessages[1]);
        Assert.Equal(request.Id, requestMessage.Id);
        Assert.Equal(request.Method, requestMessage.Method);
        Assert.Equal(request.Params, requestMessage.Params);
    }
}