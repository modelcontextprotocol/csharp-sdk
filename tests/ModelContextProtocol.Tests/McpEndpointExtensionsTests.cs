using ModelContextProtocol.Protocol;
using Moq;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

#pragma warning disable CS0618 // Type or member is obsolete

public class McpEndpointExtensionsTests
{
    [Fact]
    public async Task SendRequestAsync_Generic_Throws_When_Not_McpSession()
    {
        var endpoint = new Mock<IMcpEndpoint>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await McpEndpointExtensions.SendRequestAsync<string, int>(
            endpoint, "method", "param", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpServer.SendRequestAsync' instead", ex.Message);
    }

    [Fact]
    public async Task SendNotificationAsync_Parameterless_Throws_When_Not_McpSession()
    {
        var endpoint = new Mock<IMcpEndpoint>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await McpEndpointExtensions.SendNotificationAsync(
            endpoint, "notify", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpServer.SendNotificationAsync' instead", ex.Message);
    }

    [Fact]
    public async Task SendNotificationAsync_Generic_Throws_When_Not_McpSession()
    {
        var endpoint = new Mock<IMcpEndpoint>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await McpEndpointExtensions.SendNotificationAsync(
            endpoint, "notify", "payload", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpServer.SendNotificationAsync' instead", ex.Message);
    }

    [Fact]
    public async Task NotifyProgressAsync_Throws_When_Not_McpSession()
    {
        var endpoint = new Mock<IMcpEndpoint>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await McpEndpointExtensions.NotifyProgressAsync(
            endpoint, new ProgressToken("t1"), new ProgressNotificationValue { Progress = 0.5f }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpServer.NotifyProgressAsync' instead", ex.Message);
    }

    [Fact]
    public async Task SendRequestAsync_Generic_Forwards_To_McpSession_SendRequestAsync()
    {
        var mockSession = new Mock<McpSession> { CallBase = true };

        mockSession
            .Setup(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(42, McpJsonUtilities.DefaultOptions),
            });

        IMcpEndpoint endpoint = mockSession.Object;

        var result = await endpoint.SendRequestAsync<string, int>("method", "param", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
        mockSession.Verify(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendNotificationAsync_Parameterless_Forwards_To_McpSession_SendMessageAsync()
    {
        var mockSession = new Mock<McpSession> { CallBase = true };

        mockSession
            .Setup(s => s.SendMessageAsync(It.IsAny<JsonRpcMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IMcpEndpoint endpoint = mockSession.Object;

        await endpoint.SendNotificationAsync("notify", cancellationToken: TestContext.Current.CancellationToken);

        mockSession.Verify(s => s.SendMessageAsync(It.IsAny<JsonRpcMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendNotificationAsync_Generic_Forwards_To_McpSession_SendMessageAsync()
    {
        var mockSession = new Mock<McpSession> { CallBase = true };

        mockSession
            .Setup(s => s.SendMessageAsync(It.IsAny<JsonRpcMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IMcpEndpoint endpoint = mockSession.Object;

        await endpoint.SendNotificationAsync("notify", "payload", cancellationToken: TestContext.Current.CancellationToken);

        mockSession.Verify(s => s.SendMessageAsync(It.IsAny<JsonRpcMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyProgressAsync_Forwards_To_McpSession_SendMessageAsync()
    {
        var mockSession = new Mock<McpSession> { CallBase = true };

        mockSession
            .Setup(s => s.SendMessageAsync(It.IsAny<JsonRpcMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IMcpEndpoint endpoint = mockSession.Object;

        await endpoint.NotifyProgressAsync(new ProgressToken("progress-token"), new ProgressNotificationValue { Progress = 1 }, cancellationToken: TestContext.Current.CancellationToken);

        mockSession.Verify(s => s.SendMessageAsync(It.IsAny<JsonRpcMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}