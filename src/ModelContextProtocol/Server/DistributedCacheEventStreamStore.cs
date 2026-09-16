using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// An <see cref="ISseEventStreamStore"/> implementation backed by <see cref="IDistributedCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// This implementation stores SSE events in a distributed cache, enabling resumability across
/// multiple server instances. Event IDs are encoded with session, stream, and sequence information
/// to allow efficient retrieval of events after a given point.
/// </para>
/// <para>
/// The writer maintains in-memory state for sequence number generation, as there is guaranteed
/// to be only one writer per stream. Readers may be created from separate processes.
/// </para>
/// </remarks>
public sealed partial class DistributedCacheEventStreamStore : ISseEventStreamStore
{
    private readonly IDistributedCache _cache;
    private readonly DistributedCacheEventStreamStoreOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedCacheEventStreamStore"/> class.
    /// </summary>
    /// <param name="options">Configuration options for the store, including the <see cref="IDistributedCache"/> to use.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public DistributedCacheEventStreamStore(IOptions<DistributedCacheEventStreamStoreOptions> options, ILogger<DistributedCacheEventStreamStore>? logger = null)
    {
        Throw.IfNull(options);

        var optionsValue = options.Value;
        _cache = optionsValue.Cache ?? throw new InvalidOperationException(
            $"The '{nameof(DistributedCacheEventStreamStoreOptions)}.{nameof(DistributedCacheEventStreamStoreOptions.Cache)}' property must be set.");
        _options = optionsValue;
        _logger = logger ?? NullLogger<DistributedCacheEventStreamStore>.Instance;
    }

    /// <inheritdoc />
    public ValueTask<ISseEventStreamWriter> CreateStreamAsync(SseEventStreamOptions options, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(options);
        LogStreamCreated(options.SessionId, options.StreamId, options.Mode);
        var writer = new DistributedCacheEventStreamWriter(_cache, options.SessionId, options.StreamId, options.Mode, _options, _logger);
        return new ValueTask<ISseEventStreamWriter>(writer);
    }

    /// <inheritdoc />
    public async ValueTask<ISseEventStreamReader?> GetStreamReaderAsync(string lastEventId, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(lastEventId);

        // Parse the event ID to get session, stream, and sequence information
        if (!DistributedCacheEventIdFormatter.TryParse(lastEventId, out var sessionId, out var streamId, out var sequence))
        {
            LogEventIdParsingFailed(lastEventId);
            return null;
        }

        // Check if the stream exists by looking for its metadata
        var metadataKey = CacheKeys.StreamMetadata(sessionId, streamId);
        var metadataBytes = await _cache.GetAsync(metadataKey, cancellationToken).ConfigureAwait(false);
        if (metadataBytes is null)
        {
            LogStreamMetadataNotFound(sessionId, streamId);
            return null;
        }

        var metadata = JsonSerializer.Deserialize(metadataBytes, DistributedCacheEventStreamStoreJsonUtilities.StreamMetadataJsonTypeInfo);
        if (metadata is null)
        {
            LogStreamMetadataDeserializationFailed(sessionId, streamId);
            return null;
        }

        var startSequence = sequence + 1;
        LogStreamReaderCreated(sessionId, streamId, startSequence, metadata.LastSequence);
        return new DistributedCacheEventStreamReader(_cache, sessionId, streamId, startSequence, metadata, _options, _logger);
    }

    /// <summary>
    /// Provides methods for generating cache keys.
    /// </summary>
    /// <remarks>
    /// Cache keys are versioned to allow format changes without conflicts with existing entries.
    /// When the cache format changes, increment <see cref="Version"/> to invalidate old entries.
    /// </remarks>
    internal static class CacheKeys
    {
        /// <summary>
        /// The current cache key version. Increment this when changing the cache format
        /// to ensure old entries are ignored.
        /// </summary>
        private const string Version = "v1";
        private const string Prefix = $"mcp:sse:{Version}:";

