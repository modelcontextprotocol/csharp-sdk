using ModelContextProtocol.Protocol;
using System.Buffers;
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
        private const int MaxRetainedBytes = 4 * 1024 * 1024;
        private const int MaxRetainedIdentities = 128;
        private readonly object _lock = new();
        private readonly Dictionary<JsonElement, Action<McpServerTool, CallToolResult>> _plans = [];
        private int _retainedBytes;
        private bool _isActive = true;
        private int _requiresSchemaConformance;

        internal McpServerTool Tool { get; } = tool;
        internal JsonElement? Schema { get; } = schema;
        internal bool IsActive => Volatile.Read(ref _isActive);
        internal bool RequiresSchemaConformance => Volatile.Read(ref _requiresSchemaConformance) != 0;

        internal void RequireSchemaConformance() => Volatile.Write(ref _requiresSchemaConformance, 1);

        internal void Register(
            IReadOnlyList<JsonElement> identities, Action<McpServerTool, CallToolResult> recovery,
            JsonElement? original, int originalBytes)
        {
            lock (_lock)
            {
                if (!_isActive)
                {
                    return;
                }

                HashSet<JsonElement> newIdentities = [];
                foreach (JsonElement identity in identities)
                {
                    if (!_plans.ContainsKey(identity))
                    {
                        newIdentities.Add(identity);
                    }
                }

                if (newIdentities.Count == 0)
                {
                    foreach (JsonElement identity in identities)
                    {
                        _plans[identity] = recovery;
                    }
                    return;
                }

                if (newIdentities.Count > MaxRetainedIdentities - _plans.Count ||
                    _retainedBytes >= MaxRetainedBytes)
                {
                    return;
                }

                HashSet<JsonElement> retainedElements = [];
                foreach (JsonElement identity in identities)
                {
                    retainedElements.Add(identity);
                }

                if (!TryMeasureRetainedBytes(
                    retainedElements, original, originalBytes, MaxRetainedBytes - _retainedBytes, out int retainedBytes))
                {
                    return;
                }

                foreach (JsonElement identity in newIdentities)
                {
                    _plans.Add(identity, recovery);
                }
                _retainedBytes += retainedBytes;
            }
        }

        private static bool TryMeasureRetainedBytes(
            IEnumerable<JsonElement> elements, JsonElement? original, int originalBytes, int limit, out int retainedBytes)
        {
            var counter = new BoundedCountingBufferWriter(limit);
            try
            {
                foreach (JsonElement element in elements)
                {
                    if (original is { } source && element.Equals(source))
                    {
                        // WriteTo normalizes whitespace and can undercount the source document.
                        counter.AddKnownBytes(originalBytes);
                        continue;
                    }

                    using Utf8JsonWriter writer = new(counter);
                    element.WriteTo(writer);
                    writer.Flush();
                }
            }
            catch (RecoveryPlanLimitExceededException)
            {
                retainedBytes = 0;
                return false;
            }

            retainedBytes = counter.WrittenCount;
            return true;
        }

        private sealed class RecoveryPlanLimitExceededException : Exception;

        private sealed class BoundedCountingBufferWriter(int limit) : IBufferWriter<byte>
        {
            private byte[] _buffer = [];
            internal int WrittenCount { get; private set; }

            public void Advance(int count)
            {
                if (count < 0 || count > Math.Min(_buffer.Length, limit - WrittenCount))
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                WrittenCount += count;
            }

            internal void AddKnownBytes(int count)
            {
                if (count < 0 || count > limit - WrittenCount)
                {
                    throw new RecoveryPlanLimitExceededException();
                }

                WrittenCount += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0) => GetBuffer(sizeHint);

            public Span<byte> GetSpan(int sizeHint = 0) => GetBuffer(sizeHint).Span;

            private Memory<byte> GetBuffer(int sizeHint)
            {
                if (sizeHint < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sizeHint));
                }

                int remaining = limit - WrittenCount;
                int required = Math.Max(sizeHint, 1);
                if (required > remaining)
                {
                    throw new RecoveryPlanLimitExceededException();
                }

                if (_buffer.Length < required)
                {
                    _buffer = new byte[Math.Min(remaining, Math.Max(required, 256))];
                }

                return _buffer.AsMemory(0, Math.Min(_buffer.Length, remaining));
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
