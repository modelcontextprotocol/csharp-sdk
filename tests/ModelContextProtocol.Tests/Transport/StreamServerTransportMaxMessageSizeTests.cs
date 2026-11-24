using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Transport;

public class StreamServerTransportMaxMessageSizeTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private const int MaxMessageSize = 1024;

    // A string value long enough to push a message over MaxMessageSize. It contains the characters a
    // naive scanner could trip over: quotes, backslashes, braces, and brackets inside a string.
    private static readonly string s_largeValue = string.Concat(Enumerable.Repeat("""abc\"}{][\\ """, 1_000));

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public async Task Oversized_Request_With_Id_After_Params_Is_Answered_With_InvalidRequest(int chunkSize)
    {
        // This SDK's client writes "id" after "params", so the id of an oversized request can come
        // at its very end, long after the transport stopped buffering the message.
        string oversized = $$$"""{"jsonrpc":"2.0","method":"tools/call","params":{"name":"echo","arguments":{"text":"{{{s_largeValue}}}"}},"id":"req-1"}""";
        Assert.True(Encoding.UTF8.GetByteCount(oversized) > MaxMessageSize);

        await using var harness = new TransportHarness(MaxMessageSize, LoggerFactory);

        await harness.WriteAsync(oversized + "\n", chunkSize);
        await harness.WriteAsync("""{"jsonrpc":"2.0","id":2,"method":"ping"}""" + "\n");

        var error = Assert.IsType<JsonRpcError>(await harness.ReadOutputAsync());
        Assert.Equal("req-1", error.Id.ToString());
        Assert.Equal((int)McpErrorCode.InvalidRequest, error.Error.Code);
        Assert.Contains("maximum message size", error.Error.Message);

        // The oversized request never reaches the session; the next message does.
        var next = Assert.IsType<JsonRpcRequest>(await harness.ReadMessageAsync());
        Assert.Equal("2", next.Id.ToString());
        Assert.Equal(RequestMethods.Ping, next.Method);
        Assert.True(harness.Transport.IsConnected);
    }

    [Fact]
    public async Task Oversized_Request_With_Numeric_Id_Before_Params_Is_Answered_With_InvalidRequest()
    {
        string oversized = $$$"""{"jsonrpc":"2.0","id":41,"method":"tools/call","params":{"text":"{{{s_largeValue}}}"}}""";

        await using var harness = new TransportHarness(MaxMessageSize, LoggerFactory);

        await harness.WriteAsync(oversized + "\n", chunkSize: 256);

        var error = Assert.IsType<JsonRpcError>(await harness.ReadOutputAsync());
        Assert.Equal(new RequestId(41), error.Id);
        Assert.Equal((int)McpErrorCode.InvalidRequest, error.Error.Code);
    }

    [Fact]
    public async Task Oversized_Request_At_End_Of_Stream_Without_Newline_Is_Answered()
    {
        string oversized = $$$"""{"jsonrpc":"2.0","method":"tools/call","params":{"text":"{{{s_largeValue}}}"},"id":7}""";

        await using var harness = new TransportHarness(MaxMessageSize, LoggerFactory);

        await harness.WriteAsync(oversized, chunkSize: 300);
        harness.CompleteInput();

        var error = Assert.IsType<JsonRpcError>(await harness.ReadOutputAsync());
        Assert.Equal(new RequestId(7), error.Id);
        Assert.Equal((int)McpErrorCode.InvalidRequest, error.Error.Code);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","result":{"text":"{{VALUE}}"},"id":17}""")]
    [InlineData("""{"jsonrpc":"2.0","id":17,"error":{"code":-32603,"message":"{{VALUE}}"}}""")]
    public async Task Oversized_Response_Fails_The_Pending_Request_Locally(string template)
    {
        // A response carries the id of a request this server sent. Answering it with an error would
        // send the server's own id back to the client, so the transport fails the request locally.
        string oversized = template.Replace("{{VALUE}}", s_largeValue);

        await using var harness = new TransportHarness(MaxMessageSize, LoggerFactory);

        await harness.WriteAsync(oversized + "\n", chunkSize: 512);
        await harness.WriteAsync("""{"jsonrpc":"2.0","id":2,"method":"ping"}""" + "\n");

        var error = Assert.IsType<JsonRpcError>(await harness.ReadMessageAsync());
        Assert.Equal(new RequestId(17), error.Id);
        Assert.Equal((int)McpErrorCode.InternalError, error.Error.Code);
        Assert.Contains("maximum message size", error.Error.Message);

        Assert.IsType<JsonRpcRequest>(await harness.ReadMessageAsync());

        // By now any reply to the peer would already have been written, since replies are sent in input order.
        Assert.Equal(0, harness.OutputLength);
    }

    [Fact]
    public async Task Oversized_Notification_Is_Dropped()
    {
        string oversized = $$$"""{"jsonrpc":"2.0","method":"notifications/message","params":{"level":"info","data":"{{{s_largeValue}}}"}}""";

        await using var harness = new TransportHarness(MaxMessageSize, LoggerFactory);

        await harness.WriteAsync(oversized + "\n", chunkSize: 512);
        await harness.WriteAsync("""{"jsonrpc":"2.0","id":2,"method":"ping"}""" + "\n");

        var next = Assert.IsType<JsonRpcRequest>(await harness.ReadMessageAsync());
        Assert.Equal(new RequestId(2), next.Id);
        Assert.Equal(0, harness.OutputLength);
        Assert.Contains(MockLoggerProvider.LogMessages, m => m.LogLevel == LogLevel.Warning && m.Message.Contains("exceeds the maximum message size"));
    }

    [Fact]
    public async Task Message_At_The_Limit_Is_Accepted_And_One_Byte_Over_Is_Rejected()
    {
        const string AtLimit = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";
        const string OverLimit = """{"jsonrpc":"2.0","id":22,"method":"ping"}""";
        int limit = Encoding.UTF8.GetByteCount(AtLimit);
        Assert.Equal(limit + 1, Encoding.UTF8.GetByteCount(OverLimit));

        await using var harness = new TransportHarness(limit, LoggerFactory);

        await harness.WriteAsync(AtLimit + "\n" + OverLimit + "\n");

        var accepted = Assert.IsType<JsonRpcRequest>(await harness.ReadMessageAsync());
        Assert.Equal(new RequestId(1), accepted.Id);

        var error = Assert.IsType<JsonRpcError>(await harness.ReadOutputAsync());
        Assert.Equal(new RequestId(22), error.Id);
        Assert.Equal((int)McpErrorCode.InvalidRequest, error.Error.Code);
    }

    [Fact]
    public async Task Without_A_Limit_A_Large_Message_Is_Accepted()
    {
        string large = $$$"""{"jsonrpc":"2.0","method":"tools/call","params":{"text":"{{{s_largeValue}}}"},"id":3}""";

        await using var harness = new TransportHarness(maxMessageSize: null, LoggerFactory);

        await harness.WriteAsync(large + "\n", chunkSize: 100);

        var request = Assert.IsType<JsonRpcRequest>(await harness.ReadMessageAsync());
        Assert.Equal(new RequestId(3), request.Id);
        Assert.Equal(JsonSerializer.Deserialize<string>($"\"{s_largeValue}\""), request.Params?["text"]?.GetValue<string>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxMessageSize_Must_Be_Positive(long value)
    {
        var options = new StreamServerTransportOptions();

        Assert.Throws<ArgumentOutOfRangeException>("value", () => options.MaxMessageSize = value);
        Assert.Null(options.MaxMessageSize);

        options.MaxMessageSize = 1;
        options.MaxMessageSize = null;
        Assert.Null(options.MaxMessageSize);
    }

    [Fact]
    public async Task Oversized_Sampling_Result_Fails_The_Servers_Request_And_Keeps_The_Session()
    {
        Pipe clientToServer = new(), serverToClient = new();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(XunitLoggerProvider);
        services
            .AddMcpServer()
            .WithStreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream(),
                options => options.MaxMessageSize = 64 * 1024)
            .WithTools<SamplingTool>();

        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var server = serviceProvider.GetRequiredService<McpServer>();
        Task serverTask = server.RunAsync(cts.Token);

        string sampledText = "x";
        var clientOptions = new McpClientOptions
        {
            // Exercise the server-to-client sampling request, which the 2026-07-28 protocol replaces with MRTR.
            ProtocolVersion = "2025-11-25",
            DiscoverProbeTimeout = TestConstants.DefaultTimeout,
            Capabilities = new() { Sampling = new() },
            Handlers = new()
            {
                SamplingHandler = (_, _, _) => new(new CreateMessageResult
                {
                    Content = [new TextContentBlock { Text = sampledText }],
                    Model = "test-model",
                }),
            },
        };

        await using (McpClient client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream(), LoggerFactory),
            clientOptions,
            LoggerFactory,
            TestContext.Current.CancellationToken))
        {
            sampledText = new string('x', 256 * 1024);
            var tooLarge = await client.CallToolAsync("sample", cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains("maximum message size", Assert.IsType<TextContentBlock>(Assert.Single(tooLarge.Content)).Text);

            // The session is still usable afterwards.
            sampledText = "small";
            var ok = await client.CallToolAsync("sample", cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("Sampled 5 characters.", Assert.IsType<TextContentBlock>(Assert.Single(ok.Content)).Text);
        }

        await cts.CancelAsync();
        clientToServer.Writer.Complete();
        serverToClient.Writer.Complete();
        await serverTask;
    }

    public sealed class SamplingTool
    {
        [McpServerTool(Name = "sample"), Description("Samples once and reports the result.")]
        public static async Task<string> SampleAsync(McpServer server, CancellationToken cancellationToken)
        {
            try
            {
                var result = await server.SampleAsync(new CreateMessageRequestParams
                {
                    Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "hi" }] }],
                    MaxTokens = 10,
                }, cancellationToken);

                return $"Sampled {Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text.Length} characters.";
            }
            catch (McpProtocolException ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>A <see cref="StreamServerTransport"/> over in-memory pipes.</summary>
    private sealed class TransportHarness : IAsyncDisposable
    {
        private readonly Pipe _input = new();
        private readonly Pipe _output = new();
        private readonly StreamReader _outputReader;
        private long _outputLength;

        public TransportHarness(long? maxMessageSize, ILoggerFactory loggerFactory)
        {
            Transport = new StreamServerTransport(
                _input.Reader.AsStream(),
                new CountingStream(_output.Writer.AsStream(), this),
                serverName: null,
                loggerFactory,
                new StreamServerTransportOptions { MaxMessageSize = maxMessageSize });
            _outputReader = new StreamReader(_output.Reader.AsStream(), Encoding.UTF8);
        }

        public StreamServerTransport Transport { get; }

        public long OutputLength => Interlocked.Read(ref _outputLength);

        public async Task WriteAsync(string text, int chunkSize = int.MaxValue)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            for (int offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                int count = Math.Min(chunkSize, bytes.Length - offset);
                await _input.Writer.WriteAsync(bytes.AsMemory(offset, count), TestContext.Current.CancellationToken);
            }
        }

        public void CompleteInput() => _input.Writer.Complete();

        public async Task<JsonRpcMessage> ReadMessageAsync()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TestConstants.DefaultTimeout);
            return await Transport.MessageReader.ReadAsync(cts.Token);
        }

        public async Task<JsonRpcMessage?> ReadOutputAsync()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TestConstants.DefaultTimeout);
            var line = await _outputReader.ReadLineAsync(
#if NET
                cts.Token
#endif
            );

            Assert.NotNull(line);
            return JsonSerializer.Deserialize<JsonRpcMessage>(line, McpJsonUtilities.DefaultOptions);
        }

        public async ValueTask DisposeAsync()
        {
            await Transport.DisposeAsync();
            _input.Writer.Complete();
            _output.Writer.Complete();
            _outputReader.Dispose();
        }

        private sealed class CountingStream(Stream inner, TransportHarness owner) : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count)
            {
                Interlocked.Add(ref owner._outputLength, count);
                inner.Write(buffer, offset, count);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Interlocked.Add(ref owner._outputLength, count);
                return inner.WriteAsync(buffer, offset, count, cancellationToken);
            }

#if NET
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Interlocked.Add(ref owner._outputLength, buffer.Length);
                return inner.WriteAsync(buffer, cancellationToken);
            }
#endif

            public override void Flush() => inner.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