        public static string StreamMetadata(string sessionId, string streamId)
        {
            var sessionIdBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sessionId));
            var streamIdBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(streamId));
            return $"{Prefix}meta:{sessionIdBase64}:{streamIdBase64}";
        }

        public static string Event(string eventId)
            => $"{Prefix}event:{eventId}";
    }

    /// <summary>
    /// Metadata about a stream stored in the cache.
    /// </summary>
    internal sealed class StreamMetadata
    {
        public SseEventStreamMode Mode { get; set; }
        public bool IsCompleted { get; set; }
        public long LastSequence { get; set; }
    }

    /// <summary>
    /// Serialized representation of an SSE event stored in the cache.
    /// </summary>
    internal sealed class StoredEvent
    {
        public string? EventType { get; set; }
        public string? EventId { get; set; }
        public int? ReconnectionIntervalMs { get; set; }
        public JsonRpcMessage? Data { get; set; }
    }

    private sealed partial class DistributedCacheEventStreamWriter : ISseEventStreamWriter
    {
        private readonly IDistributedCache _cache;
        private readonly string _sessionId;
        private readonly string _streamId;
        private SseEventStreamMode _mode;
        private readonly DistributedCacheEventStreamStoreOptions _options;
        private readonly ILogger _logger;
        private long _sequence;
        private bool _disposed;

        public DistributedCacheEventStreamWriter(
            IDistributedCache cache,
            string sessionId,
            string streamId,
            SseEventStreamMode mode,
            DistributedCacheEventStreamStoreOptions options,
            ILogger logger)
        {
            _cache = cache;
            _sessionId = sessionId;
            _streamId = streamId;
            _mode = mode;
            _options = options;
            _logger = logger;
        }

        public async ValueTask SetModeAsync(SseEventStreamMode mode, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            LogStreamModeChanged(_sessionId, _streamId, mode);
            _mode = mode;
            await UpdateMetadataAsync(isCompleted: false, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<SseItem<JsonRpcMessage?>> WriteEventAsync(SseItem<JsonRpcMessage?> sseItem, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Skip if already has an event ID
            if (sseItem.EventId is not null)
            {
                LogEventAlreadyHasId(_sessionId, _streamId, sseItem.EventId);
                return sseItem;
            }

            // Generate a new sequence number and event ID
            var sequence = Interlocked.Increment(ref _sequence);
            var eventId = DistributedCacheEventIdFormatter.Format(_sessionId, _streamId, sequence);
            var newItem = sseItem with { EventId = eventId };

            // Store the event in the cache
            var storedEvent = new StoredEvent
            {
                EventType = newItem.EventType,
                EventId = eventId,
                ReconnectionIntervalMs = newItem.ReconnectionInterval.HasValue
                    ? (int)newItem.ReconnectionInterval.Value.TotalMilliseconds
                    : null,
                Data = newItem.Data,
            };

            var eventBytes = JsonSerializer.SerializeToUtf8Bytes(storedEvent, DistributedCacheEventStreamStoreJsonUtilities.StoredEventJsonTypeInfo);
            var eventKey = CacheKeys.Event(eventId);

            await _cache.SetAsync(eventKey, eventBytes, new DistributedCacheEntryOptions
            {
                SlidingExpiration = _options.EventSlidingExpiration,
                AbsoluteExpirationRelativeToNow = _options.EventAbsoluteExpiration,
            }, cancellationToken).ConfigureAwait(false);

            // Update metadata with the latest sequence
            await UpdateMetadataAsync(isCompleted: false, cancellationToken).ConfigureAwait(false);

            LogEventWritten(_sessionId, _streamId, eventId, sequence);
            return newItem;
        }

        private async ValueTask UpdateMetadataAsync(bool isCompleted, CancellationToken cancellationToken)
        {
            var metadata = new StreamMetadata
            {
                Mode = _mode,
                IsCompleted = isCompleted,
                LastSequence = Interlocked.Read(ref _sequence),
            };

            var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, DistributedCacheEventStreamStoreJsonUtilities.StreamMetadataJsonTypeInfo);
            var metadataKey = CacheKeys.StreamMetadata(_sessionId, _streamId);

            await _cache.SetAsync(metadataKey, metadataBytes, new DistributedCacheEntryOptions
            {
                SlidingExpiration = _options.MetadataSlidingExpiration,
                AbsoluteExpirationRelativeToNow = _options.MetadataAbsoluteExpiration,
            }, cancellationToken).ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
#if NET
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DistributedCacheEventStreamWriter));
            }
