using Microsoft.Extensions.Caching.Distributed;
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
public sealed class DistributedCacheEventStreamStore : ISseEventStreamStore
{
    private readonly IDistributedCache _cache;
    private readonly DistributedCacheEventStreamStoreOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedCacheEventStreamStore"/> class.
    /// </summary>
    /// <param name="cache">The distributed cache to use for storage.</param>
    /// <param name="options">Optional configuration options for the store.</param>
    public DistributedCacheEventStreamStore(IDistributedCache cache, DistributedCacheEventStreamStoreOptions? options = null)
    {
        Throw.IfNull(cache);
        _cache = cache;
        _options = options ?? new();
    }

    /// <inheritdoc />
    public ValueTask<ISseEventStreamWriter> CreateStreamAsync(SseEventStreamOptions options, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(options);
        var writer = new DistributedCacheEventStreamWriter(_cache, options.SessionId, options.StreamId, options.Mode, _options);
        return new ValueTask<ISseEventStreamWriter>(writer);
    }

    /// <inheritdoc />
    public async ValueTask<ISseEventStreamReader?> GetStreamReaderAsync(string lastEventId, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(lastEventId);

        // Parse the event ID to get session, stream, and sequence information
        if (!DistributedCacheEventIdFormatter.TryParse(lastEventId, out var sessionId, out var streamId, out var sequence))
        {
            return null;
        }

        // Check if the stream exists by looking for its metadata
        var metadataKey = CacheKeys.StreamMetadata(sessionId, streamId);
        var metadataBytes = await _cache.GetAsync(metadataKey, cancellationToken).ConfigureAwait(false);
        if (metadataBytes is null)
        {
            return null;
        }

        var metadata = JsonSerializer.Deserialize(metadataBytes, McpJsonUtilities.JsonContext.Default.StreamMetadata);
        if (metadata is null)
        {
            return null;
        }

        var startSequence = sequence + 1;
        return new DistributedCacheEventStreamReader(_cache, sessionId, streamId, startSequence, metadata, _options);
    }

    /// <summary>
    /// Provides methods for generating cache keys.
    /// </summary>
    internal static class CacheKeys
    {
        private const string Prefix = "mcp:sse:";

        public static string StreamMetadata(string sessionId, string streamId) =>
            $"{Prefix}meta:{sessionId}:{streamId}";

        public static string Event(string eventId) =>
            $"{Prefix}event:{eventId}";

        public static string StreamEventCount(string sessionId, string streamId) =>
            $"{Prefix}count:{sessionId}:{streamId}";
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
        public JsonRpcMessage? Data { get; set; }
    }

    private sealed class DistributedCacheEventStreamWriter : ISseEventStreamWriter
    {
        private readonly IDistributedCache _cache;
        private readonly DistributedCacheEventStreamStoreOptions _options;
        private long _sequence;
        private bool _disposed;

        public DistributedCacheEventStreamWriter(
            IDistributedCache cache,
            string sessionId,
            string streamId,
            SseEventStreamMode mode,
            DistributedCacheEventStreamStoreOptions options)
        {
            _cache = cache;
            SessionId = sessionId;
            StreamId = streamId;
            Mode = mode;
            _options = options;
        }

        public string SessionId { get; }
        public string StreamId { get; }
        public SseEventStreamMode Mode { get; private set; }

