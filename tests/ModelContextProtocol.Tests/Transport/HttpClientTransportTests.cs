using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Text;

namespace ModelContextProtocol.Tests.Transport;

public class HttpClientTransportTests : LoggedTest
{
    private readonly HttpClientTransportOptions _transportOptions;

    public HttpClientTransportTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:8080"),
            ConnectionTimeout = TimeSpan.FromSeconds(2),
            Name = "Test Server",
            TransportMode = HttpTransportMode.Sse,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["test"] = "header"
            }
        };
    }

    [Fact]
    public void Constructor_Throws_For_Null_Options()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new HttpClientTransport(null!, LoggerFactory));
        Assert.Equal("transportOptions", exception.ParamName);
    }

    [Fact]
    public void Constructor_Throws_For_Null_HttpClient()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new HttpClientTransport(_transportOptions, httpClient: null!, LoggerFactory));
        Assert.Equal("httpClient", exception.ParamName);
    }

    [Fact]
    public async Task ConnectAsync_Should_Connect_Successfully()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(_transportOptions, httpClient, LoggerFactory);

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
        await using var transport = new HttpClientTransport(_transportOptions, httpClient, LoggerFactory);

        var retries = 0;
        mockHttpHandler.RequestHandler = (request) =>
        {
            retries++;
            throw new Exception("Test exception");
        };

        var exception = await Assert.ThrowsAsync<Exception>(() => transport.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Test exception", exception.Message);
        Assert.Equal(1, retries);
    }

    [Fact]
    public async Task ConnectAsync_Throws_HttpRequestException_With_ResponseBody_On_ErrorStatusCode()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(_transportOptions, httpClient, LoggerFactory);

        const string errorDetails = "Bad request: Invalid MCP protocol version";
        mockHttpHandler.RequestHandler = (request) =>
        {
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                ReasonPhrase = "Bad Request",
                Content = new StringContent(errorDetails)
            });
        };

        var httpException = await Assert.ThrowsAsync<HttpRequestException>(() => transport.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Contains(errorDetails, httpException.Message);
        Assert.Contains("400", httpException.Message);
#if NET
        Assert.Equal(HttpStatusCode.BadRequest, httpException.StatusCode);
#endif
    }

    [Fact]
    public async Task SendMessageAsync_Handles_Accepted_Response()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(_transportOptions, httpClient, LoggerFactory);

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
        await session.SendMessageAsync(new JsonRpcRequest { Method = RequestMethods.Initialize, Id = new RequestId(44) }, CancellationToken.None);
        Assert.True(true);
    }

    [Fact]
    public async Task SendMessageAsync_Throws_HttpRequestException_With_ResponseBody_On_ErrorStatusCode()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(_transportOptions, httpClient, LoggerFactory);

        var firstCall = true;
        const string errorDetails = "Invalid JSON-RPC message format: missing 'id' field";

        mockHttpHandler.RequestHandler = (request) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsoluteUri == "http://localhost:8080/sseendpoint")
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ReasonPhrase = "Bad Request",
                    Content = new StringContent(errorDetails)
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
        var httpException = await Assert.ThrowsAsync<HttpRequestException>(() =>
            session.SendMessageAsync(new JsonRpcRequest { Method = RequestMethods.Initialize, Id = new RequestId(44) }, CancellationToken.None));

        Assert.Contains(errorDetails, httpException.Message);
        Assert.Contains("400", httpException.Message);
#if NET
        Assert.Equal(HttpStatusCode.BadRequest, httpException.StatusCode);
#endif
    }

    [Fact]
    public async Task ReceiveMessagesAsync_Handles_Messages()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(_transportOptions, httpClient, LoggerFactory);

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
        Assert.Equal("44", ((JsonRpcRequest)message).Id.ToString());
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

        await using var transport = new HttpClientTransport(_transportOptions, httpClient, LoggerFactory);
        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await session.DisposeAsync();

        var transportBase = Assert.IsAssignableFrom<TransportBase>(session);
        Assert.False(transportBase.IsConnected);
    }

    [Fact]
    public async Task StreamableHttp_InitialGetSseConnection_DoesNotCountAgainstMaxReconnectionAttempts()
    {
        // Arrange: The initial GET SSE connection (with no Last-Event-ID) is the initial connection,
        // not a reconnection. It should not count against MaxReconnectionAttempts.
        // With MaxReconnectionAttempts=2, we expect 1 initial + 2 reconnection = 3 total GET requests.
        const int MaxReconnectionAttempts = 2;

        var getRequestCount = 0;
        var allGetRequestsDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:8080"),
            TransportMode = HttpTransportMode.StreamableHttp,
            MaxReconnectionAttempts = MaxReconnectionAttempts,
            DefaultReconnectionInterval = TimeSpan.FromMilliseconds(1),
        };

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(options, httpClient, LoggerFactory);

        mockHttpHandler.RequestHandler = (request) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                // Return a successful initialize response with a session-id header.
                // This triggers ReceiveUnsolicitedMessagesAsync which starts the GET SSE stream.
                var response = new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26","capabilities":{},"serverInfo":{"name":"TestServer","version":"1.0.0"}}}""",
                        Encoding.UTF8,
                        "application/json"),
                };
                response.Headers.Add("Mcp-Session-Id", "test-session");
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Get)
            {
                // Return 500 for all GET SSE requests to force the retry loop to exhaust all attempts.
                var count = Interlocked.Increment(ref getRequestCount);
                if (count == 1 + MaxReconnectionAttempts)
                {
                    allGetRequestsDone.TrySetResult(true);
                }
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                });
            }

            if (request.Method == HttpMethod.Delete)
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                });
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method}");
        };

        // Act - Connect and send the initialize request, which starts the background GET SSE task.
        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);
        await session.SendMessageAsync(
            new JsonRpcRequest { Method = RequestMethods.Initialize, Id = new RequestId(1) },
            TestContext.Current.CancellationToken);

        // Wait for all expected GET requests to be made before disposing.
        await allGetRequestsDone.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Assert - Total GET requests = 1 initial connection + MaxReconnectionAttempts reconnections.
        Assert.Equal(1 + MaxReconnectionAttempts, getRequestCount);
    }
}