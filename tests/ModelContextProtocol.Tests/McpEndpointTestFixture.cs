using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Test fixture for McpEndpoint tests that provides shared transport implementations.
/// </summary>
public class McpEndpointTestFixture() : IAsyncDisposable
{
    /// <summary>
    /// Creates a test transport.
    /// </summary>
    internal TestCancellationTransport CreateTransport() => new();
    internal IClientTransport CreateClientTransport(ITransport? transport = default)
        => new TestCancellationClientTransport(transport ?? CreateTransport());
    

    /// <summary>
    /// Creates a test client endpoint.
    /// </summary>
    internal async Task<IMcpClient> CreateClientEndpointAsync(
        Func<McpServerConfig, ILoggerFactory?, IClientTransport>? transportFactory = default)
    {
        transportFactory ??= (_, _) => CreateClientTransport();
        return await McpClientFactory.CreateAsync(new()
        {
            Id = "TestServer",
            Name = "Test Server",
            TransportType = "TestTransport",
        }, createTransportFunc: transportFactory);
    }
    internal Task<IMcpClient> CreateClientEndpointAsync(IClientTransport transport)
        => CreateClientEndpointAsync((_, _) => transport);
    internal Task<IMcpClient> CreateClientEndpointAsync(ITransport transport)
        => CreateClientEndpointAsync(new TestCancellationClientTransport(transport));

    internal async Task<IMcpServer> CreateServerEndpointAsync(ITransport transport)
    {
        var server = McpServerFactory.Create(transport, new()
        {
            ServerInfo = new()
            {
                Name = "TestServer",
                Version = "1.0.0",
            }
        });
        await server.RunAsync();
        return server;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    
    internal class TestCancellationTransport : ITransport
    {
        public bool IsConnected => true;
        public List<IJsonRpcMessage> SentMessages { get; } = [];
        public ChannelReader<IJsonRpcMessage> MessageReader { get; init; }
            = Channel.CreateUnbounded<IJsonRpcMessage>().Reader;
        public Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal class TestCancellationClientTransport(ITransport transport) : IClientTransport
    {        
        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(transport);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
