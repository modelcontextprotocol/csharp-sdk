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
        var token = TestContext.Current.CancellationToken;
        var clientTransport = fixture.CreateClientTransport();
        await using var endpoint = await fixture.CreateClientEndpointAsync(clientTransport);
        var transport = await clientTransport.ConnectAsync(token);
        
        var requestId = new RequestId("test-request-id-123");
        const string reason = "Operation was cancelled by the user";

        // Act
        await endpoint.NotifyCancelAsync(requestId, reason, token);

        // Assert
        Assert.Equal(1, transport.MessageReader.Count);
        var message = await transport.MessageReader.ReadAsync(token);
        Assert.NotNull(message);

        var notification = Assert.IsType<JsonRpcNotification>(message);
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
        var clientTransport = fixture.CreateClientTransport();
        await using var endpoint = await fixture.CreateClientEndpointAsync(clientTransport);
        var transport = await clientTransport.ConnectAsync(token);
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
        Assert.Equal(2, transport.MessageReader.Count);
        var message = await transport.MessageReader.ReadAsync(token);
        Assert.NotNull(message);

        var notification = Assert.IsType<JsonRpcNotification>(message);
        Assert.Equal(NotificationMethods.CancelledNotification, notification.Method);
        
        var cancelParams = Assert.IsType<CancelledNotification>(notification.Params);
        Assert.Equal(requestId, cancelParams.RequestId);

        message = await transport.MessageReader.ReadAsync(token);
        Assert.NotNull(message);
        var requestMessage = Assert.IsType<JsonRpcRequest>(message);
        Assert.Equal(request.Id, requestMessage.Id);
        Assert.Equal(request.Method, requestMessage.Method);
        Assert.Equal(request.Params, requestMessage.Params);
    }
}