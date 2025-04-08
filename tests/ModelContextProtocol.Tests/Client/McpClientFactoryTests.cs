using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using Moq;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Client;

public class McpClientFactoryTests
{
    [Fact]
    public async Task CreateAsync_WithInvalidArgs_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>("clientTransport", () => McpClientFactory.CreateAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_NopTransport_ReturnsClient()
    {
        // Act
        await using var client = await McpClientFactory.CreateAsync(
            new NopTransport(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(client);
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
                Sampling = new SamplingCapability
                {
                    SamplingHandler = (c, p, t) => Task.FromResult(
                        new CreateMessageResult { 
                            Content = new Content { Text = "result" }, 
                            Model = "test-model", 
                            Role = "test-role", 
                            StopReason = "endTurn" 
                    }),
                },
                Roots = new RootsCapability
                {
                    ListChanged = true,
                    RootsHandler = (t, r) => Task.FromResult(new ListRootsResult { Roots = [] }),
                }
            }
        };

        var clientTransport = (IClientTransport)Activator.CreateInstance(transportType)!;
        IMcpClient? client = null;

        var actionTask = McpClientFactory.CreateAsync(clientTransport, clientOptions, new Mock<ILoggerFactory>().Object, CancellationToken.None);

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
        private readonly Channel<IJsonRpcMessage> _channel = Channel.CreateUnbounded<IJsonRpcMessage>();

        public bool IsConnected => true;

        public ChannelReader<IJsonRpcMessage> MessageReader => _channel.Reader;

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult<ITransport>(this);

        public ValueTask DisposeAsync() => default;

        public string Name => "Test Nop Transport";

        public virtual Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
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
                        }),
                    });
                    break;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FailureTransport : NopTransport 
    {
        public const string ExpectedMessage = "Something failed";

        public override Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(ExpectedMessage);
        }
    }
}
