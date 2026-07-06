using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies the server establishes its negotiated protocol version exactly once per stateful session: the
/// initial <see langword="null"/>-to-value transition is allowed and re-sending the same version is an
/// idempotent no-op, but a request that switches to a different (even if otherwise supported) version is
/// rejected with <see cref="McpErrorCode.InvalidRequest"/>. The session is driven over a raw stream
/// transport (the stdio-shaped, stateful path) so the per-request <c>_meta</c> protocol version is fully
/// controlled - the SDK's own client normalizes it on every outgoing request, so only a misbehaving peer
/// can trigger a mid-session change.
/// </summary>
public sealed class NegotiatedProtocolVersionTests : LoggedTest, IAsyncDisposable
{
    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly ServiceProvider _services;
    private readonly Task _serverTask;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;

    public NegotiatedProtocolVersionTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<ILoggerProvider>(XunitLoggerProvider);
        serviceCollection
            .AddMcpServer()
            .WithStreamServerTransport(_clientToServer.Reader.AsStream(), _serverToClient.Writer.AsStream())
            .WithTools<EchoTools>();

        _services = serviceCollection.BuildServiceProvider(validateScopes: true);
        var server = _services.GetRequiredService<McpServer>();
        _serverTask = server.RunAsync(_cts.Token);

