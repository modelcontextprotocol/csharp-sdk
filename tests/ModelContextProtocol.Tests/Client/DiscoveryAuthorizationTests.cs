using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

public class DiscoveryAuthorizationTests(ITestOutputHelper output) : LoggedTest(output)
{
    [Fact]
    public async Task Discover_AuthorizesOncePerClient()
    {
        using var server = new AuthorizationServer();
        await ConnectTwiceAsync(server);

        Assert.Equal(2, server.AuthorizationStates.Count);
        Assert.Equal(2, server.AuthorizationStates.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, server.AuthorizedDiscoverRequests);
        Assert.Equal(0, server.InitializeRequests);
    }

    [Theory]
    [InlineData(HttpTransportMode.AutoDetect)]
    [InlineData(HttpTransportMode.StreamableHttp)]
    public async Task Discover_MetadataHttpTimeout_TriesNextEndpoint(HttpTransportMode transportMode)
    {
        using var gate = new AuthorizationGate();
        using var server = new AuthorizationServer { Gate = gate, GatedStage = "metadata" };
        using var httpClient = new HttpClient(server) { Timeout = TimeSpan.FromMilliseconds(400) };
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        caller.CancelAfter(TimeSpan.FromSeconds(8));
        await using var transport = CreateTransport(server, httpClient, transportMode);
        var options = new McpClientOptions { InitializationTimeout = TimeSpan.FromSeconds(3) };

        await using var client = await McpClient.CreateAsync(transport, options, LoggerFactory, caller.Token);

        Assert.False(caller.IsCancellationRequested);
        Assert.Equal(1, gate.Cancellations);
        Assert.Equal(2, server.MetadataRequests);
        Assert.Single(server.AuthorizationStates);
        Assert.Equal(1, server.AuthorizedDiscoverRequests);
        Assert.Equal(0, server.InitializeRequests);
    }

