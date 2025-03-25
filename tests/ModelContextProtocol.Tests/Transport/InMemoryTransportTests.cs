using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Transport;

public class InMemoryTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private readonly Type[] _toolTypes = [typeof(TestTool)];

    [Fact]
    public async Task Constructor_Should_Initialize_With_Valid_Parameters()
    {
        // Act
        await using var transport = InMemoryTransport.Create(LoggerFactory, _toolTypes);

        // Assert
        Assert.NotNull(transport);
    }

    [Fact]
    public void Constructor_Throws_For_Null_Parameters()
    {
        Assert.Throws<ArgumentException>("toolTypes",
            () => InMemoryTransport.Create(LoggerFactory, Array.Empty<Type>()));

        Assert.Throws<ArgumentNullException>("toolTypes",
            () => InMemoryTransport.Create(LoggerFactory, null!));
    }

    [Fact]
    public async Task ConnectAsync_Should_Set_Connected_State()
    {
        // Arrange
        var transport = InMemoryTransport.Create(LoggerFactory, _toolTypes);

        // Act
        var clientTransport = (IClientTransport)transport;
        await clientTransport.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(transport.IsConnected);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task StartListeningAsync_Should_Set_Connected_State()
    {
        // Arrange
        await using var transport = InMemoryTransport.Create(LoggerFactory, _toolTypes);

        // Act
        var serverTransport = (IServerTransport)transport;
        await serverTransport.StartListeningAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(transport.IsConnected);
    }

    [Theory]
    [InlineData("Hello, World!")]
    [InlineData("上下文伺服器")]
    [InlineData("🔍 🚀 👍")]
    public async Task SendMessageAsync_Should_Preserve_Characters(string messageText)
    {
        // Arrange
        var transport = InMemoryTransport.Create(LoggerFactory, _toolTypes);

        IServerTransport serverTransport = transport;
        await serverTransport.StartListeningAsync(TestContext.Current.CancellationToken);

        // Ensure transport is fully initialized
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var chineseMessage = new JsonRpcRequest
        {
            Method = "test",
            Id = RequestId.FromNumber(44),
            Params = new Dictionary<string, object>
            {
                ["text"] = messageText
            }
        };

        // Act & Assert - Chinese
        await transport.SendMessageAsync(chineseMessage, TestContext.Current.CancellationToken);

        Assert.True(transport.MessageReader.TryRead(out var receivedMessage));
        Assert.NotNull(receivedMessage);
        Assert.IsType<JsonRpcRequest>(receivedMessage);
        var chineseRequest = (JsonRpcRequest)receivedMessage;
        var chineseParams = (Dictionary<string, object>)chineseRequest.Params!;
        Assert.Equal(messageText, (string)chineseParams["text"]);

        await transport.DisposeAsync();
    }


    [Fact]
    public async Task SendMessageAsync_Throws_Exception_If_Not_Connected()
    {
        // Arrange
        await using var transport = InMemoryTransport.Create(LoggerFactory, _toolTypes);

        var message = new JsonRpcRequest { Method = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<McpTransportException>(
            () => transport.SendMessageAsync(message, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_Should_Dispose_Resources()
    {
        // Arrange
        var transport = InMemoryTransport.Create(LoggerFactory, _toolTypes);
        IServerTransport serverTransport = transport;
        await serverTransport.StartListeningAsync(TestContext.Current.CancellationToken);

        // Act
        await transport.DisposeAsync();

        // Assert
        Assert.False(transport.IsConnected);
    }

    [McpServerToolType]
    private class TestTool
    {
        [McpServerTool]
        public string Echo(string message) => message;
    }
}
