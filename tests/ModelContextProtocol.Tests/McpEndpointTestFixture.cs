using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Shared;
using ModelContextProtocol.Protocol.Transport;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Test fixture for McpEndpoint tests that provides shared transport implementations.
/// </summary>
public class McpEndpointTestFixture() : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

    /// <summary>
    /// Creates a test transport.
    /// </summary>
    internal TestCancellationTransport CreateTransport() => new();

    /// <summary>
    /// Creates a test client endpoint.
    /// </summary>
    internal TestMcpJsonRpcEndpoint CreateEndpoint() => new();


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

    internal class TestMcpJsonRpcEndpoint(LoggerFactory? loggerFactory = null)
        : McpJsonRpcEndpoint(loggerFactory ?? new())
    {
        public override string EndpointName => "TestEndpoint";

        public void Start(
            ITransport transport,
            CancellationToken token)
            => StartSession(transport, token);
    }
}
