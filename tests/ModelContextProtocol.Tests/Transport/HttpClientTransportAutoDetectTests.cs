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

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AutoDetectMode_DoesNotFallBackToSse_OnAuthError(HttpStatusCode authStatusCode)
    {
        // Auth errors (401, 403) are not transport-related — the server understood the
        // request but rejected the credentials. The SDK should propagate the error
        // immediately instead of falling back to SSE, which would mask the real cause.
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost"),
            TransportMode = HttpTransportMode.AutoDetect,
            Name = "AutoDetect test client"
        };

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(options, httpClient, LoggerFactory);

        var requestMethods = new List<HttpMethod>();

        mockHttpHandler.RequestHandler = (request) =>
        {
            requestMethods.Add(request.Method);

            if (request.Method == HttpMethod.Post)
            {
                // Streamable HTTP POST returns auth error
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = authStatusCode,
                    Content = new StringContent($"{{\"error\": \"{authStatusCode}\"}}")
                });
            }

            // SSE GET should never be reached
            throw new InvalidOperationException("Should not fall back to SSE on auth error");
        };

        // ConnectAsync for AutoDetect mode just creates the transport without sending
        // any HTTP request. The auto-detection is triggered lazily by the first
        // SendMessageAsync call, which happens inside McpClient.CreateAsync when it
        // sends the JSON-RPC "initialize" message.
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(authStatusCode, ex.StatusCode);

        // Verify only POST was sent — no GET fallback
        Assert.Single(requestMethods);
        Assert.Equal(HttpMethod.Post, requestMethods[0]);
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