using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using Microsoft.Extensions.Logging;
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

    // Regression test for https://github.com/modelcontextprotocol/csharp-sdk/issues/1526
    // When Streamable HTTP returns 415 (e.g. wrong Content-Type) and the SSE fallback also fails
    // (e.g. a Streamable-HTTP-only server returns 405 to the GET), the surfaced exception must
    // preserve the original Streamable HTTP error rather than dropping it on the floor.
    [Fact]
    public async Task AutoDetectMode_PreservesOriginalError_WhenStreamableHttpReturns415AndSseFallbackFails()
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

        const string streamableHttpBody = "Content-Type must be 'application/json'";

        mockHttpHandler.RequestHandler = (request) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                // Streamable HTTP fails with 415 - this is the real server diagnostic the user needs to see.
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.UnsupportedMediaType,
                    Content = new StringContent(streamableHttpBody),
                });
            }

            if (request.Method == HttpMethod.Get)
            {
                // Streamable-HTTP-only server: SSE GET is rejected with 405. Without the fix this is the
                // ONLY error the user ever sees, masking the real 415 diagnostic above.
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.MethodNotAllowed,
                    Content = new StringContent("Method not allowed"),
                });
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method}");
        };

        // ConnectAsync only constructs the AutoDetect transport; the probe runs on the first SendMessageAsync.
        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            session.SendMessageAsync(
                new JsonRpcRequest { Method = RequestMethods.Initialize, Id = new RequestId(1) },
                TestContext.Current.CancellationToken));

        // Walk the exception chain and assert the original 415 (and its body) is somewhere in it.
        // We don't pin the exact exception type so this stays robust to future error-shape tweaks,
        // but the underlying status code and server body must reach the caller.
        var combined = Flatten(ex);
        Assert.Contains("415", combined);
        Assert.Contains(streamableHttpBody, combined);

        static string Flatten(Exception e)
        {
            var sb = new System.Text.StringBuilder();
            void Walk(Exception? cur)
            {
                while (cur is not null)
                {
                    sb.Append(cur.GetType().FullName).Append(": ").AppendLine(cur.Message);
                    if (cur is AggregateException agg)
                    {
                        foreach (var inner in agg.InnerExceptions)
                        {
                            Walk(inner);
                        }
                        return;
                    }
                    cur = cur.InnerException;
                }
            }
            Walk(e);
            return sb.ToString();
        }
    }

    // When Streamable HTTP fails (non-JSON-RPC) and the SSE fallback also fails, the surfaced exception must remain
    // an HttpRequestException carrying the original Streamable HTTP status/body, with the SSE failure as its inner.
    [Fact]
    public async Task AutoDetectMode_SurfacesStreamableHttpError_WithSseAsInner_WhenSseFallbackFails()
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

        const string streamableHttpBody = "Content-Type must be 'application/json'";

        mockHttpHandler.RequestHandler = (request) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.UnsupportedMediaType,
                    Content = new StringContent(streamableHttpBody),
                });
            }

            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.MethodNotAllowed,
                    Content = new StringContent("Method not allowed"),
                });
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method}");
        };

        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            session.SendMessageAsync(
                new JsonRpcRequest { Method = RequestMethods.Initialize, Id = new RequestId(1) },
                TestContext.Current.CancellationToken));

        // The surfaced exception is the original Streamable HTTP error (the real server diagnostic), not the SSE 405.
        var httpEx = Assert.IsType<HttpRequestException>(ex);
        Assert.Contains("415", httpEx.Message);
        Assert.Contains(streamableHttpBody, httpEx.Message);
#if NET
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, httpEx.StatusCode);
#endif

        // The SSE fallback failure (the 405 from the GET) is preserved as the inner exception, not dropped.
        Assert.NotNull(httpEx.InnerException);
        Assert.Contains("405", httpEx.InnerException.ToString());
    }

    // Cancellation during the AutoDetect probe must surface as an OperationCanceledException, not be masked or
    // wrapped in the dual-failure AggregateException.
    [Fact]
    public async Task AutoDetectMode_SurfacesCancellation_WithoutWrapping()
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

        mockHttpHandler.RequestHandler = (request) =>
            Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.UnsupportedMediaType,
                Content = new StringContent("Content-Type must be 'application/json'"),
            });

        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            session.SendMessageAsync(
                new JsonRpcRequest { Method = RequestMethods.Initialize, Id = new RequestId(1) },
                cts.Token));

        Assert.IsNotType<AggregateException>(ex);
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    // The dual-failure case must be visible in logs at Warning level, even when callers swallow the exception.
    [Fact]
    public async Task AutoDetectMode_LogsWarning_WhenSseFallbackFailsAfterStreamableHttp()
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

        mockHttpHandler.RequestHandler = (request) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.UnsupportedMediaType,
                    Content = new StringContent("Content-Type must be 'application/json'"),
                });
            }

            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.MethodNotAllowed,
                Content = new StringContent("Method not allowed"),
            });
        };

        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            session.SendMessageAsync(
                new JsonRpcRequest { Method = RequestMethods.Initialize, Id = new RequestId(1) },
                TestContext.Current.CancellationToken));

        Assert.Contains(
            MockLoggerProvider.LogMessages,
            m => m.LogLevel == LogLevel.Warning && m.Message.Contains("SSE fallback failed"));
    }
}
