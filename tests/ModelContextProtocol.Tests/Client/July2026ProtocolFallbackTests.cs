using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Regression tests for the fallback from the 2026-07-28 protocol revision to a legacy protocol in
/// <see cref="McpClient"/>. With default options (<c>ProtocolVersion = null</c>) the client prefers
/// 2026-07-28 but probes with <c>server/discover</c>, falls back to the legacy <c>initialize</c>
/// handshake when the server is legacy, and accepts whatever supported protocol version the legacy
/// server negotiates. Pinning <c>ProtocolVersion</c> to <c>2026-07-28</c> instead makes it the
/// minimum too, so the client refuses to fall back.
/// </summary>
/// <remarks>
/// The originally shipped logic in <c>PerformLegacyInitializeAsync</c> compared the server's response
/// against the requested version and threw when a legacy server downgraded to (say) <c>"2025-06-18"</c>,
/// even though the legacy negotiation succeeded. These tests guard against that regression.
/// </remarks>
public class July2026ProtocolFallbackTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task Client_OnMethodNotFound_FallsBackTo_Initialize_AcceptsDowngradedVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new LegacyServerTestTransport(serverNegotiatedVersion: "2025-06-18");

        // Default options (ProtocolVersion = null) prefer 2026-07-28 but allow automatic fallback.
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.True(transport.ServerDiscoverProbed);
        Assert.True(transport.LegacyInitializeReceived);
        Assert.Equal(McpHttpHeaders.November2025ProtocolVersion, transport.LegacyInitializeProtocolVersion);
        Assert.Equal("2025-06-18", client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task Client_OnInvalidParams_FallsBackTo_Initialize()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new LegacyServerTestTransport(
            serverNegotiatedVersion: "2025-11-25",
            probeErrorCode: (int)McpErrorCode.InvalidParams);

        // Default options (ProtocolVersion = null) prefer 2026-07-28 but allow automatic fallback.
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.True(transport.LegacyInitializeReceived);
        Assert.Equal(McpHttpHeaders.November2025ProtocolVersion, transport.LegacyInitializeProtocolVersion);
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task Client_OnLegacyFallback_RejectsModernInitializeResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new LegacyServerTestTransport(
            serverNegotiatedVersion: McpHttpHeaders.July2026ProtocolVersion);

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
                loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.IsType<McpException>(exception);
        Assert.True(transport.LegacyInitializeReceived);
        Assert.Equal(McpHttpHeaders.November2025ProtocolVersion, transport.LegacyInitializeProtocolVersion);
        Assert.Contains("mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_WithPinnedJuly2026Version_RefusesFallback_ToLegacyServer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new LegacyServerTestTransport(serverNegotiatedVersion: "2025-06-18");

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                // Pinning the version makes it the minimum too, so the client refuses to fall back.
                ProtocolVersion = McpHttpHeaders.July2026ProtocolVersion,
            }, loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.IsType<McpException>(exception);
        Assert.Contains("2026-07-28", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyClient_WithExplicitPin_StillRequires_ExactVersionMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        // Server responds with a DIFFERENT version than the one the user pinned.
        await using var transport = new LegacyServerTestTransport(serverNegotiatedVersion: "2025-03-26");

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ProtocolVersion = "2025-11-25",
            }, loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.IsType<McpException>(exception);
        Assert.Contains("mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_OnHeaderMismatch_Surfaces_NoFallback()
    {
        // The peer is modern (returns the spec-defined -32020 HeaderMismatch on the probe).
        // Falling back to legacy initialize would just produce another malformed envelope.
        // Verify the connect-time logic surfaces the error to the caller instead of falling back.
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new LegacyServerTestTransport(
            serverNegotiatedVersion: "2025-11-25",
            probeErrorCode: (int)McpErrorCode.HeaderMismatch);

        var exception = await Assert.ThrowsAnyAsync<McpException>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ProtocolVersion = McpHttpHeaders.July2026ProtocolVersion,
            }, loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.True(transport.ServerDiscoverProbed);
        Assert.False(transport.LegacyInitializeReceived);
        Assert.Equal(McpErrorCode.HeaderMismatch, ((McpProtocolException)exception).ErrorCode);
    }

    [Fact]
    public async Task Client_OnSilentProbe_FallsBackTo_Initialize_AfterConfiguredProbeTimeout()
    {
        // Simulate a legacy server that silently drops the unknown server/discover method (it never
        // responds to the probe). The client must fall back to legacy initialize once the configured
        // DiscoverProbeTimeout elapses, well before the much larger InitializationTimeout.
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new LegacyServerTestTransport(
            serverNegotiatedVersion: "2025-11-25",
            silentDiscoverProbe: true);

        var stopwatch = Stopwatch.StartNew();
        // Default options (ProtocolVersion = null) prefer 2026-07-28 but allow automatic fallback.
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            DiscoverProbeTimeout = TimeSpan.FromMilliseconds(250),
            InitializationTimeout = TestConstants.DefaultTimeout,
        }, loggerFactory: LoggerFactory, cancellationToken: ct);
        stopwatch.Stop();

        Assert.True(transport.ServerDiscoverProbed);
        Assert.True(transport.LegacyInitializeReceived);
        Assert.Equal(McpHttpHeaders.November2025ProtocolVersion, transport.LegacyInitializeProtocolVersion);
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        // The fallback was driven by the short probe timeout, not the 60s InitializationTimeout.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"Fallback should have happened shortly after the {nameof(McpClientOptions.DiscoverProbeTimeout)}, but took {stopwatch.Elapsed}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void DiscoverProbeTimeout_Setter_Rejects_NonPositiveValues(int milliseconds)
    {
        var options = new McpClientOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DiscoverProbeTimeout = TimeSpan.FromMilliseconds(milliseconds));
    }

    [Fact]
    public void DiscoverProbeTimeout_Setter_Accepts_PositiveAndInfiniteValues()
    {
        var options = new McpClientOptions();

        // Default is the documented 5 seconds.
        Assert.Equal(TimeSpan.FromSeconds(5), options.DiscoverProbeTimeout);

        options.DiscoverProbeTimeout = TimeSpan.FromSeconds(30);
        Assert.Equal(TimeSpan.FromSeconds(30), options.DiscoverProbeTimeout);

        // Timeout.InfiniteTimeSpan disables the separate probe timeout (bounded by InitializationTimeout only).
        options.DiscoverProbeTimeout = Timeout.InfiniteTimeSpan;
        Assert.Equal(Timeout.InfiniteTimeSpan, options.DiscoverProbeTimeout);
    }

    /// <summary>
    /// Minimal in-memory transport that simulates a legacy server: rejects
    /// <c>server/discover</c> (with a configurable JSON-RPC error code, or by
    /// silently dropping the request) and responds to <c>initialize</c> with a
    /// configurable protocol version.
    /// </summary>
    private sealed class LegacyServerTestTransport(
        string serverNegotiatedVersion,
        int probeErrorCode = (int)McpErrorCode.MethodNotFound,
        bool silentDiscoverProbe = false) : IClientTransport
    {
        private readonly Channel<JsonRpcMessage> _incomingToClient = Channel.CreateUnbounded<JsonRpcMessage>();

        public string Name => "legacy-server-test-transport";

        public bool ServerDiscoverProbed { get; private set; }

        public bool LegacyInitializeReceived { get; private set; }

        public string? LegacyInitializeProtocolVersion { get; private set; }

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ITransport transport = new TransportChannel(_incomingToClient, this);
            return Task.FromResult(transport);
        }

        public ValueTask DisposeAsync() => default;

        private void HandleOutgoingMessage(JsonRpcMessage message)
        {
            switch (message)
            {
                case JsonRpcRequest { Method: RequestMethods.ServerDiscover } discoverReq:
                    ServerDiscoverProbed = true;
                    if (silentDiscoverProbe)
                    {
                        // Model a legacy server that drops the unknown method without replying.
                        break;
                    }

                    _ = WriteAsync(new JsonRpcError
                    {
                        Id = discoverReq.Id,
                        Error = new JsonRpcErrorDetail
                        {
                            Code = probeErrorCode,
                            Message = probeErrorCode == (int)McpErrorCode.MethodNotFound
                                ? "Method not found"
                                : "Invalid params",
                        },
                    });
                    break;

                case JsonRpcRequest { Method: RequestMethods.Initialize } initReq:
                    LegacyInitializeReceived = true;
                    var initializeRequest = JsonSerializer.Deserialize<InitializeRequestParams>(initReq.Params, McpJsonUtilities.DefaultOptions);
                    LegacyInitializeProtocolVersion = initializeRequest?.ProtocolVersion;
                    _ = WriteAsync(new JsonRpcResponse
                    {
                        Id = initReq.Id,
                        Result = JsonSerializer.SerializeToNode(new InitializeResult
                        {
                            ProtocolVersion = serverNegotiatedVersion,
                            Capabilities = new ServerCapabilities(),
                            ServerInfo = new Implementation { Name = "legacy-test-server", Version = "1.0.0" },
                        }, McpJsonUtilities.DefaultOptions),
                    });
                    break;
            }
        }

        private Task WriteAsync(JsonRpcMessage message)
            => _incomingToClient.Writer.WriteAsync(message, CancellationToken.None).AsTask();

        private sealed class TransportChannel(
            Channel<JsonRpcMessage> incoming,
            LegacyServerTestTransport parent) : ITransport
        {
            public ChannelReader<JsonRpcMessage> MessageReader => incoming.Reader;
            public bool IsConnected { get; private set; } = true;
            public string? SessionId => null;

            public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
            {
                parent.HandleOutgoingMessage(message);
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                incoming.Writer.TryComplete();
                IsConnected = false;
                return default;
            }
        }
    }
}
