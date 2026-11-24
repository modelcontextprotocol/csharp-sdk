using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a context container that provides access to the client request parameters and resources for the request.
/// </summary>
/// <typeparam name="TParams">Type of the request parameters specific to each MCP operation.</typeparam>
/// <remarks>
/// The <see cref="RequestContext{TParams}"/> encapsulates all contextual information for handling an MCP request.
/// This type is typically received as a parameter in handler delegates registered with IMcpServerBuilder,
/// and can be injected as parameters into <see cref="McpServerTool"/>s.
/// </remarks>
public sealed class RequestContext<TParams> : MessageContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestContext{TParams}"/> class with the specified server, JSON-RPC request, and request parameters.
    /// </summary>
    /// <param name="server">The server with which this instance is associated.</param>
    /// <param name="jsonRpcRequest">The JSON-RPC request associated with this context.</param>
    /// <param name="parameters">The parameters associated with this request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> or <paramref name="jsonRpcRequest"/> is <see langword="null"/>.</exception>
    public RequestContext(McpServer server, JsonRpcRequest jsonRpcRequest, TParams parameters)
        : base(server, jsonRpcRequest)
    {
        Params = parameters;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestContext{TParams}"/> class with the specified server and JSON-RPC request.
    /// </summary>
    /// <param name="server">The server with which this instance is associated.</param>
    /// <param name="jsonRpcRequest">The JSON-RPC request associated with this context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> or <paramref name="jsonRpcRequest"/> is <see langword="null"/>.</exception>
    [Obsolete(Obsoletions.RequestContextParamsConstructor_Message, DiagnosticId = Obsoletions.RequestContextParamsConstructor_DiagnosticId, UrlFormat = Obsoletions.RequestContextParamsConstructor_Url)]
    public RequestContext(McpServer server, JsonRpcRequest jsonRpcRequest)
        : base(server, jsonRpcRequest)
    {
        Params = default!;
    }

    /// <summary>Gets or sets the parameters associated with this request.</summary>
    public TParams Params { get; set; }

    /// <summary>
    /// Gets or sets the primitive that matched the request.
    /// </summary>
    public IMcpServerPrimitive? MatchedPrimitive { get; set; }

    private readonly AsyncLocal<StructuredContentRecoveryState?> _structuredContentRecoveryTarget = new();

    internal StructuredContentRecoveryState? StructuredContentRecoveryTarget
    {
        get => _structuredContentRecoveryTarget.Value is { IsActive: true } target ? target : null;
        set => _structuredContentRecoveryTarget.Value = value;
    }

    internal void RecoverStructuredContent(McpServerTool tool, CallToolResult result) =>
        StructuredContentRecoveryTarget?.Recover(tool, result);

    internal sealed class StructuredContentRecoveryState(McpServerTool tool, JsonElement? schema)
    {
        // Recovery callbacks retain serialized projections until the dispatch completes.
        private const long MaxRetainedStructuredContentBytes = 4 * 1024 * 1024;
        private const int MaxRetainedRecoveryIdentities = 4096;
        private readonly object _lock = new();
        private readonly Dictionary<JsonElement, Action<McpServerTool, CallToolResult>> _plans = [];
        private bool _isActive = true;
        private bool _retentionBudgetExhausted;
        private long _retainedBytes;
        private int _retainedIdentityCount;

        internal McpServerTool Tool { get; } = tool;
        internal JsonElement? Schema { get; } = schema;
        internal bool IsActive => Volatile.Read(ref _isActive);

        internal void Register(IEnumerable<JsonElement> identities, Action<McpServerTool, CallToolResult> recovery)
        {
            lock (_lock)
            {
                if (!_isActive || _retentionBudgetExhausted)
                {
                    return;
                }
            }

            List<JsonElement> retainedIdentities = [];
            long registrationBytes = 0;
            foreach (JsonElement identity in identities)
            {
                retainedIdentities.Add(identity);
                if (retainedIdentities.Count > MaxRetainedRecoveryIdentities)
                {
                    registrationBytes = MaxRetainedStructuredContentBytes + 1;
                    break;
                }

                long remainingBytes = MaxRetainedStructuredContentBytes - registrationBytes;
                registrationBytes += GetSerializedByteCount(identity, remainingBytes);
                if (registrationBytes > MaxRetainedStructuredContentBytes)
                {
                    break;
                }
            }

            lock (_lock)
            {
                if (!_isActive || _retentionBudgetExhausted)
                {
                    return;
                }

                if (registrationBytes > MaxRetainedStructuredContentBytes - _retainedBytes ||
                    retainedIdentities.Count > MaxRetainedRecoveryIdentities - _retainedIdentityCount)
                {
                    // Keep existing plans usable and leave later results unchanged after the budget is exhausted.
                    _retentionBudgetExhausted = true;
                    return;
                }

                foreach (JsonElement identity in retainedIdentities)
                {
                    _plans[identity] = recovery;
                }

                _retainedBytes += registrationBytes;
                _retainedIdentityCount += retainedIdentities.Count;
            }
        }

        private static long GetSerializedByteCount(JsonElement element, long limit)
        {
            using CountingStream stream = new(limit);
            using Utf8JsonWriter writer = new(stream);
            element.WriteTo(writer);
            writer.Flush();
            return stream.BytesWritten;
        }

        private sealed class CountingStream(long limit) : Stream
        {
            internal long BytesWritten { get; private set; }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => BytesWritten;
            public override long Position
            {
                get => BytesWritten;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => AddBytes(count);

#if NET
            public override void Write(ReadOnlySpan<byte> buffer) => AddBytes(buffer.Length);
#endif

            public override void WriteByte(byte value) => AddBytes(1);

            private void AddBytes(int count)
            {
                BytesWritten = BytesWritten > limit - count ? limit + 1 : BytesWritten + count;
            }
        }

        internal void Recover(McpServerTool tool, CallToolResult result)
        {
            if (result.IsError == true || result.StructuredContent is not { } identity)
            {
                return;
            }

            Action<McpServerTool, CallToolResult>? recovery;
            lock (_lock)
            {
                if (!_isActive || !_plans.TryGetValue(identity, out recovery))
                {
                    return;
                }
            }
            recovery(tool, result);
        }

        internal void Clear()
        {
            lock (_lock)
            {
                _isActive = false;
                _plans.Clear();
                _retainedBytes = 0;
                _retainedIdentityCount = 0;
            }
        }
    }

    /// <summary>
    /// Gets the JSON-RPC request associated with this context.
    /// </summary>
    /// <remarks>
    /// This property provides access to the complete JSON-RPC request that initiated this handler invocation,
    /// including the method name, parameters, request ID, and associated transport and user information.
    /// </remarks>
    public JsonRpcRequest JsonRpcRequest
    {
        get => (JsonRpcRequest)JsonRpcMessage;
        set => JsonRpcMessage = value;
    }

    /// <summary>
    /// Ends the current response and enables polling for updates from the server.
    /// </summary>
    /// <param name="retryInterval">The interval at which the client should poll for updates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when polling has been enabled.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the transport does not support polling.</exception>
    public async ValueTask EnablePollingAsync(TimeSpan retryInterval, CancellationToken cancellationToken = default)
    {
        if (JsonRpcRequest.Context?.RelatedTransport is not StreamableHttpPostTransport transport)
        {
            throw new InvalidOperationException("Polling is only supported for Streamable HTTP transports.");
        }

        await transport.EnablePollingAsync(retryInterval, cancellationToken).ConfigureAwait(false);
    }
}