        _writer = new StreamWriter(_clientToServer.Writer.AsStream()) { AutoFlush = true };
        _reader = new StreamReader(_serverToClient.Reader.AsStream());
    }

    [Fact]
    public async Task PerRequestProtocolVersion_IsEstablishedOnce_AndRejectsLaterChange()
    {
        var ct = TestContext.Current.CancellationToken;

        // The first request establishes the 2026-07-28 version for the stateful session (null -> 2026-07-28).
        Assert.IsType<JsonRpcResponse>(await RoundTripAsync(id: 1, McpProtocolVersions.July2026ProtocolVersion, ct));

        // Re-sending the same version is an idempotent no-op, not an error.
        Assert.IsType<JsonRpcResponse>(await RoundTripAsync(id: 2, McpProtocolVersions.July2026ProtocolVersion, ct));

        // Switching to a different (still-supported) version mid-session is rejected.
        var error = Assert.IsType<JsonRpcError>(await RoundTripAsync(id: 3, McpProtocolVersions.November2025ProtocolVersion, ct));
        Assert.Equal((int)McpErrorCode.InvalidRequest, error.Error.Code);
        Assert.Contains("protocol version cannot change", error.Error.Message, StringComparison.OrdinalIgnoreCase);

        // The rejected request must not have mutated the negotiated version: the original 2026-07-28 version still works.
        Assert.IsType<JsonRpcResponse>(await RoundTripAsync(id: 4, McpProtocolVersions.July2026ProtocolVersion, ct));
    }

    [Fact]
    public async Task PerRequestMetadata_RejectsInitializeHandshakeVersionBeforeInitialize()
    {
        var ct = TestContext.Current.CancellationToken;

        var error = Assert.IsType<JsonRpcError>(await RoundTripAsync(id: 1, McpProtocolVersions.November2025ProtocolVersion, ct));
        Assert.Equal((int)McpErrorCode.UnsupportedProtocolVersion, error.Error.Code);
        Assert.Contains("initialize", error.Error.Message, StringComparison.OrdinalIgnoreCase);

        // The rejected initialize-handshake _meta request must not have established session state.
        Assert.IsType<JsonRpcResponse>(await RoundTripAsync(id: 2, McpProtocolVersions.July2026ProtocolVersion, ct));
    }

    [Fact]
    public async Task PerRequestMetadata_RejectsRequestMissingRequiredMetadata()
    {
        var ct = TestContext.Current.CancellationToken;

        var error = Assert.IsType<JsonRpcError>(
            await RoundTripAsync(
                id: 1,
                McpProtocolVersions.July2026ProtocolVersion,
                ct,
                includeClientInfo: false));

        Assert.Equal((int)McpErrorCode.InvalidParams, error.Error.Code);
        Assert.Contains(MetaKeys.ClientInfo, error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerDiscover_WithoutPerRequestMetadata_IsRejectedBeforeInitialize()
    {
        var ct = TestContext.Current.CancellationToken;

        var request = new JsonRpcRequest
        {
            Id = new RequestId(1),
            Method = RequestMethods.ServerDiscover,
            Params = new JsonObject(),
        };

        var error = Assert.IsType<JsonRpcError>(await SendAndReceiveAsync(request, ct));
        Assert.Equal((int)McpErrorCode.InvalidParams, error.Error.Code);
        Assert.Contains(RequestMethods.ServerDiscover, error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initialize_WithPerRequestMetadataProtocolVersion_IsRejected()
    {
        var ct = TestContext.Current.CancellationToken;

        var error = Assert.IsType<JsonRpcError>(
            await RoundTripInitializeAsync(id: 1, McpProtocolVersions.July2026ProtocolVersion, ct));

        Assert.Equal((int)McpErrorCode.UnsupportedProtocolVersion, error.Error.Code);
        Assert.Contains("initialize", error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initialize_WithReservedPerRequestMetadata_IsRejected()
    {
        var ct = TestContext.Current.CancellationToken;

        var request = new JsonRpcRequest
        {
            Id = new RequestId(1),
            Method = RequestMethods.Initialize,
            Params = JsonSerializer.SerializeToNode(new InitializeRequestParams
            {
                ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
                Capabilities = new ClientCapabilities(),
                ClientInfo = new Implementation { Name = "test-client", Version = "1.0.0" },
                Meta = new JsonObject
                {
                    [MetaKeys.ClientInfo] = new JsonObject
                    {
                        ["name"] = "per-request-meta-client",
                        ["version"] = "1.0.0",
                    },
                },
            }, McpJsonUtilities.DefaultOptions),
        };

        var error = Assert.IsType<JsonRpcError>(await SendAndReceiveAsync(request, ct));
        Assert.Equal((int)McpErrorCode.InvalidRequest, error.Error.Code);
        Assert.Contains(MetaKeys.ClientInfo, error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscriptionsListen_WithInitializeProtocolVersion_IsRejected()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.IsType<JsonRpcResponse>(
            await RoundTripInitializeAsync(id: 1, McpProtocolVersions.November2025ProtocolVersion, ct));

        var request = new JsonRpcRequest
        {
            Id = new RequestId(2),
            Method = RequestMethods.SubscriptionsListen,
            Params = new JsonObject(),
        };

        var error = Assert.IsType<JsonRpcError>(await SendAndReceiveAsync(request, ct));
        Assert.Equal((int)McpErrorCode.MethodNotFound, error.Error.Code);
        Assert.Contains(RequestMethods.SubscriptionsListen, error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoggingSetLevel_WithPerRequestMetadataProtocolVersion_IsRejected()
    {
        var ct = TestContext.Current.CancellationToken;

        var request = new JsonRpcRequest
        {
            Id = new RequestId(1),
            Method = RequestMethods.LoggingSetLevel,
            Params = new JsonObject
            {
                ["level"] = "info",
                ["_meta"] = PerRequestMetadata(),
            },
        };

        var error = Assert.IsType<JsonRpcError>(await SendAndReceiveAsync(request, ct));
        Assert.Equal((int)McpErrorCode.MethodNotFound, error.Error.Code);
        Assert.Contains(RequestMethods.LoggingSetLevel, error.Error.Message, StringComparison.Ordinal);
    }

    private async Task<JsonRpcMessage> RoundTripAsync(
        long id,
        string protocolVersion,
        CancellationToken cancellationToken,
        bool includeClientInfo = true,
        bool includeClientCapabilities = true)
    {
        // tools/list is available under both the initialize-handshake and 2026-07-28 revisions (unlike ping/initialize,
        // which the 2026-07-28 protocol removed), so it exercises the version guard rather than the
        // per-method availability gate.
        var meta = new JsonObject
        {
            [MetaKeys.ProtocolVersion] = protocolVersion,
        };

        if (McpProtocolVersions.RequiresPerRequestMetadata(protocolVersion))
        {
            if (includeClientInfo)
            {
                meta[MetaKeys.ClientInfo] = new JsonObject
                {
                    ["name"] = "test-client",
                    ["version"] = "1.0.0",
                };
            }

            if (includeClientCapabilities)
            {
                meta[MetaKeys.ClientCapabilities] = new JsonObject();
            }
        }

        var request = new JsonRpcRequest
        {
            Id = new RequestId(id),
            Method = RequestMethods.ToolsList,
            Params = new JsonObject
            {
                ["_meta"] = meta,
            },
        };

        return await SendAndReceiveAsync(request, cancellationToken);
    }

    private static JsonObject PerRequestMetadata() => new()
    {
        [MetaKeys.ProtocolVersion] = McpProtocolVersions.July2026ProtocolVersion,
        [MetaKeys.ClientInfo] = new JsonObject
        {
            ["name"] = "test-client",
            ["version"] = "1.0.0",
        },
        [MetaKeys.ClientCapabilities] = new JsonObject(),
    };

    private async Task<JsonRpcMessage> RoundTripInitializeAsync(long id, string protocolVersion, CancellationToken cancellationToken)
    {
        var request = new JsonRpcRequest
        {
            Id = new RequestId(id),
            Method = RequestMethods.Initialize,
            Params = JsonSerializer.SerializeToNode(new InitializeRequestParams
            {
                ProtocolVersion = protocolVersion,
                Capabilities = new ClientCapabilities(),
                ClientInfo = new Implementation { Name = "test-client", Version = "1.0.0" },
            }, McpJsonUtilities.DefaultOptions),
        };

        return await SendAndReceiveAsync(request, cancellationToken);
    }

    private async Task<JsonRpcMessage> SendAndReceiveAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(request, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)));
#if NET
        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
#else
        cancellationToken.ThrowIfCancellationRequested();
        await _writer.WriteLineAsync(json);
#endif

        while (true)
        {
#if NET
            string? line = await _reader.ReadLineAsync(cancellationToken)
                .AsTask()
                .WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
#else
            string? line = await _reader.ReadLineAsync()
                .WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
#endif

            if (line is null)
            {
                throw new InvalidOperationException("Server stream closed before responding.");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var message = (JsonRpcMessage)JsonSerializer.Deserialize(line, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)))!;

            // Ignore anything that isn't the response to the request we just sent (e.g. notifications).
            if (message is JsonRpcMessageWithId withId && withId.Id.Equals(request.Id))
            {
                return message;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _clientToServer.Writer.Complete();
        _serverToClient.Writer.Complete();

        try
        {
            await _serverTask;
        }
        catch (OperationCanceledException)
        {
        }

        await _services.DisposeAsync();
        _cts.Dispose();
        Dispose();
    }

    [McpServerToolType]
    private sealed class EchoTools
    {
        [McpServerTool, Description("Echoes the input back to the caller.")]
        public static string Echo([Description("The message to echo.")] string message) => message;
    }
}
