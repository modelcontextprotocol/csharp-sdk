using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

#pragma warning disable CS0618 // Type or member is obsolete

public class McpServerExtensionsTests
{
    [Fact]
    public async Task SampleAsync_Request_Throws_When_Not_McpServer()
    {
        var server = new Mock<IMcpServer>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await server.SampleAsync(
            new CreateMessageRequestParams { Messages = [new SamplingMessage { Role = Role.User, Content = new TextContentBlock { Text = "hi" } }] },
            TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpServer.SampleAsync' instead", ex.Message);
    }

    [Fact]
    public async Task SampleAsync_Messages_Throws_When_Not_McpServer()
    {
        var server = new Mock<IMcpServer>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await server.SampleAsync(
            [new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpServer.SampleAsync' instead", ex.Message);
    }

    [Fact]
    public void AsSamplingChatClient_Throws_When_Not_McpServer()
    {
        var server = new Mock<IMcpServer>(MockBehavior.Strict).Object;

        var ex = Assert.Throws<InvalidOperationException>(server.AsSamplingChatClient);
        Assert.Contains("Prefer using 'McpServer.AsSamplingChatClient' instead", ex.Message);
    }

    [Fact]
    public void AsClientLoggerProvider_Throws_When_Not_McpServer()
    {
        var server = new Mock<IMcpServer>(MockBehavior.Strict).Object;

        var ex = Assert.Throws<InvalidOperationException>(server.AsClientLoggerProvider);
        Assert.Contains("Prefer using 'McpServer.AsClientLoggerProvider' instead", ex.Message);
    }

    [Fact]
    public async Task RequestRootsAsync_Throws_When_Not_McpServer()
    {
        var server = new Mock<IMcpServer>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await server.RequestRootsAsync(
            new ListRootsRequestParams(), TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpServer.RequestRootsAsync' instead", ex.Message);
    }

    [Fact]
    public async Task ElicitAsync_Throws_When_Not_McpServer()
    {
        var server = new Mock<IMcpServer>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await server.ElicitAsync(
            new ElicitRequestParams { Message = "hello" }, TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpServer.ElicitAsync' instead", ex.Message);
    }

    [Fact]
    public async Task SampleAsync_Request_Forwards_To_McpServer_SendRequestAsync()
    {
        var mockServer = new Mock<McpServer> { CallBase = true };

        var resultPayload = new CreateMessageResult
        {
            Content = new TextContentBlock { Text = "resp" },
            Model = "test-model",
            Role = Role.Assistant,
            StopReason = "endTurn",
        };

        mockServer
            .Setup(s => s.ClientCapabilities)
            .Returns(new ClientCapabilities() { Sampling = new() });

        mockServer
            .Setup(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonUtilities.DefaultOptions),
            });

        IMcpServer server = mockServer.Object;

        var result = await server.SampleAsync(new CreateMessageRequestParams
        {
            Messages = [new SamplingMessage { Role = Role.User, Content = new TextContentBlock { Text = "hi" } }]
        }, TestContext.Current.CancellationToken);

        Assert.Equal("test-model", result.Model);
        Assert.Equal(Role.Assistant, result.Role);
        Assert.Equal("resp", Assert.IsType<TextContentBlock>(result.Content).Text);
        mockServer.Verify(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SampleAsync_Messages_Forwards_To_McpServer_SendRequestAsync()
    {
        var mockServer = new Mock<McpServer> { CallBase = true };

        var resultPayload = new CreateMessageResult
        {
            Content = new TextContentBlock { Text = "resp" },
            Model = "test-model",
            Role = Role.Assistant,
            StopReason = "endTurn",
        };

        mockServer
            .Setup(s => s.ClientCapabilities)
            .Returns(new ClientCapabilities() { Sampling = new() });

        mockServer
            .Setup(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonUtilities.DefaultOptions),
            });

        IMcpServer server = mockServer.Object;

        var chatResponse = await server.SampleAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("test-model", chatResponse.ModelId);
        var last = chatResponse.Messages.Last();
        Assert.Equal(ChatRole.Assistant, last.Role);
        Assert.Equal("resp", last.Text);
        mockServer.Verify(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestRootsAsync_Forwards_To_McpServer_SendRequestAsync()
    {
        var mockServer = new Mock<McpServer> { CallBase = true };

        var resultPayload = new ListRootsResult { Roots = [new Root { Uri = "root://a" }] };

        mockServer
            .Setup(s => s.ClientCapabilities)
            .Returns(new ClientCapabilities() { Roots = new() });

        mockServer
            .Setup(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonUtilities.DefaultOptions),
            });

        IMcpServer server = mockServer.Object;

        var result = await server.RequestRootsAsync(new ListRootsRequestParams(), TestContext.Current.CancellationToken);

        Assert.Equal("root://a", result.Roots[0].Uri);
        mockServer.Verify(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ElicitAsync_Forwards_To_McpServer_SendRequestAsync()
    {
        var mockServer = new Mock<McpServer> { CallBase = true };

        var resultPayload = new ElicitResult { Action = "accept" };

        mockServer
            .Setup(s => s.ClientCapabilities)
            .Returns(new ClientCapabilities() { Elicitation = new() });

        mockServer
            .Setup(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonUtilities.DefaultOptions),
            });

        IMcpServer server = mockServer.Object;

        var result = await server.ElicitAsync(new ElicitRequestParams { Message = "hi" }, TestContext.Current.CancellationToken);

        Assert.Equal("accept", result.Action);
        mockServer.Verify(s => s.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
