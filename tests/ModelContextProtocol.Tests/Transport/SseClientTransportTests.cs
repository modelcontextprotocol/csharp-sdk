using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Reflection;

namespace ModelContextProtocol.Tests.Transport;

public class SseClientTransportTests
{
    private readonly McpServerConfig _serverConfig;
    private readonly SseClientTransportOptions _transportOptions;

    public SseClientTransportTests()
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
    public async Task Constructor_Should_Initialize_With_Valid_Parameters()
    {
        // Act
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, NullLoggerFactory.Instance);

        // Assert
        Assert.NotNull(transport);

        PropertyInfo? getOptions = transport.GetType().GetProperty("Options", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(getOptions);
        var options = (SseClientTransportOptions)getOptions.GetValue(transport)!;

        Assert.Equal(TimeSpan.FromSeconds(2), options.ConnectionTimeout);
        Assert.Equal(3, options.MaxReconnectAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(50), options.ReconnectDelay);
        Assert.NotNull(options.AdditionalHeaders);
        Assert.Equal("header", options.AdditionalHeaders["test"]);
    }

    [Fact]
    public void Constructor_Throws_For_Null_Options()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SseClientTransport(null!, _serverConfig, NullLoggerFactory.Instance));
        Assert.Equal("transportOptions", exception.ParamName);
    }

    [Fact]
    public void Constructor_Throws_For_Null_Config()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SseClientTransport(_transportOptions, null!, NullLoggerFactory.Instance));
        Assert.Equal("serverConfig", exception.ParamName);
    }

    [Fact]
    public void Constructor_Throws_For_Null_HttpClientg()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SseClientTransport(_transportOptions, _serverConfig, null!, NullLoggerFactory.Instance));
        Assert.Equal("httpClient", exception.ParamName);
    }

    [Fact]
    public async Task ConnectAsync_Should_Connect_Successfully()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, NullLoggerFactory.Instance);

        bool firstCall = true;

        mockHttpHandler.RequestHandler = async (request) =>
        {
            if (!firstCall)
            {
                Assert.True(transport.IsConnected);
                await transport.CloseAsync();
            }

            firstCall = false;
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("event: endpoint\r\ndata: http://localhost\r\n\r\n")
            };
        };

        await transport.ConnectAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConnectAsync_Throws_If_Already_Connected()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, NullLoggerFactory.Instance);
        var tcsConnected = new TaskCompletionSource();
        var tcsDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callIndex = 0;

        mockHttpHandler.RequestHandler = async (request) =>
        {
            switch (callIndex++)
            {
                case 0:
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent("event: endpoint\r\ndata: http://localhost\r\n\r\n")
                    };
                case 1:
                    tcsConnected.SetResult();
                    await tcsDone.Task;
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent("")
                    };
                default:
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent("")
                    };
            }
        };

        var task = transport.ConnectAsync(TestContext.Current.CancellationToken);
        await tcsConnected.Task;
        Assert.True(transport.IsConnected);
        var action = async () => await transport.ConnectAsync();
        var exception = await Assert.ThrowsAsync<McpTransportException>(action);
        Assert.Equal("Transport is already connected", exception.Message);
        tcsDone.SetResult();
        await transport.CloseAsync();
        await task;
    }

    [Fact]
    public async Task ConnectAsync_Throws_Exception_On_Failure()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, NullLoggerFactory.Instance);

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
    public async Task SendMessageAsync_Throws_Exception_If_MessageEndpoint_Not_Set()
    {
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, NullLoggerFactory.Instance);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendMessageAsync(new JsonRpcRequest() { Method = "test" }, CancellationToken.None));
    }

    [Fact]
    public async Task SendMessageAsync_Handles_Accepted_Response()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, NullLoggerFactory.Instance);

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

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await transport.SendMessageAsync(new JsonRpcRequest() { Method = "initialize", Id = RequestId.FromNumber(44) }, CancellationToken.None);
        Assert.True(true);
    }

    [Fact]
    public async Task SendMessageAsync_Handles_Accepted_Json_RPC_Response()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, NullLoggerFactory.Instance);

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
                    Content = new StringContent("event: endpoint\r\ndata: /sseendpoint\r\n\r\n")
                });
            }
        };

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await transport.SendMessageAsync(new JsonRpcRequest() { Method = "initialize", Id = RequestId.FromNumber(44) }, CancellationToken.None);
        Assert.True(true);
    }

    [Fact]
    public async Task ReceiveMessagesAsync_Handles_Messages()
    {
        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, httpClient, NullLoggerFactory.Instance);

        var callIndex = 0;
        mockHttpHandler.RequestHandler = (request) =>
        {
            callIndex++;

            if (callIndex == 1)
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("event: endpoint\r\ndata: /sseendpoint\r\n\r\n")
                });
            }
            else if (callIndex == 2)
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("event: message\r\ndata: {\"jsonrpc\":\"2.0\", \"id\": \"44\", \"method\": \"test\", \"params\": null}\r\n\r\n")
                });
            }

            throw new IOException("Abort");
        };

        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.True(transport.MessageReader.TryRead(out var message));
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        Assert.Equal("44", ((JsonRpcRequest)message).Id.AsString);
    }

    [Fact]
    public async Task CloseAsync_Should_Dispose_Resources()
    {
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, NullLoggerFactory.Instance);

        await transport.CloseAsync();

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_Should_Dispose_Resources()
    {
        await using var transport = new SseClientTransport(_transportOptions, _serverConfig, NullLoggerFactory.Instance);

        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
    }
}