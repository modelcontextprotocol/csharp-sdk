using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Client;

public class McpClientCreationTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
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

    [Fact]
    public async Task CreateAsync_TransportChannelClosed_ThrowsClientTransportClosedException()
    {
        // Arrange - transport completes its read channel with ClientTransportClosedException
        // when the client tries to send the initialize request (simulating a server process
        // exit detected by the reader loop). SendMessageAsync returns successfully —
        // only the read side fails.
        var transport = new ChannelClosedDuringInitTransport();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ClientTransportClosedException>(
            () => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        var details = Assert.IsType<StdioClientCompletionDetails>(ex.Details);
        Assert.Equal(42, details.ExitCode);
        Assert.Equal(9999, details.ProcessId);
        Assert.NotNull(details.StandardErrorTail);
        Assert.Equal("Feature disabled", details.StandardErrorTail![0]);

        // Verify initialization error was logged
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Error &&
            log.Message.Contains("client initialization error"));
    }

    [Fact]
    public async Task CreateAsync_SendFails_PropagatesOriginalIOException()
    {
        // Arrange - transport throws IOException from SendMessageAsync, but the channel
        // is not completed with ClientTransportClosedException. The original IOException should
        // propagate without being wrapped in ClientTransportClosedException.
        var transport = new SendFailsDuringInitTransport();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<IOException>(
            () => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SendFailsDuringInitTransport.ExpectedMessage, ex.Message);

        // Verify initialization error was logged
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Error &&
            log.Message.Contains("client initialization error"));
    }

    private class NopTransport : ITransport, IClientTransport
    {
        private readonly Channel<JsonRpcMessage> _channel = Channel.CreateUnbounded<JsonRpcMessage>();

        public bool IsConnected => true;
        public string? SessionId => null;

        public ChannelReader<JsonRpcMessage> MessageReader => _channel.Reader;

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult<ITransport>(this);

        public ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            return default;
        }

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

    /// <summary>
    /// Simulates a transport where the read channel closes with structured completion details during
    /// initialization, as happens when a stdio server process exits before completing the handshake.
    /// The send succeeds — only the read side carries the failure.
    /// </summary>
    private sealed class ChannelClosedDuringInitTransport : ITransport, IClientTransport
    {
        private readonly Channel<JsonRpcMessage> _channel = Channel.CreateUnbounded<JsonRpcMessage>();

        public bool IsConnected => true;
        public string? SessionId => null;

        public ChannelReader<JsonRpcMessage> MessageReader => _channel.Reader;

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult<ITransport>(this);

        public ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            return default;
        }

        public string Name => "Test ChannelClosed Transport";

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            // Simulate the server process exiting: complete the channel with a
            // ClientTransportClosedException carrying structured completion details.
            // The send itself succeeds — the failure comes from the read side.
            var details = new StdioClientCompletionDetails
            {
                ExitCode = 42,
                ProcessId = 9999,
                StandardErrorTail = ["Feature disabled"],
                Exception = new IOException("MCP server process exited unexpectedly (exit code: 42)"),
            };

            _channel.Writer.TryComplete(new ClientTransportClosedException(details));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Simulates a transport where SendMessageAsync throws IOException but the channel
    /// doesn't carry a ClientTransportClosedException (e.g., a write pipe break without structured details).
    /// </summary>
    private sealed class SendFailsDuringInitTransport : NopTransport
    {
        public const string ExpectedMessage = "Failed to write to transport";

        public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            throw new IOException(ExpectedMessage);
        }
    }
}