#endif
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Mark the stream as completed in the metadata
            await UpdateMetadataAsync(isCompleted: true, CancellationToken.None).ConfigureAwait(false);
            LogStreamWriterDisposed(_sessionId, _streamId, Interlocked.Read(ref _sequence));
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Stream mode changed for session '{SessionId}', stream '{StreamId}' to {Mode}.")]
        private partial void LogStreamModeChanged(string sessionId, string streamId, SseEventStreamMode mode);

        [LoggerMessage(Level = LogLevel.Trace, Message = "Event already has ID '{EventId}' for session '{SessionId}', stream '{StreamId}'. Skipping ID generation.")]
        private partial void LogEventAlreadyHasId(string sessionId, string streamId, string eventId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Event written to session '{SessionId}', stream '{StreamId}' with ID '{EventId}' (sequence {Sequence}).")]
        private partial void LogEventWritten(string sessionId, string streamId, string eventId, long sequence);

        [LoggerMessage(Level = LogLevel.Information, Message = "Stream writer disposed for session '{SessionId}', stream '{StreamId}'. Total events written: {TotalEvents}.")]
        private partial void LogStreamWriterDisposed(string sessionId, string streamId, long totalEvents);
    }

    private sealed partial class DistributedCacheEventStreamReader : ISseEventStreamReader
    {
        private readonly IDistributedCache _cache;
        private readonly long _startSequence;
        private readonly StreamMetadata _initialMetadata;
        private readonly DistributedCacheEventStreamStoreOptions _options;
        private readonly ILogger _logger;

        public DistributedCacheEventStreamReader(
            IDistributedCache cache,
            string sessionId,
            string streamId,
            long startSequence,
            StreamMetadata initialMetadata,
            DistributedCacheEventStreamStoreOptions options,
            ILogger logger)
        {
            _cache = cache;
            SessionId = sessionId;
            StreamId = streamId;
            _startSequence = startSequence;
            _initialMetadata = initialMetadata;
            _options = options;
            _logger = logger;
        }

        public string SessionId { get; }
        public string StreamId { get; }

        public async IAsyncEnumerable<SseItem<JsonRpcMessage?>> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Start from the sequence after the last received event
            var currentSequence = _startSequence;

            // Use the initial metadata passed to the constructor for the first read.
            var lastSequence = _initialMetadata.LastSequence;
            var isCompleted = _initialMetadata.IsCompleted;
            var mode = _initialMetadata.Mode;

            LogReadingEventsStarted(SessionId, StreamId, _startSequence, lastSequence);

            while (!cancellationToken.IsCancellationRequested)
            {
                // Read all available events from currentSequence + 1 to lastSequence
                for (; currentSequence <= lastSequence; currentSequence++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var eventId = DistributedCacheEventIdFormatter.Format(SessionId, StreamId, currentSequence);
                    var eventKey = CacheKeys.Event(eventId);
                    var eventBytes = await _cache.GetAsync(eventKey, cancellationToken).ConfigureAwait(false)
                        ?? throw new McpException($"SSE event with ID '{eventId}' was not found in the cache. The event may have expired.");

                    var storedEvent = JsonSerializer.Deserialize(eventBytes, DistributedCacheEventStreamStoreJsonUtilities.StoredEventJsonTypeInfo);
                    if (storedEvent is not null)
                    {
                        LogEventRead(SessionId, StreamId, eventId, currentSequence);
                        yield return new SseItem<JsonRpcMessage?>(storedEvent.Data, storedEvent.EventType)
                        {
                            EventId = storedEvent.EventId,
                            ReconnectionInterval = storedEvent.ReconnectionIntervalMs.HasValue
                                ? TimeSpan.FromMilliseconds(storedEvent.ReconnectionIntervalMs.Value)
                                : null,
                        };
                    }
                }

                // If in polling mode, stop after returning currently available events
                if (mode == SseEventStreamMode.Polling)
                {
                    LogReadingEventsCompletedPolling(SessionId, StreamId, currentSequence - 1);
                    yield break;
                }

                // If the stream is completed and we've read all events, stop
                if (isCompleted)
                {
                    LogReadingEventsCompletedStreamEnded(SessionId, StreamId, currentSequence - 1);
                    yield break;
                }

                // Wait before polling again for new events
                LogWaitingForNewEvents(SessionId, StreamId, _options.StreamReaderPollingInterval);
                await Task.Delay(_options.StreamReaderPollingInterval, cancellationToken).ConfigureAwait(false);

                // Refresh metadata to get the latest sequence and completion status
                var metadataKey = CacheKeys.StreamMetadata(SessionId, StreamId);
                var metadataBytes = await _cache.GetAsync(metadataKey, cancellationToken).ConfigureAwait(false)
                    ?? throw new McpException($"Stream metadata for session '{SessionId}' and stream '{StreamId}' was not found in the cache. The metadata may have expired.");

                var currentMetadata = JsonSerializer.Deserialize(metadataBytes, DistributedCacheEventStreamStoreJsonUtilities.StreamMetadataJsonTypeInfo)
                    ?? throw new McpException($"Stream metadata for session '{SessionId}' and stream '{StreamId}' could not be deserialized.");

                lastSequence = currentMetadata.LastSequence;
                isCompleted = currentMetadata.IsCompleted;
                mode = currentMetadata.Mode;
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Starting to read events for session '{SessionId}', stream '{StreamId}' starting at sequence {StartSequence}. Last available sequence: {LastSequence}.")]
        private partial void LogReadingEventsStarted(string sessionId, string streamId, long startSequence, long lastSequence);

        [LoggerMessage(Level = LogLevel.Trace, Message = "Event read from session '{SessionId}', stream '{StreamId}' with ID '{EventId}' (sequence {Sequence}).")]
        private partial void LogEventRead(string sessionId, string streamId, string eventId, long sequence);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Reading events completed for session '{SessionId}', stream '{StreamId}' in polling mode. Last sequence read: {LastSequence}.")]
        private partial void LogReadingEventsCompletedPolling(string sessionId, string streamId, long lastSequence);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Reading events completed for session '{SessionId}', stream '{StreamId}' as stream has ended. Last sequence read: {LastSequence}.")]
        private partial void LogReadingEventsCompletedStreamEnded(string sessionId, string streamId, long lastSequence);

        [LoggerMessage(Level = LogLevel.Trace, Message = "Waiting for new events on session '{SessionId}', stream '{StreamId}'. Polling interval: {PollingInterval}.")]
        private partial void LogWaitingForNewEvents(string sessionId, string streamId, TimeSpan pollingInterval);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stream created for session '{SessionId}', stream '{StreamId}' with mode {Mode}.")]
    private partial void LogStreamCreated(string sessionId, string streamId, SseEventStreamMode mode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stream reader created for session '{SessionId}', stream '{StreamId}' starting at sequence {StartSequence}. Last available sequence: {LastSequence}.")]
    private partial void LogStreamReaderCreated(string sessionId, string streamId, long startSequence, long lastSequence);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse event ID '{EventId}'. Unable to create stream reader.")]
    private partial void LogEventIdParsingFailed(string eventId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stream metadata not found for session '{SessionId}', stream '{StreamId}'.")]
    private partial void LogStreamMetadataNotFound(string sessionId, string streamId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize stream metadata for session '{SessionId}', stream '{StreamId}'.")]
    private partial void LogStreamMetadataDeserializationFailed(string sessionId, string streamId);
}
