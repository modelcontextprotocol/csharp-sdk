using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.IO.Pipelines;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Client;

public class McpClientCreationTests
{
    [Fact]
    public async Task CreateAsync_WithInvalidArgs_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>("clientTransport", () => McpClient.CreateAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_NopTransport_ReturnsClient()
    {
        // Act
        await using var client = await McpClient.CreateAsync(
            new NopTransport(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_ThrowsCancellationException(bool preCanceled)
    {
        var cts = new CancellationTokenSource();

        if (preCanceled)
        {
            cts.Cancel();
        }

        Task t = McpClient.CreateAsync(
            new StreamClientTransport(new Pipe().Writer.AsStream(), new Pipe().Reader.AsStream()),
            cancellationToken: cts.Token);
        if (!preCanceled)
        {
            Assert.False(t.IsCompleted);
        }

        if (!preCanceled)
        {
            cts.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t);
    }

    [Theory]
    [InlineData(typeof(NopTransport))]
    [InlineData(typeof(FailureTransport))]
    public async Task CreateAsync_WithCapabilitiesOptions(Type transportType)
    {
        // Arrange
        var clientOptions = new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Roots = new RootsCapability
                {
                    ListChanged = true,
                }
            },
            Handlers = new()
            {
                RootsHandler = async (t, r) => new ListRootsResult { Roots = [] },
                SamplingHandler = async (c, p, t) => new CreateMessageResult
                {
                    Content = [new TextContentBlock { Text = "result" }],
                    Model = "test-model",
                    Role = Role.User,
                    StopReason = "endTurn",
                }
            }
        };

        var clientTransport = (IClientTransport)Activator.CreateInstance(transportType)!;
        McpClient? client = null;

        var actionTask = McpClient.CreateAsync(clientTransport, clientOptions, loggerFactory: null, CancellationToken.None);

        // Act
        if (clientTransport is FailureTransport)
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async() => await actionTask);
            Assert.Equal(FailureTransport.ExpectedMessage, exception.Message);
        }
        else
        {
            client = await actionTask;

            // Assert
            Assert.NotNull(client);
        }        
    }

    private class NopTransport : ITransport, IClientTransport
    {
        private readonly Channel<JsonRpcMessage> _channel = Channel.CreateUnbounded<JsonRpcMessage>();

        public bool IsConnected => true;
        public string? SessionId => null;

        public ChannelReader<JsonRpcMessage> MessageReader => _channel.Reader;

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult<ITransport>(this);

        public ValueTask DisposeAsync() => default;

        public string Name => "Test Nop Transport";

        public virtual Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            switch (message)
            {
                case JsonRpcRequest:
                    _channel.Writer.TryWrite(new JsonRpcResponse
                    {
                        Id = ((JsonRpcRequest)message).Id,
                        Result = JsonSerializer.SerializeToNode(new InitializeResult
                        {
                            Capabilities = new ServerCapabilities(),
                            ProtocolVersion = "2024-11-05",
                            ServerInfo = new Implementation
                            {
                                Name = "NopTransport",
                                Version = "1.0.0"
                            },
                        }, McpJsonUtilities.DefaultOptions),
                    });
                    break;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FailureTransport : NopTransport 
    {
        public const string ExpectedMessage = "Something failed";

        public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(ExpectedMessage);
        }
    }
}
