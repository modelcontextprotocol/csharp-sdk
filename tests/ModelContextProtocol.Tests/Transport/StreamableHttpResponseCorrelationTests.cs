using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Transport;

/// <summary>
/// Regression tests for how <see cref="StreamableHttpClientSessionTransport"/> correlates an HTTP POST response
/// with the request that produced it, and for the diagnostics it reports when no reply can be correlated.
/// </summary>
/// <remarks>
/// <para>
/// The Streamable HTTP specification allows a server to answer a JSON-RPC request with either
/// <c>text/event-stream</c> or <c>application/json</c>, and clients MUST support both. Existing coverage of the
/// <c>application/json</c> case uses an in-memory <see cref="StringContent"/> response, which is already buffered and
/// carries a <c>Content-Length</c>. These tests exercise the production path instead: a real loopback socket that
/// sends <c>Transfer-Encoding: chunked</c> with no <c>Content-Length</c>, consumed through the transport's normal
/// <c>ResponseHeadersRead</c> flow.
/// </para>
/// <para>
/// The remaining tests pin the no-reply failure message, which previously only reported the request ID even though
/// the peer had clearly answered — with a different ID, an empty body, or something that wasn't a JSON-RPC reply.
/// See https://github.com/modelcontextprotocol/csharp-sdk/issues/1862.
/// </para>
/// </remarks>
public class StreamableHttpResponseCorrelationTests : LoggedTest
{
    public StreamableHttpResponseCorrelationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task ChunkedJsonResponse_WithCorrelatedError_FallsBackToInitialize()
    {
        var ct = TestContext.Current.CancellationToken;
        var receivedMethods = new List<string>();

        await using var server = new LoopbackHttpServer
        {
            Handler = (_, body) =>
            {
                string? method = null;
                string? id = null;
                try
                {
                    if (JsonNode.Parse(body) is JsonObject request)
                    {
                        method = request["method"]?.GetValue<string>();
                        id = request["id"]?.ToJsonString();
                    }
                }
                catch (Exception)
                {
                    // Leave method/id null; the assertions below will report the unexpected request.
                }

                lock (receivedMethods)
                {
                    receivedMethods.Add(method ?? "<unparsed>");
                }

                switch (method)
                {
                    case RequestMethods.ServerDiscover:
                        // HTTP 200, application/json with a charset parameter, Transfer-Encoding: chunked, and no
                        // Content-Length. The peer is an initialize-handshake server that rejects the
                        // server/discover probe with a correlated JSON-RPC error.
                        return LoopbackHttpServer.ChunkedJsonResponse(new JsonObject
                        {
                            ["jsonrpc"] = "2.0",
                            ["id"] = JsonNode.Parse(id!),
                            ["error"] = new JsonObject
                            {
                                ["code"] = -32600,
                                ["message"] = "Invalid Request",
                            },
                        }.ToJsonString(), chunkSize: 16);

                    case RequestMethods.Initialize:
                        return LoopbackHttpServer.ChunkedJsonResponse(new JsonObject
                        {
                            ["jsonrpc"] = "2.0",
                            ["id"] = JsonNode.Parse(id!),
                            ["result"] = new JsonObject
                            {
                                ["protocolVersion"] = McpProtocolVersions.June2025ProtocolVersion,
                                ["capabilities"] = new JsonObject(),
                                ["serverInfo"] = new JsonObject
                                {
                                    ["name"] = "ChunkedLoopback",
                                    ["version"] = "1.0.0",
                                },
                            },
                        }.ToJsonString(), chunkSize: 16);

                    default:
                        // Notifications (e.g. notifications/initialized) have no id and expect no reply.
                        return LoopbackHttpServer.EmptyResponse(HttpStatusCode.Accepted);
                }
            },
        };

        using var httpClient = new HttpClient();
        await using var transport = new HttpClientTransport(CreateOptions(server.Endpoint), httpClient, LoggerFactory);

        // Default options (ProtocolVersion = null) probe with server/discover and are expected to fall back to the
        // initialize handshake when that probe is rejected.
        await using var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions(),
            loggerFactory: LoggerFactory,
            cancellationToken: ct).WaitAsync(TestConstants.DefaultTimeout, ct);

        Assert.Equal(McpProtocolVersions.June2025ProtocolVersion, client.NegotiatedProtocolVersion);

