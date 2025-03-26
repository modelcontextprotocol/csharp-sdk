using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Transport;

public class InMemoryTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private readonly Type[] _toolTypes = [typeof(TestTool)];

    [Fact]
    public async Task TransportPair_Should_Create_Valid_Transports()
    {
        // Act - create a transport pair
        var (clientTransport, serverTransport) = InMemoryTransport.Create(LoggerFactory);

        // Assert
        Assert.NotNull(clientTransport);
        Assert.NotNull(serverTransport);
        Assert.False(clientTransport.IsConnected);
        Assert.False(serverTransport.IsConnected);

        // Cleanup
        await clientTransport.DisposeAsync();
        await serverTransport.DisposeAsync();
    }

    [Fact]
    public async Task ClientConnect_Should_StartServer_And_SetConnected()
    {
        // Arrange
        var (clientTransport, serverTransport) = InMemoryTransport.Create(LoggerFactory);

        // Act
        await clientTransport.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(clientTransport.IsConnected);
        Assert.True(serverTransport.IsConnected);

        // Cleanup
        await clientTransport.DisposeAsync();
        await serverTransport.DisposeAsync();
    }

    [Fact]
    public async Task Message_Should_Flow_From_Client_To_Server()
    {
        // Arrange
        var (clientTransport, serverTransport) = InMemoryTransport.Create(LoggerFactory);
        await clientTransport.ConnectAsync(TestContext.Current.CancellationToken);

        var message = new JsonRpcRequest
        {
            Method = "test",
            Id = RequestId.FromNumber(123),
            Params = new Dictionary<string, object> { ["text"] = "Hello, World!" }
        };

        // Act
        await clientTransport.SendMessageAsync(message, TestContext.Current.CancellationToken);
        await Task.Delay(1, TestContext.Current.CancellationToken);
        // Assert
        Assert.True(serverTransport.MessageReader.TryRead(out var receivedMessage));
        Assert.NotNull(receivedMessage);
        Assert.IsType<JsonRpcRequest>(receivedMessage);

        var request = (JsonRpcRequest)receivedMessage;
        Assert.Equal(123, request.Id.AsNumber);
        Assert.Equal("test", request.Method);

        var requestParams = (Dictionary<string, object>)request.Params!;
        Assert.Equal("Hello, World!", requestParams["text"]);

        // Cleanup
        await clientTransport.DisposeAsync();
        await serverTransport.DisposeAsync();
    }

    [Fact]
    public async Task Message_Should_Flow_From_Server_To_Client()
    {
        // Arrange
        var (clientTransport, serverTransport) = InMemoryTransport.Create(LoggerFactory);
        await clientTransport.ConnectAsync(TestContext.Current.CancellationToken);

        var message = new JsonRpcResponse
        {
            Id = RequestId.FromNumber(456),
            Result = new Dictionary<string, object> { ["text"] = "Response from server" }
        };

        // Act
        await serverTransport.SendMessageAsync(message, TestContext.Current.CancellationToken);
        await Task.Delay(1, TestContext.Current.CancellationToken);
        // Assert
        Assert.True(clientTransport.MessageReader.TryRead(out var receivedMessage));
        Assert.NotNull(receivedMessage);
        Assert.IsType<JsonRpcResponse>(receivedMessage);

        var response = (JsonRpcResponse)receivedMessage;
        Assert.Equal(456, response.Id.AsNumber);

        var responseResult = (Dictionary<string, object>)response.Result!;
        Assert.Equal("Response from server", responseResult["text"]);

        // Cleanup
        await clientTransport.DisposeAsync();
        await serverTransport.DisposeAsync();
    }


    [McpServerToolType]
    private class TestTool
    {
        [McpServerTool]
        public string Echo(string message) => message;
    }
}
