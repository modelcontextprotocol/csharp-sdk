using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides JSON serialization utilities for <see cref="DistributedCacheEventStreamStore"/> types.
/// </summary>
/// <remarks>
/// This class provides source-generated serialization for the internal types used by
/// <see cref="DistributedCacheEventStreamStore"/> to persist SSE events and stream metadata.
/// It combines with <see cref="McpJsonUtilities.DefaultOptions"/> to support the full object
/// graph including <see cref="Protocol.JsonRpcMessage"/> and its derived types.
/// </remarks>
internal static partial class DistributedCacheEventStreamStoreJsonUtilities
{
    /// <summary>
    /// Gets the <see cref="JsonSerializerOptions"/> instance for serializing distributed cache event stream store types.
    /// </summary>
    /// <remarks>
    /// This options instance combines the source-generated context for <see cref="DistributedCacheEventStreamStore.StreamMetadata"/>
    /// and <see cref="DistributedCacheEventStreamStore.StoredEvent"/> with <see cref="McpJsonUtilities.DefaultOptions"/>
    /// to support the full object graph including <see cref="Protocol.JsonRpcMessage"/> types.
    /// </remarks>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    /// <summary>
    /// Gets the <see cref="JsonTypeInfo{T}"/> for <see cref="DistributedCacheEventStreamStore.StreamMetadata"/>.
    /// </summary>
    public static JsonTypeInfo<DistributedCacheEventStreamStore.StreamMetadata> StreamMetadataJsonTypeInfo { get; } =
        (JsonTypeInfo<DistributedCacheEventStreamStore.StreamMetadata>)DefaultOptions.GetTypeInfo(typeof(DistributedCacheEventStreamStore.StreamMetadata));

    /// <summary>
    /// Gets the <see cref="JsonTypeInfo{T}"/> for <see cref="DistributedCacheEventStreamStore.StoredEvent"/>.
    /// </summary>
    public static JsonTypeInfo<DistributedCacheEventStreamStore.StoredEvent> StoredEventJsonTypeInfo { get; } =
        (JsonTypeInfo<DistributedCacheEventStreamStore.StoredEvent>)DefaultOptions.GetTypeInfo(typeof(DistributedCacheEventStreamStore.StoredEvent));

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        // Copy the configuration from McpJsonUtilities.DefaultOptions.
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);

        // Add our source-generated context for StreamMetadata and StoredEvent.
        options.TypeInfoResolverChain.Insert(0, JsonContext.Default);

        options.MakeReadOnly();
        return options;
    }

    [JsonSourceGenerationOptions(
        JsonSerializerDefaults.Web,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(DistributedCacheEventStreamStore.StreamMetadata))]
    [JsonSerializable(typeof(DistributedCacheEventStreamStore.StoredEvent))]
    private sealed partial class JsonContext : JsonSerializerContext;
}