        lock (receivedMethods)
        {
            Assert.Contains(RequestMethods.ServerDiscover, receivedMethods);
            Assert.Contains(RequestMethods.Initialize, receivedMethods);
        }
    }

    [Fact]
    public async Task ChunkedJsonResponse_WithCorrelatedError_IsDeliveredToTheSession()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var server = new LoopbackHttpServer
        {
            Handler = (_, _) => LoopbackHttpServer.ChunkedJsonResponse(
                """{"jsonrpc":"2.0","id":1,"error":{"code":-32600,"message":"Invalid Request"}}"""),
        };

        using var httpClient = new HttpClient();
        await using var transport = new HttpClientTransport(CreateOptions(server.Endpoint), httpClient, LoggerFactory);
        await using var session = await transport.ConnectAsync(ct);

        // The correlated error must be surfaced as a message rather than turn into a no-reply failure.
        await session.SendMessageAsync(
            new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.ServerDiscover, Params = new JsonObject() },
            ct).WaitAsync(TestConstants.DefaultTimeout, ct);

        Assert.True(session.MessageReader.TryRead(out var received));
        var error = Assert.IsType<JsonRpcError>(received);
        Assert.Equal(-32600, error.Error.Code);
        Assert.Equal(1, Assert.IsType<long>(error.Id.Id));
    }

    [Fact]
    public async Task NoReply_ResponseWithDifferentId_ExceptionNamesTheObservedId()
    {
        var ct = TestContext.Current.CancellationToken;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(CreateOptions(new Uri("http://localhost:8080")), httpClient, LoggerFactory);
        await using var session = await transport.ConnectAsync(ct);

        mockHttpHandler.RequestHandler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":42,"result":{}}""", Encoding.UTF8, "application/json"),
        });

        var exception = await Assert.ThrowsAsync<McpException>(() => session.SendMessageAsync(
            new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.ToolsList },
            ct));

        Assert.Contains("with ID: 1", exception.Message);
        Assert.Contains("The response Content-Type was 'application/json'", exception.Message);
        Assert.Contains("ID '42'", exception.Message);
    }

    [Fact]
    public async Task NoReply_NullIdErrorResponse_ExceptionReportsTheNullId()
    {
        var ct = TestContext.Current.CancellationToken;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(CreateOptions(new Uri("http://localhost:8080")), httpClient, LoggerFactory);
        await using var session = await transport.ConnectAsync(ct);

        // JSON-RPC 2.0 permits an error response with a null ID when the peer could not determine the request ID
        // (parse error / invalid request). Such a response can never be correlated with a request.
        mockHttpHandler.RequestHandler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"Parse error"}}""",
                Encoding.UTF8,
                "application/json"),
        });

        var exception = await Assert.ThrowsAsync<McpException>(() => session.SendMessageAsync(
            new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.ToolsList },
            ct));

        Assert.Contains("with ID: 1", exception.Message);
        Assert.Contains("with a null ID", exception.Message);
    }

    [Fact]
    public async Task NoReply_EmptyJsonBody_ExceptionReportsTheEmptyBody()
    {
        var ct = TestContext.Current.CancellationToken;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(CreateOptions(new Uri("http://localhost:8080")), httpClient, LoggerFactory);
        await using var session = await transport.ConnectAsync(ct);

        mockHttpHandler.RequestHandler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("", Encoding.UTF8, "application/json"),
        });

        var exception = await Assert.ThrowsAsync<McpException>(() => session.SendMessageAsync(
            new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.ToolsList },
            ct));

        Assert.Contains("with ID: 1", exception.Message);
        Assert.Contains("the response body was empty", exception.Message);
    }

    [Fact]
    public async Task NoReply_NonJsonContentType_ExceptionReportsTheMediaType()
    {
        var ct = TestContext.Current.CancellationToken;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        await using var transport = new HttpClientTransport(CreateOptions(new Uri("http://localhost:8080")), httpClient, LoggerFactory);
        await using var session = await transport.ConnectAsync(ct);

        mockHttpHandler.RequestHandler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not an MCP response", Encoding.UTF8, "text/plain"),
        });

        var exception = await Assert.ThrowsAsync<McpException>(() => session.SendMessageAsync(
            new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.ToolsList },
            ct));

        Assert.Contains("The response Content-Type was 'text/plain'", exception.Message);
        Assert.Contains("no JSON-RPC response or error was present", exception.Message);
    }

    private static HttpClientTransportOptions CreateOptions(Uri endpoint) => new()
    {
        Endpoint = endpoint,
        TransportMode = HttpTransportMode.StreamableHttp,
        // The transport opens a standalone GET SSE stream once it adopts the session; these tests only exercise POST.
        EnableStandaloneGetStream = false,
    };

    /// <summary>
    /// A minimal HTTP/1.1 loopback server that writes raw responses, so tests can control the framing
    /// (notably <c>Transfer-Encoding: chunked</c> with no <c>Content-Length</c>) that a mocked
    /// <see cref="HttpMessageHandler"/> cannot produce.
    /// </summary>
    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public LoopbackHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        /// <summary>Receives the request method, the decoded request body, and returns the raw response to write.</summary>
        public Func<string, string, string>? Handler { get; set; }

        public Uri Endpoint => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

        /// <summary>Builds an HTTP 200 <c>application/json</c> response sent with chunked transfer encoding.</summary>
        public static string ChunkedJsonResponse(string body, int chunkSize = 12)
        {
            var response = new StringBuilder();
            response.Append("HTTP/1.1 200 OK\r\n");
            response.Append("Content-Type: application/json; charset=utf-8\r\n");
            response.Append("Transfer-Encoding: chunked\r\n");
            response.Append("Connection: close\r\n");
            response.Append("\r\n");

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            for (int offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                int length = Math.Min(chunkSize, bytes.Length - offset);
                response.Append(length.ToString("x")).Append("\r\n");
                response.Append(Encoding.UTF8.GetString(bytes, offset, length)).Append("\r\n");
            }

            response.Append("0\r\n\r\n");
            return response.ToString();
        }

        /// <summary>Builds a response with no body, such as the <c>202 Accepted</c> expected for a notification.</summary>
        public static string EmptyResponse(HttpStatusCode statusCode) =>
            $"HTTP/1.1 {(int)statusCode} {statusCode}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

        private async Task AcceptLoopAsync()
        {
            var connections = new List<Task>();
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    connections.Add(Task.Run(() => HandleConnectionAsync(client)));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task HandleConnectionAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    string headerText = await ReadHeadersAsync(stream);

                    string[] headerLines = headerText.Split("\r\n");
                    string method = headerLines[0].Split(' ')[0];

                    int contentLength = 0;
                    bool isChunked = false;
                    foreach (string line in headerLines)
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            contentLength = int.Parse(line["Content-Length:".Length..].Trim());
                        }
                        else if (line.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase) &&
                                 line.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                        {
                            isChunked = true;
                        }
                    }

                    // The SDK sends JsonContent, whose length is unknown, so request bodies arrive chunked as well.
                    string body = isChunked
                        ? await ReadChunkedBodyAsync(stream)
                        : await ReadFixedLengthBodyAsync(stream, contentLength);

                    string response = Handler?.Invoke(method, body)
                        ?? EmptyResponse(HttpStatusCode.NotFound);

                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                }
                catch (IOException)
                {
                    // The client closed the connection (e.g. after disposing the transport); nothing to do.
                }
            }
        }

        private async Task<string> ReadHeadersAsync(NetworkStream stream)
        {
            var bytes = new List<byte>();
            var single = new byte[1];
            while (true)
            {
                if (await stream.ReadAsync(single, _cts.Token) == 0)
                {
                    break;
                }

                bytes.Add(single[0]);
                int count = bytes.Count;
                if (count >= 4 &&
                    bytes[count - 4] == '\r' && bytes[count - 3] == '\n' &&
                    bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private async Task<string> ReadFixedLengthBodyAsync(NetworkStream stream, int contentLength)
        {
            if (contentLength == 0)
            {
                return "";
            }

            var buffer = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int chunk = await stream.ReadAsync(buffer.AsMemory(read), _cts.Token);
                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            return Encoding.UTF8.GetString(buffer, 0, read);
        }

        private async Task<string> ReadChunkedBodyAsync(NetworkStream stream)
        {
            var body = new StringBuilder();
            while (true)
            {
                int size = Convert.ToInt32((await ReadLineAsync(stream)).Trim(), 16);
                if (size == 0)
                {
                    await ReadLineAsync(stream); // Trailer-terminating CRLF.
                    break;
                }

                var chunk = new byte[size];
                int read = 0;
                while (read < size)
                {
                    int count = await stream.ReadAsync(chunk.AsMemory(read), _cts.Token);
                    if (count == 0)
                    {
                        break;
                    }

                    read += count;
                }

                body.Append(Encoding.UTF8.GetString(chunk, 0, read));
                await ReadLineAsync(stream); // CRLF following the chunk data.
            }

            return body.ToString();
        }

        private async Task<string> ReadLineAsync(NetworkStream stream)
        {
            var line = new StringBuilder();
            var single = new byte[1];
            while (true)
            {
                if (await stream.ReadAsync(single, _cts.Token) == 0)
                {
                    break;
                }

                char c = (char)single[0];
                if (c == '\n')
                {
                    break;
                }

                if (c != '\r')
                {
                    line.Append(c);
                }
            }

            return line.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();

            try
            {
                await _acceptLoop;
            }
            catch (Exception)
            {
            }

            _cts.Dispose();
        }
    }
}
