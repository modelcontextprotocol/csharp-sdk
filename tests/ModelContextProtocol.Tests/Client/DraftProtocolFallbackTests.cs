using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Regression tests for the draft-protocol-to-legacy fallback path in
/// <see cref="McpClient"/>. These verify that a client configured with
/// <c>McpClientOptions.ProtocolVersion = McpSession.DraftProtocolVersion</c>
/// correctly probes for a draft-aware server with <c>server/discover</c>, falls
/// back to the legacy <c>initialize</c> handshake when the server is legacy,
/// and accepts whatever supported protocol version the legacy server
/// negotiates - including a version different from the one the client
/// originally requested.
/// </summary>
/// <remarks>
/// The originally shipped logic in <c>PerformLegacyInitializeAsync</c> compared
/// the server's response against <c>_options.ProtocolVersion</c>, which under
/// draft is <c>"2026-07-28"</c>. When the legacy server downgraded to (say)
/// <c>"2025-06-18"</c>, the comparison threw, even though the legacy
/// negotiation succeeded. These tests guard against that regression.
/// </remarks>
public class DraftProtocolFallbackTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task DraftClient_OnMethodNotFound_FallsBackTo_Initialize_AcceptsDowngradedVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new LegacyServerTestTransport(serverNegotiatedVersion: "2025-06-18");

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ProtocolVersion = McpSession.DraftProtocolVersion,
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.True(transport.ServerDiscoverProbed);
        Assert.True(transport.LegacyInitializeReceived);
        Assert.Equal("2025-06-18", client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task DraftClient_OnInvalidParams_FallsBackTo_Initialize()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new LegacyServerTestTransport(
            serverNegotiatedVersion: "2025-11-25",
            probeErrorCode: (int)McpErrorCode.InvalidParams);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ProtocolVersion = McpSession.DraftProtocolVersion,
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.True(transport.LegacyInitializeReceived);
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task DraftClient_WithMinProtocolVersion_RefusesFallback_BelowMinimum()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new LegacyServerTestTransport(serverNegotiatedVersion: "2025-06-18");

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ProtocolVersion = McpSession.DraftProtocolVersion,
                MinProtocolVersion = McpSession.DraftProtocolVersion,
            }, loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.IsType<McpException>(exception);
        Assert.Contains("minimum", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Minimal in-memory transport that simulates a legacy server: rejects
    /// <c>server/discover</c> (with a configurable JSON-RPC error code) and
    /// responds to <c>initialize</c> with a configurable protocol version.
    /// </summary>
    private sealed class LegacyServerTestTransport(
        string serverNegotiatedVersion,
        int probeErrorCode = (int)McpErrorCode.MethodNotFound) : IClientTransport
    {
        private readonly Channel<JsonRpcMessage> _incomingToClient = Channel.CreateUnbounded<JsonRpcMessage>();

        public string Name => "legacy-server-test-transport";

        public bool ServerDiscoverProbed { get; private set; }

        public bool LegacyInitializeReceived { get; private set; }

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
