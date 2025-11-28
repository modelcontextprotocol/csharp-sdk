using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moq;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

#pragma warning disable CS0618 // Type or member is obsolete

public class McpClientExtensionsTests
{
    [Fact]
    public async Task PingAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.PingAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.PingAsync' instead", ex.Message);
    }

    [Fact]
    public async Task GetPromptAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.GetPromptAsync(
            "name", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.GetPromptAsync' instead", ex.Message);
    }

    [Fact]
    public async Task CallToolAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.CallToolAsync(
            "tool", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.CallToolAsync' instead", ex.Message);
    }

    [Fact]
    public async Task ListResourcesAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ListResourcesAsync(
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.ListResourcesAsync' instead", ex.Message);
    }

    [Fact]
    public void EnumerateResourcesAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = Assert.Throws<InvalidOperationException>(() => client.EnumerateResourcesAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.EnumerateResourcesAsync' instead", ex.Message);
    }

    [Fact]
    public async Task SubscribeToResourceAsync_String_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SubscribeToResourceAsync(
            "mcp://resource/1", TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.SubscribeToResourceAsync' instead", ex.Message);
    }

    [Fact]
    public async Task SubscribeToResourceAsync_Uri_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SubscribeToResourceAsync(
            new Uri("mcp://resource/1"), TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.SubscribeToResourceAsync' instead", ex.Message);
    }

    [Fact]
    public async Task UnsubscribeFromResourceAsync_String_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.UnsubscribeFromResourceAsync(
            "mcp://resource/1", TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.UnsubscribeFromResourceAsync' instead", ex.Message);
    }

    [Fact]
    public async Task UnsubscribeFromResourceAsync_Uri_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.UnsubscribeFromResourceAsync(
            new Uri("mcp://resource/1"), TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.UnsubscribeFromResourceAsync' instead", ex.Message);
    }

    [Fact]
    public async Task ReadResourceAsync_String_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ReadResourceAsync(
            "mcp://resource/1", TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.ReadResourceAsync' instead", ex.Message);
    }

    [Fact]
    public async Task ReadResourceAsync_Uri_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ReadResourceAsync(
            new Uri("mcp://resource/1"), TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.ReadResourceAsync' instead", ex.Message);
    }

    [Fact]
    public async Task ReadResourceAsync_Template_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ReadResourceAsync(
            "mcp://resource/{id}", new Dictionary<string, object?> { ["id"] = 1 }, TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.ReadResourceAsync' instead", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;
        var reference = new PromptReference { Name = "prompt" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.CompleteAsync(
            reference, "arg", "val", TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.CompleteAsync' instead", ex.Message);
    }

    [Fact]
    public async Task ListToolsAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.ListToolsAsync' instead", ex.Message);
    }

    [Fact]
    public void EnumerateToolsAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = Assert.Throws<InvalidOperationException>(() => client.EnumerateToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.EnumerateToolsAsync' instead", ex.Message);
    }

    [Fact]
    public async Task ListPromptsAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ListPromptsAsync(
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.ListPromptsAsync' instead", ex.Message);
    }

    [Fact]
    public void EnumeratePromptsAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = Assert.Throws<InvalidOperationException>(() => client.EnumeratePromptsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.EnumeratePromptsAsync' instead", ex.Message);
    }

    [Fact]
    public async Task ListResourceTemplatesAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ListResourceTemplatesAsync(
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.ListResourceTemplatesAsync' instead", ex.Message);
    }

    [Fact]
    public void EnumerateResourceTemplatesAsync_Throws_When_Not_McpClient()
    {
        var client = new Mock<IMcpClient>(MockBehavior.Strict).Object;

        var ex = Assert.Throws<InvalidOperationException>(() => client.EnumerateResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Prefer using 'McpClient.EnumerateResourceTemplatesAsync' instead", ex.Message);
    }

    [Fact]
    public async Task PingAsync_Forwards_To_McpClient_SendRequestAsync()
    {
        var mockClient = new Mock<McpClient> { CallBase = true };

        mockClient
            .Setup(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(new object(), McpJsonUtilities.DefaultOptions),
            });

        IMcpClient client = mockClient.Object;

        await client.PingAsync(TestContext.Current.CancellationToken);

        mockClient.Verify(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPromptAsync_Forwards_To_McpClient_SendRequestAsync()
    {
        var mockClient = new Mock<McpClient> { CallBase = true };

        var resultPayload = new GetPromptResult 
        {
            Messages = [new()
            {
                Role = Role.User,
                Content = new TextContentBlock { Text = "hi" } 
            }] 
        };

        mockClient
            .Setup(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonUtilities.DefaultOptions),
            });

        IMcpClient client = mockClient.Object;

        var result = await client.GetPromptAsync("name", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hi", Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text);
        mockClient.Verify(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CallToolAsync_Forwards_To_McpClient_SendRequestAsync()
    {
        var mockClient = new Mock<McpClient> { CallBase = true };

        var callResult = new CallToolResult { Content = [new TextContentBlock { Text = "ok" }] };

        mockClient
            .Setup(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(callResult, McpJsonUtilities.DefaultOptions),
            });

        IMcpClient client = mockClient.Object;

        var result = await client.CallToolAsync("tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        mockClient.Verify(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubscribeToResourceAsync_Forwards_To_McpClient_SendRequestAsync()
    {
        var mockClient = new Mock<McpClient> { CallBase = true };

        mockClient
            .Setup(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(new EmptyResult(), McpJsonUtilities.DefaultOptions),
            });

        IMcpClient client = mockClient.Object;

        await client.SubscribeToResourceAsync("mcp://resource/1", TestContext.Current.CancellationToken);

        mockClient.Verify(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnsubscribeFromResourceAsync_Forwards_To_McpClient_SendRequestAsync()
    {
        var mockClient = new Mock<McpClient> { CallBase = true };

        mockClient
            .Setup(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(new EmptyResult(), McpJsonUtilities.DefaultOptions),
            });

        IMcpClient client = mockClient.Object;

        await client.UnsubscribeFromResourceAsync("mcp://resource/1", TestContext.Current.CancellationToken);

        mockClient.Verify(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_Forwards_To_McpClient_SendRequestAsync()
    {
        var mockClient = new Mock<McpClient> { CallBase = true };

        var completion = new Completion { Values = ["one", "two"] };
        var resultPayload = new CompleteResult { Completion = completion };

        mockClient
            .Setup(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonUtilities.DefaultOptions),
            });

        IMcpClient client = mockClient.Object;

        var result = await client.CompleteAsync(new PromptReference { Name = "p" }, "arg", "val", TestContext.Current.CancellationToken);

        Assert.Contains("one", result.Completion.Values);
        mockClient.Verify(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadResourceAsync_String_Forwards_To_McpClient_SendRequestAsync()
    {
        var mockClient = new Mock<McpClient> { CallBase = true };

        var resultPayload = new ReadResourceResult { Contents = [] };

        mockClient
            .Setup(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonUtilities.DefaultOptions),
            });

        IMcpClient client = mockClient.Object;

        var result = await client.ReadResourceAsync("mcp://resource/1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        mockClient.Verify(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadResourceAsync_Uri_Forwards_To_McpClient_SendRequestAsync()
    {
        var mockClient = new Mock<McpClient> { CallBase = true };

        var resultPayload = new ReadResourceResult { Contents = [] };

        mockClient
            .Setup(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonUtilities.DefaultOptions),
            });

        IMcpClient client = mockClient.Object;

        var result = await client.ReadResourceAsync(new Uri("mcp://resource/1"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        mockClient.Verify(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadResourceAsync_Template_Forwards_To_McpClient_SendRequestAsync()
    {
        var mockClient = new Mock<McpClient> { CallBase = true };

        var resultPayload = new ReadResourceResult { Contents = [] };

        mockClient
            .Setup(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonUtilities.DefaultOptions),
            });

        IMcpClient client = mockClient.Object;

        var result = await client.ReadResourceAsync("mcp://resource/{id}", new Dictionary<string, object?> { ["id"] = 1 }, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        mockClient.Verify(c => c.SendRequestAsync(It.IsAny<JsonRpcRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
