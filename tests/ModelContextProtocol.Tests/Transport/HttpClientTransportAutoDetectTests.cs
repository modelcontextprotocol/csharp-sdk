using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;
using System.Net;

namespace ModelContextProtocol.Tests.Transport;

public class HttpClientTransportAutoDetectTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task AutoDetectMode_UsesStreamableHttp_WhenServerSupportsIt()
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost"),
            TransportMode = HttpTransportMode.AutoDetect,
            Name = "AutoDetect test client"
        };

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(options, httpClient, LoggerFactory);

        // Simulate successful Streamable HTTP response for initialize
        mockHttpHandler.RequestHandler = (request) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":\"init-id\",\"result\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{\"tools\":{}}}}"),
                    Headers =
                    {
                        { "Content-Type", "application/json" },
                        { "mcp-session-id", "test-session" }
                    }
                });
            }

            // Shouldn't reach here for successful Streamable HTTP
            throw new InvalidOperationException("Unexpected request");
        };

        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);
        
        // The auto-detecting transport should be returned
        Assert.NotNull(session);
    }

    [Fact]
    public async Task AutoDetectMode_WhenBothTransportsFail_ThrowsInvalidOperationException()
    {
        // Regression test: when Streamable HTTP POST fails (e.g. 403) and the SSE GET
        // fallback also fails (e.g. 405), ConnectAsync should wrap the error in an
        // InvalidOperationException. Previously, CloseAsync() would re-throw the
        // HttpRequestException from the faulted _receiveTask, preempting the wrapping.
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost"),
            TransportMode = HttpTransportMode.AutoDetect,
            Name = "AutoDetect test client"
        };

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(options, httpClient, LoggerFactory);

        mockHttpHandler.RequestHandler = (request) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                // Streamable HTTP POST fails with 403 (auth error)
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Forbidden,
                    Content = new StringContent("Forbidden")
                });
            }

            if (request.Method == HttpMethod.Get)
            {
                // SSE GET fallback fails with 405
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.MethodNotAllowed,
                    Content = new StringContent("Method Not Allowed")
                });
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method}");
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Failed to connect transport", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact] 
    public async Task AutoDetectMode_FallsBackToSse_WhenStreamableHttpFails()
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost"),
            TransportMode = HttpTransportMode.AutoDetect,
            Name = "AutoDetect test client"
        };

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(options, httpClient, LoggerFactory);

        var requestCount = 0;

        mockHttpHandler.RequestHandler = (request) =>
        {
            requestCount++;

            if (request.Method == HttpMethod.Post && requestCount == 1)
            {
                // First POST (Streamable HTTP) fails
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.NotFound,
                    Content = new StringContent("Streamable HTTP not supported")
                });
            }

            if (request.Method == HttpMethod.Get)
            {
                // SSE connection request
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("event: endpoint\r\ndata: /sse-endpoint\r\n\r\n"),
                    Headers = { { "Content-Type", "text/event-stream" } }
                });
            }

            if (request.Method == HttpMethod.Post && requestCount > 1)
            {
                // Subsequent POST to SSE endpoint succeeds
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("accepted")
                });
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method}, count: {requestCount}");
        };

        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);
        
        // The auto-detecting transport should be returned
        Assert.NotNull(session);
    }
}