using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Net;

namespace ModelContextProtocol.Tests.Transport;

public class SseClientTransportTests : LoggedTest
{
    private readonly McpServerConfig _serverConfig;
    private readonly SseClientTransportOptions _transportOptions;

    public SseClientTransportTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _serverConfig = new McpServerConfig
        {
            Id = "test-server",
            Name = "Test Server",
            TransportType = TransportTypes.Sse,
            Location = "http://localhost:8080"
        };

        _transportOptions = new SseClientTransportOptions
        {
            ConnectionTimeout = TimeSpan.FromSeconds(2),
            MaxReconnectAttempts = 3,
            ReconnectDelay = TimeSpan.FromMilliseconds(50),
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["test"] = "header"
            }
        };
    }

    [Fact]
    public void Constructor_Throws_For_Null_Options()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SseClientTransport(null!, _serverConfig, LoggerFactory));
        Assert.Equal("transportOptions", exception.ParamName);
    }

    [Fact]
    public void Constructor_Throws_For_Null_Config()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SseClientTransport(_transportOptions, null!, LoggerFactory));
        Assert.Equal("serverConfig", exception.ParamName);
    }

    [Fact]
    public void Constructor_Throws_For_Null_HttpClientg()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SseClientTransport(_transportOptions, _serverConfig, null!, LoggerFactory));
        Assert.Equal("httpClient", exception.ParamName);
    }

    [Fact]
    public async Task ConnectAsync_Should_Connect_Successfully()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, LoggerFactory);

        bool firstCall = true;

        mockHttpHandler.RequestHandler = (request) =>
        {
            firstCall = false;
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("event: endpoint\r\ndata: http://localhost\r\n\r\n")
            });
        };

        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        Assert.False(firstCall);
    }

    [Fact]
    public async Task ConnectAsync_Throws_Exception_On_Failure()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, LoggerFactory);

        var retries = 0;
        mockHttpHandler.RequestHandler = (request) =>
        {
            retries++;
            throw new InvalidOperationException("Test exception");
        };

        var action = async () => await transport.ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpTransportException>(action);
        Assert.Equal("Exceeded reconnect limit", exception.Message);

        Assert.Equal(_transportOptions.MaxReconnectAttempts, retries);
    }

    [Fact]
    public async Task SendMessageAsync_Handles_Accepted_Response()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, LoggerFactory);

        var firstCall = true;
        mockHttpHandler.RequestHandler = (request) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsoluteUri == "http://localhost:8080/sseendpoint")
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("accepted")
                });
            }
            else
            {
                if (!firstCall)
                    throw new IOException("Abort");
                else
                    firstCall = false;

                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("event: endpoint\r\ndata: /sseendpoint\r\n\r\n")
                });
            }
        };

        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);
        await session.SendMessageAsync(new JsonRpcRequest() { Method = RequestMethods.Initialize, Id = new RequestId(44) }, CancellationToken.None);
        Assert.True(true);
    }

    [Fact]
    public async Task SendMessageAsync_Handles_Accepted_Json_RPC_Response()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, LoggerFactory);

        var eventSourcePipe = new Pipe();
        var eventSourceData = "event: endpoint\r\ndata: /sseendpoint\r\n\r\n"u8;
        Assert.True(eventSourceData.TryCopyTo(eventSourcePipe.Writer.GetSpan()));
        eventSourcePipe.Writer.Advance(eventSourceData.Length);
        await eventSourcePipe.Writer.FlushAsync(TestContext.Current.CancellationToken);

        var firstCall = true;
        mockHttpHandler.RequestHandler = (request) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsoluteUri == "http://localhost:8080/sseendpoint")
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("{\"jsonrpc\":\"2.0\", \"id\": \"44\", \"result\": null}")
                });
            }
            else
            {
                if (!firstCall)
                    throw new IOException("Abort");
                else
                    firstCall = false;

                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StreamContent(eventSourcePipe.Reader.AsStream()),
                });
            }
        };

        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await session.SendMessageAsync(new JsonRpcRequest() { Method = RequestMethods.Initialize, Id = new RequestId(44) }, CancellationToken.None);
        Assert.True(true);
        eventSourcePipe.Writer.Complete();
    }

    [Fact]
    public async Task ReceiveMessagesAsync_Handles_Messages()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, LoggerFactory);

        var callIndex = 0;
        mockHttpHandler.RequestHandler = (request) =>
        {
            callIndex++;

            if (callIndex == 1)
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("event: endpoint\r\ndata: /sseendpoint\r\n\r\nevent: message\r\ndata: {\"jsonrpc\":\"2.0\", \"id\": \"44\", \"method\": \"test\", \"params\": null}\r\n\r\n")
                });
            }

            throw new IOException("Abort");
        };

        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.True(session.MessageReader.TryRead(out var message));
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        Assert.Equal("\"44\"", ((JsonRpcRequest)message).Id.ToString());
    }

    [Fact]
    public async Task DisposeAsync_Should_Dispose_Resources()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        mockHttpHandler.RequestHandler = request =>
        {
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("event: endpoint\r\ndata: http://localhost\r\n\r\n")
            });
        };

        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, LoggerFactory);
        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await session.DisposeAsync();

        Assert.False(session.IsConnected);
    }
}