        public async ValueTask SetModeAsync(SseEventStreamMode mode, CancellationToken cancellationToken = default)
        {
            Mode = mode;
            await UpdateMetadataAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<SseItem<JsonRpcMessage?>> WriteEventAsync(SseItem<JsonRpcMessage?> sseItem, CancellationToken cancellationToken = default)
        {
            // Skip if already has an event ID
            if (sseItem.EventId is not null)
            {
                return sseItem;
            }

            // Generate a new sequence number and event ID
            var sequence = Interlocked.Increment(ref _sequence);
            var eventId = DistributedCacheEventIdFormatter.Format(SessionId, StreamId, sequence);
            var newItem = sseItem with { EventId = eventId };

            // Store the event in the cache
            var storedEvent = new StoredEvent
            {
                EventType = newItem.EventType,
                EventId = eventId,
                Data = newItem.Data,
            };

            var eventBytes = JsonSerializer.SerializeToUtf8Bytes(storedEvent, McpJsonUtilities.JsonContext.Default.StoredEvent);
            var eventKey = CacheKeys.Event(eventId);

            await _cache.SetAsync(eventKey, eventBytes, new DistributedCacheEntryOptions
            {
                SlidingExpiration = _options.EventSlidingExpiration,
                AbsoluteExpirationRelativeToNow = _options.EventAbsoluteExpiration,
            }, cancellationToken).ConfigureAwait(false);

            // Update metadata with the latest sequence
            await UpdateMetadataAsync(cancellationToken).ConfigureAwait(false);

            return newItem;
        }

        private async ValueTask UpdateMetadataAsync(CancellationToken cancellationToken)
        {
            var metadata = new StreamMetadata
            {
                Mode = Mode,
                IsCompleted = _disposed,
                LastSequence = Interlocked.Read(ref _sequence),
            };

            var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, McpJsonUtilities.JsonContext.Default.StreamMetadata);
            var metadataKey = CacheKeys.StreamMetadata(SessionId, StreamId);

            await _cache.SetAsync(metadataKey, metadataBytes, new DistributedCacheEntryOptions
            {
                SlidingExpiration = _options.MetadataSlidingExpiration,
                AbsoluteExpirationRelativeToNow = _options.MetadataAbsoluteExpiration,
            }, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Mark the stream as completed in the metadata
            await UpdateMetadataAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class DistributedCacheEventStreamReader : ISseEventStreamReader
    {
        private readonly IDistributedCache _cache;
        private readonly long _startSequence;
        private readonly StreamMetadata _initialMetadata;
        private readonly DistributedCacheEventStreamStoreOptions _options;

        public DistributedCacheEventStreamReader(
            IDistributedCache cache,
            string sessionId,
            string streamId,
            long startSequence,
            StreamMetadata initialMetadata,
            DistributedCacheEventStreamStoreOptions options)
        {
            _cache = cache;
            SessionId = sessionId;
            StreamId = streamId;
            _startSequence = startSequence;
            _initialMetadata = initialMetadata;
            _options = options;
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

            while (!cancellationToken.IsCancellationRequested)
            {
                // Read all available events from currentSequence + 1 to lastSequence
                for (; currentSequence <= lastSequence; currentSequence++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var eventId = DistributedCacheEventIdFormatter.Format(SessionId, StreamId, currentSequence);
                    var eventKey = CacheKeys.Event(eventId);
                    var eventBytes = await _cache.GetAsync(eventKey, cancellationToken).ConfigureAwait(false);

                    if (eventBytes is null)
                    {
                        // Event may have expired; skip to next
                        continue;
                    }

                    var storedEvent = JsonSerializer.Deserialize(eventBytes, McpJsonUtilities.JsonContext.Default.StoredEvent);
                    if (storedEvent is not null)
                    {
                        yield return new SseItem<JsonRpcMessage?>(storedEvent.Data, storedEvent.EventType)
                        {
                            EventId = storedEvent.EventId,
                        };
                    }
                }

                // If in polling mode, stop after returning currently available events
                if (mode == SseEventStreamMode.Polling)
                {
                    yield break;
                }

                // If the stream is completed and we've read all events, stop
                if (isCompleted)
                {
                    yield break;
                }

                // Wait before polling again for new events
                await Task.Delay(_options.PollingInterval, cancellationToken).ConfigureAwait(false);

                // Refresh metadata to get the latest sequence and completion status
                var metadataKey = CacheKeys.StreamMetadata(SessionId, StreamId);
                var metadataBytes = await _cache.GetAsync(metadataKey, cancellationToken).ConfigureAwait(false);

                if (metadataBytes is null)
                {
                    // Metadata expired - treat stream as complete to avoid infinite loop
                    yield break;
                }

                var currentMetadata = JsonSerializer.Deserialize(metadataBytes, McpJsonUtilities.JsonContext.Default.StreamMetadata);
                if (currentMetadata is null)
                {
                    yield break;
                }

                lastSequence = currentMetadata.LastSequence;
                isCompleted = currentMetadata.IsCompleted;
                mode = currentMetadata.Mode;
            }
        }
    }
}