    [Theory]
    [InlineData("large", false)]
    [InlineData("maximum", false)]
    [InlineData("infinite", false)]
    [InlineData("large", true)]
    [InlineData("maximum", true)]
    [InlineData("infinite", true)]
    public async Task Discover_AcceptsFullProbeTimeoutRange(string probeKind, bool infiniteInitialization)
    {
        using var server = new AuthorizationServer();
        using var httpClient = new HttpClient(server);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        caller.CancelAfter(TestConstants.DefaultTimeout);
        await using var transport = CreateTransport(server, httpClient);
        var options = new McpClientOptions
        {
            DiscoverProbeTimeout = GetProbeTimeout(probeKind),
            InitializationTimeout = infiniteInitialization ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(3),
        };

        await using var client = await McpClient.CreateAsync(transport, options, LoggerFactory, caller.Token);

        Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);
        Assert.Equal(1, server.AuthorizedDiscoverRequests);
        Assert.Equal(0, server.InitializeRequests);
    }

    [Theory]
    [InlineData("large")]
    [InlineData("maximum")]
    [InlineData("infinite")]
    public async Task Discover_LargeOrInfiniteProbe_StillHonorsInitializationTimeout(string probeKind)
    {
        using var server = new AuthorizationServer { SilentBeforeAuthorization = true };
        using var httpClient = new HttpClient(server);
        await using var transport = CreateTransport(server, httpClient);
        var options = new McpClientOptions
        {
            DiscoverProbeTimeout = GetProbeTimeout(probeKind),
            InitializationTimeout = TimeSpan.FromMilliseconds(250),
        };

        await Assert.ThrowsAsync<TimeoutException>(() => McpClient.CreateAsync(
            transport, options, LoggerFactory, TestContext.Current.CancellationToken));

        Assert.Equal(1, server.SilentProbeCancellations);
        Assert.Equal(0, server.InitializeRequests);
    }

    private TimeSpan GetProbeTimeout(string probeKind) => probeKind switch
    {
        "large" => TimeSpan.FromDays(60),
        "maximum" => TimeSpan.MaxValue,
        "infinite" => Timeout.InfiniteTimeSpan,
        _ => throw new ArgumentOutOfRangeException(nameof(probeKind)),
    };

    [Theory]
    [InlineData("callback")]
    [InlineData("metadata")]
    public async Task Discover_AuthorizationOutlivesProbe_WithoutRestartingAuthorization(string stage)
    {
        using var gate = new AuthorizationGate();
        using var server = new AuthorizationServer { Gate = gate, GatedStage = stage };
        var connection = ConnectTwiceAsync(server);
        try
        {
            await WaitForAuthorizationAsync(gate, connection);
            // Keep the first authorization pending for longer than the configured probe.
            // This timer exercises the deadline; the gate controls when authorization can complete.
            await gate.HoldPastProbeAsync(new McpClientOptions().DiscoverProbeTimeout);
        }
        finally
        {
            gate.Release.TrySetResult(true);
        }
        await connection;

        Assert.Equal(0, gate.Cancellations);
        Assert.Equal(2, server.AuthorizationStates.Count);
        Assert.Equal(2, server.AuthorizationStates.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, server.AuthorizedDiscoverRequests);
        Assert.Equal(0, server.InitializeRequests);
    }

    [Theory]
    [InlineData("callback")]
    [InlineData("metadata")]
    public async Task Discover_CallerCancellationDuringAuthorization_Propagates(string stage)
    {
        using var gate = new AuthorizationGate();
        using var server = new AuthorizationServer { Gate = gate, GatedStage = stage };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient(server);
        await using var transport = CreateTransport(server, httpClient);

        var connection = McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: cancellation.Token);
        await WaitForAuthorizationAsync(gate, connection);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection);
        Assert.Equal(1, gate.Cancellations);
        Assert.Equal(1, server.MetadataRequests);
        Assert.Equal(0, server.InitializeRequests);
    }

    [Theory]
    [InlineData("callback")]
    [InlineData("metadata")]
    public async Task Discover_InitializationTimeoutDuringAuthorization_Propagates(string stage)
    {
        using var gate = new AuthorizationGate();
        using var server = new AuthorizationServer { Gate = gate, GatedStage = stage };
        using var httpClient = new HttpClient(server);
        await using var transport = CreateTransport(server, httpClient);
        var options = new McpClientOptions { InitializationTimeout = TimeSpan.FromSeconds(1) };

        await Assert.ThrowsAsync<TimeoutException>(() => McpClient.CreateAsync(
            transport, options, LoggerFactory, TestContext.Current.CancellationToken));

        Assert.True(gate.Entered.Task.IsCompleted);
        Assert.Equal(1, gate.Cancellations);
        Assert.Equal(1, server.MetadataRequests);
        Assert.Equal(0, server.InitializeRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discover_SilentServer_StillFallsBackWithinProbeBudget(bool afterAuthorization)
    {
        using var server = new AuthorizationServer { SilentBeforeAuthorization = !afterAuthorization, SilentAfterAuthorization = afterAuthorization };
        using var httpClient = new HttpClient(server);
        await using var transport = CreateTransport(server, httpClient);
        var options = new McpClientOptions { DiscoverProbeTimeout = TimeSpan.FromMilliseconds(250) };

        await using var client = await McpClient.CreateAsync(
            transport, options, LoggerFactory, TestContext.Current.CancellationToken);

        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);
        Assert.Equal(1, server.InitializeRequests);
        Assert.Single(server.AuthorizationStates);
        Assert.Equal(1, server.SilentProbeCancellations);
    }

    [Theory]
    [InlineData("callback")]
    [InlineData("metadata")]
    public async Task Discover_SseAuthorizationOutlivesProbe_WithoutRestartingAuthorization(string stage)
    {
        using var gate = new AuthorizationGate();
        using var server = new AuthorizationServer { Gate = gate, GatedStage = stage, UseLegacySse = true };
        var connection = ConnectTwiceAsync(server);
        try
        {
            await WaitForAuthorizationAsync(gate, connection);
            await gate.HoldPastProbeAsync(new McpClientOptions().DiscoverProbeTimeout);
        }
        finally
        {
            gate.Release.TrySetResult(true);
        }
        await connection;

        Assert.Equal(0, gate.Cancellations);
        Assert.Equal(2, server.AuthorizationStates.Count);
        Assert.Equal(2, server.AuthorizationStates.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, server.AuthorizedDiscoverRequests);
        Assert.Equal(2, server.InitializeRequests);
    }

    private async Task WaitForAuthorizationAsync(AuthorizationGate gate, Task connection)
    {
        await Task.WhenAny(gate.Entered.Task, connection).WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        if (connection.IsCompleted)
        {
            await connection;
        }
        Assert.True(gate.Entered.Task.IsCompleted, "The connection completed without entering authorization.");
    }

    private async Task ConnectTwiceAsync(AuthorizationServer server)
    {
        using var httpClient = new HttpClient(server) { Timeout = TestConstants.DefaultTimeout };
        for (int connection = 0; connection < 2; connection++)
        {
            await using var transport = CreateTransport(server, httpClient);
            await using var client = await McpClient.CreateAsync(
                transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private HttpClientTransport CreateTransport(
        AuthorizationServer server, HttpClient httpClient, HttpTransportMode transportMode = HttpTransportMode.AutoDetect)
    {
        return new HttpClientTransport(new()
        {
            Endpoint = new Uri("http://localhost/mcp"),
            TransportMode = transportMode,
            OAuth = new()
            {
                ClientId = "synthetic-client",
                RedirectUri = new Uri("http://localhost/callback"),
                AuthorizationCallbackHandler = server.AuthorizeAsync,
            },
        }, httpClient, LoggerFactory);
    }

    private sealed class AuthorizationGate : IDisposable
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Cancellations { get; private set; }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (!Entered.TrySetResult(true))
            {
                return;
            }

            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancellations++;
                throw;
            }
        }

        public async Task HoldPastProbeAsync(TimeSpan probeTimeout)
        {
            var elapsed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timer = new Timer(_ => elapsed.TrySetResult(true), null,
                probeTimeout + TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
            await elapsed.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        }

        public void Dispose() => Release.TrySetResult(true);
    }

    private sealed class AuthorizationServer : HttpMessageHandler
    {
        private readonly List<Pipe> _ssePipes = [];
        private Pipe? _ssePipe;

        public AuthorizationGate? Gate { get; init; }
        public string? GatedStage { get; init; }
        public bool UseLegacySse { get; init; }
        public bool SilentBeforeAuthorization { get; init; }
        public bool SilentAfterAuthorization { get; init; }
        public List<string> AuthorizationStates { get; } = [];
        public int MetadataRequests { get; private set; }
        public int AuthorizedDiscoverRequests { get; private set; }
        public int InitializeRequests { get; private set; }
        public int SilentProbeCancellations { get; private set; }

        public async Task<AuthorizationResult?> AuthorizeAsync(AuthorizationCallbackContext context, CancellationToken cancellationToken)
        {
            var stateParameter = context.AuthorizationUri.Query.TrimStart('?').Split('&')
                .Single(parameter => parameter.StartsWith("state=", StringComparison.Ordinal));
            var state = Uri.UnescapeDataString(stateParameter.Substring("state=".Length));
            AuthorizationStates.Add(state);
            if (GatedStage == "callback")
            {
                await Assert.IsType<AuthorizationGate>(Gate).WaitAsync(cancellationToken);
            }
            return new AuthorizationResult { Code = "synthetic-code", State = state };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = Assert.IsType<Uri>(request.RequestUri);
            Assert.True(uri.IsLoopback);
            switch (uri.AbsolutePath)
            {
                case "/.well-known/oauth-protected-resource":
                    return JsonResponse("""{"resource":"http://localhost/mcp","authorization_servers":["http://localhost"]}""");

                case "/.well-known/oauth-authorization-server":
                case "/.well-known/openid-configuration":
                    MetadataRequests++;
                    if (GatedStage == "metadata")
                    {
                        await Assert.IsType<AuthorizationGate>(Gate).WaitAsync(cancellationToken);
                    }
                    return JsonResponse("""
                        {"issuer":"http://localhost","authorization_endpoint":"http://localhost/authorize",
                         "token_endpoint":"http://localhost/token","code_challenge_methods_supported":["S256"]}
                        """);

                case "/token":
                    return JsonResponse("""{"access_token":"synthetic-token","token_type":"Bearer","expires_in":300}""");

                case "/mcp":
                case "/messages":
                    if (UseLegacySse &&
                        uri.AbsolutePath == "/mcp" &&
                        request.Method == HttpMethod.Post)
                    {
                        return EmptyResponse(HttpStatusCode.MethodNotAllowed);
                    }

                    if (UseLegacySse &&
                        request.Method == HttpMethod.Get &&
                        request.Headers.Authorization is not null)
                    {
                        _ssePipe = new Pipe();
                        _ssePipes.Add(_ssePipe);
                        await _ssePipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("event: endpoint\ndata: /messages\n\n"), cancellationToken);
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StreamContent(_ssePipe.Reader.AsStream())
                            {
                                Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") },
                            },
                        };
                    }

                    JsonRpcMessage? message = null;
                    if (request.Content is { } content)
                    {
                        var body = await content.ReadAsStringAsync(cancellationToken);
                        message = JsonSerializer.Deserialize<JsonRpcMessage>(body, McpJsonUtilities.DefaultOptions);
                    }

                    if (SilentBeforeAuthorization &&
                        message is JsonRpcRequest { Method: RequestMethods.ServerDiscover })
                    {
                        await WaitForProbeCancellationAsync(cancellationToken);
                    }

                    if (request.Headers.Authorization is null)
                    {
                        var challenge = EmptyResponse(HttpStatusCode.Unauthorized);
                        challenge.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", "resource_metadata=\"http://localhost/.well-known/oauth-protected-resource\""));
                        return challenge;
                    }

                    if (message is JsonRpcNotification)
                    {
                        return EmptyResponse(HttpStatusCode.Accepted);
                    }
                    var rpc = Assert.IsType<JsonRpcRequest>(message);
                    switch (rpc.Method)
                    {
                        case RequestMethods.ServerDiscover:
                            AuthorizedDiscoverRequests++;
                            if (SilentAfterAuthorization)
                            {
                                await WaitForProbeCancellationAsync(cancellationToken);
                            }
                            if (UseLegacySse)
                            {
                                return await ReplyAsync(new JsonRpcError
                                {
                                    Id = rpc.Id,
                                    Error = new() { Code = -32601, Message = "Method not found" },
                                }, cancellationToken);
                            }
                            return await ReplyAsync(RpcResponse(rpc, new DiscoverResult
                            {
                                SupportedVersions = ["2026-07-28"],
                                Capabilities = new(),
                            }), cancellationToken);

                        case RequestMethods.Initialize:
                            InitializeRequests++;
                            return await ReplyAsync(RpcResponse(rpc, new InitializeResult
                            {
                                ProtocolVersion = "2025-11-25",
                                ServerInfo = new() { Name = "synthetic-server", Version = "1.0" },
                                Capabilities = new(),
                            }), cancellationToken);
                    }
                    throw new InvalidOperationException($"Unexpected RPC: {rpc.Method}");

                default:
                    throw new InvalidOperationException($"Unexpected synthetic request: {request.Method} {uri}");
            }
        }

        private async Task WaitForProbeCancellationAsync(CancellationToken cancellationToken)
        {
            var noResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await noResponse.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                SilentProbeCancellations++;
                throw;
            }
            throw new InvalidOperationException("A silent probe must be cancelled, not completed.");
        }

        private JsonRpcResponse RpcResponse<TResult>(JsonRpcRequest request, TResult result)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToNode(result, McpJsonUtilities.DefaultOptions),
            };
        }

        private async Task<HttpResponseMessage> ReplyAsync(JsonRpcMessage response, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(response, McpJsonUtilities.DefaultOptions);
            if (UseLegacySse)
            {
                var pipe = Assert.IsType<Pipe>(_ssePipe);
                await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes($"event: message\ndata: {json}\n\n"), cancellationToken);
                return EmptyResponse(HttpStatusCode.Accepted);
            }
            return JsonResponse(json);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var pipe in _ssePipes)
                {
                    pipe.Writer.Complete();
                }
                _ssePipes.Clear();
            }
            base.Dispose(disposing);
        }

        private HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        private HttpResponseMessage EmptyResponse(HttpStatusCode status) => new(status)
        {
            Content = new StringContent(string.Empty),
        };
    }
}
