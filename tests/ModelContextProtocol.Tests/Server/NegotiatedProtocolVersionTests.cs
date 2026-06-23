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

        // The first request establishes the draft version for the stateful session (null -> draft).
        Assert.IsType<JsonRpcResponse>(await RoundTripAsync(id: 1, McpSession.DraftProtocolVersion, ct));

        // Re-sending the same version is an idempotent no-op, not an error.
        Assert.IsType<JsonRpcResponse>(await RoundTripAsync(id: 2, McpSession.DraftProtocolVersion, ct));

        // Switching to a different (still-supported) version mid-session is rejected.
        var error = Assert.IsType<JsonRpcError>(await RoundTripAsync(id: 3, McpSession.LatestProtocolVersion, ct));
        Assert.Equal((int)McpErrorCode.InvalidRequest, error.Error.Code);
        Assert.Contains("protocol version cannot change", error.Error.Message, StringComparison.OrdinalIgnoreCase);

        // The rejected request must not have mutated the negotiated version: the original draft version still works.
        Assert.IsType<JsonRpcResponse>(await RoundTripAsync(id: 4, McpSession.DraftProtocolVersion, ct));
    }

    private async Task<JsonRpcMessage> RoundTripAsync(long id, string protocolVersion, CancellationToken cancellationToken)
    {
        // tools/list is available under both the legacy and draft revisions (unlike ping/initialize,
        // which the draft revision removed), so it exercises the version guard rather than the
        // per-method availability gate.
        var request = new JsonRpcRequest
        {
            Id = new RequestId(id),
            Method = RequestMethods.ToolsList,
            Params = new JsonObject
            {
                ["_meta"] = new JsonObject
                {
                    [NotificationMethods.ProtocolVersionMetaKey] = protocolVersion,
                },
            },
        };

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
