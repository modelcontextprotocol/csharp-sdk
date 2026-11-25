using ModelContextProtocol.Protocol;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
    private McpServerTool? _matchedToolByName;
    private string? _matchedToolName;
    private static readonly ConditionalWeakTable<object, DeferredOutputValidationCollection> s_deferredOutputValidations = new();

    internal StructuredContentRecoveryState? StructuredContentRecoveryTarget
    {
        get => _structuredContentRecoveryTarget.Value is { IsActive: true } target ? target : null;
        set => _structuredContentRecoveryTarget.Value = value;
    }

    internal void MatchToolByName(string? name, McpServerTool? tool)
    {
        bool isExplicitOverride = MatchedPrimitive is not null &&
            !ReferenceEquals(MatchedPrimitive, _matchedToolByName);
        if (isExplicitOverride)
        {
            return;
        }

        if (_matchedToolByName is not null &&
            (!ReferenceEquals(tool, _matchedToolByName) || !string.Equals(name, _matchedToolName, StringComparison.Ordinal)))
        {
            MatchedPrimitive = null;
        }

        if (tool is not null)
        {
            MatchedPrimitive = tool;
            _matchedToolByName = tool;
            _matchedToolName = name;
        }
        else
        {
            _matchedToolByName = null;
            _matchedToolName = null;
        }
    }

    internal void CopyMatchedToolMatchFrom(RequestContext<CallToolRequestParams> source)
    {
        MatchedPrimitive = source.MatchedPrimitive;
        _matchedToolByName = source._matchedToolByName;
        _matchedToolName = source._matchedToolName;
    }

    internal static void RegisterDeferredOutputValidation(
        McpServer server, CallToolResult result, StructuredContentRecoveryState state)
    {
        s_deferredOutputValidations.GetValue(server.ToolOutputValidationScopeKey, static _ => new())
            .Register(result, state);
    }

    internal static IReadOnlyList<StructuredContentRecoveryState> GetDeferredOutputValidations(
        McpServer server,
        CallToolResult result,
        out bool overflowed)
    {
        if (s_deferredOutputValidations.TryGetValue(server.ToolOutputValidationScopeKey, out DeferredOutputValidationCollection? collection))
        {
            var entries = collection.GetEntries(out overflowed);
            return DeferredOutputValidationCollection.GetCandidates(entries, result);
        }

        overflowed = false;
        return [];
    }

    internal static void ClearDeferredOutputValidations(McpServer server)
    {
        s_deferredOutputValidations.Remove(server.ToolOutputValidationScopeKey);
    }

    internal void RecoverStructuredContent(McpServerTool tool, CallToolResult result) =>
        StructuredContentRecoveryTarget?.Recover(tool, result);

    internal sealed class StructuredContentRecoveryState(
        McpServerTool tool,
        IReadOnlyList<(McpServerTool Tool, JsonElement? Schema)> schemas)
    {
        private const int MaxRetainedBytes = 4 * 1024 * 1024;
        private const int MaxRetainedIdentities = 128;
        private readonly (McpServerTool Tool, JsonElement? Schema)[] _schemas = [.. schemas];
        private readonly object _lock = new();
        private readonly Dictionary<JsonElement, Action<McpServerTool, CallToolResult>> _plans = [];
        private int _retainedBytes;
        private bool _isActive = true;
        private int _requiresSchemaConformance;
        private int _returnsExplicitResult;
        private int _skipExternalSchemaValidation = 1;

        internal McpServerTool Tool { get; } = tool;
        internal JsonElement? Schema => _schemas[0].Schema;
        internal bool IsActive => Volatile.Read(ref _isActive);
        internal bool RequiresSchemaConformance => Volatile.Read(ref _requiresSchemaConformance) != 0;
        internal bool ReturnsExplicitResult => Volatile.Read(ref _returnsExplicitResult) != 0;
        internal bool SkipExternalSchemaValidation => Volatile.Read(ref _skipExternalSchemaValidation) != 0;

        internal void RequireSchemaConformance() => Volatile.Write(ref _requiresSchemaConformance, 1);
        internal void MarkExplicitResult() => Volatile.Write(ref _returnsExplicitResult, 1);
        internal void AllowExternalSchemaResolution() => Volatile.Write(ref _skipExternalSchemaValidation, 0);

        internal bool TryGetSchema(McpServerTool tool, out JsonElement? schema)
        {
            foreach (var (capturedTool, capturedSchema) in _schemas)
            {
                if (ReferenceEquals(capturedTool, tool))
                {
                    schema = capturedSchema;
                    return true;
                }
            }

            schema = null;
            return false;
        }

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

    internal sealed class DeferredOutputValidationCollection
    {
        private const int MaxEntries = 128;
        // Larger values are not fingerprinted; copied results then fall back to all dispatched schemas.
        private const int MaxStructuredContentFingerprintBytes = 64 * 1024;
        private readonly object _lock = new();
        private readonly List<DeferredOutputValidationEntry> _entries = [];
        private bool _overflowed;

        internal readonly record struct DeferredOutputValidationEntry(
            WeakReference<CallToolResult> Result,
            byte[]? StructuredContentHash,
            int StructuredContentByteCount,
            StructuredContentRecoveryState State);

        internal IReadOnlyList<DeferredOutputValidationEntry> GetEntries(out bool overflowed)
        {
            lock (_lock)
            {
                overflowed = _overflowed;
                return [.. _entries];
            }
        }

        internal void Register(CallToolResult result, StructuredContentRecoveryState state)
        {
            lock (_lock)
            {
                if (_entries.Count >= MaxEntries)
                {
                    _overflowed = true;
                    return;
                }
            }

            byte[]? structuredContentHash = null;
            int structuredContentByteCount = 0;
            if (result.StructuredContent is { } structuredContent)
            {
                TryGetStructuredContentFingerprint(
                    structuredContent, out structuredContentHash, out structuredContentByteCount);
            }

            lock (_lock)
            {
                if (_entries.Count >= MaxEntries)
                {
                    _overflowed = true;
                    return;
                }

                _entries.Add(new(
                    new WeakReference<CallToolResult>(result),
                    structuredContentHash,
                    structuredContentByteCount,
                    state));
            }
        }

        internal static IReadOnlyList<StructuredContentRecoveryState> GetCandidates(
            IReadOnlyList<DeferredOutputValidationEntry> entries,
            CallToolResult result)
        {
            List<StructuredContentRecoveryState> exactMatches = [];
            foreach (var entry in entries)
            {
                if (entry.Result.TryGetTarget(out CallToolResult? registeredResult) &&
                    ReferenceEquals(result, registeredResult) &&
                    !exactMatches.Contains(entry.State))
                {
                    exactMatches.Add(entry.State);
                }
            }

            if (exactMatches.Count > 0)
            {
                return exactMatches;
            }

            if (result.StructuredContent is { } content &&
                entries.Any(static entry => entry.StructuredContentHash is not null) &&
                TryGetStructuredContentFingerprint(
                    content, out byte[]? resultHash, out int resultByteCount))
            {
                List<StructuredContentRecoveryState> contentMatches = [];
                foreach (var entry in entries)
                {
                    if (entry.StructuredContentHash is { } registeredHash &&
                        entry.StructuredContentByteCount == resultByteCount &&
                        registeredHash.AsSpan().SequenceEqual(resultHash) &&
                        !contentMatches.Contains(entry.State))
                    {
                        contentMatches.Add(entry.State);
                    }
                }

                if (contentMatches.Count > 0)
                {
                    return contentMatches;
                }
            }

            List<StructuredContentRecoveryState> candidates = [];
            foreach (var entry in entries)
            {
                if (!candidates.Contains(entry.State))
                {
                    candidates.Add(entry.State);
                }
            }

            return candidates;
        }

        private static bool TryGetStructuredContentFingerprint(
            JsonElement content, out byte[]? hash, out int byteCount)
        {
            using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var bufferWriter = new FingerprintBufferWriter(incrementalHash, MaxStructuredContentFingerprintBytes);

            try
            {
                using (var writer = new Utf8JsonWriter(bufferWriter))
                {
                    content.WriteTo(writer);
                    writer.Flush();
                }

                hash = incrementalHash.GetHashAndReset();
                byteCount = bufferWriter.WrittenCount;
                return true;
            }
            catch (FingerprintLimitExceededException)
            {
                hash = null;
                byteCount = 0;
                return false;
            }
        }

        private sealed class FingerprintLimitExceededException : Exception;

        private sealed class FingerprintBufferWriter(IncrementalHash hash, int maxBytes) : IBufferWriter<byte>, IDisposable
        {
            private byte[]? _buffer = ArrayPool<byte>.Shared.Rent(256);
            internal int WrittenCount { get; private set; }

            public void Advance(int count)
            {
                byte[] buffer = _buffer ?? throw new ObjectDisposedException(nameof(FingerprintBufferWriter));
                int remaining = maxBytes - WrittenCount;
                if (count < 0 || count > Math.Min(buffer.Length, remaining))
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                hash.AppendData(buffer, 0, count);
                WrittenCount += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0) => GetBuffer(sizeHint);

            public Span<byte> GetSpan(int sizeHint = 0) => GetBuffer(sizeHint).Span;

            private Memory<byte> GetBuffer(int sizeHint)
            {
                byte[] buffer = _buffer ?? throw new ObjectDisposedException(nameof(FingerprintBufferWriter));
                if (sizeHint < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sizeHint));
                }

                int remaining = maxBytes - WrittenCount;
                int required = Math.Max(sizeHint, 1);
                if (required > remaining)
                {
                    throw new FingerprintLimitExceededException();
                }

                if (buffer.Length < required)
                {
                    byte[] largerBuffer = ArrayPool<byte>.Shared.Rent(required);
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                    _buffer = buffer = largerBuffer;
                }

                return buffer.AsMemory(0, Math.Min(buffer.Length, remaining));
            }

            public void Dispose()
            {
                if (_buffer is { } buffer)
                {
                    _buffer = null;
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }
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
