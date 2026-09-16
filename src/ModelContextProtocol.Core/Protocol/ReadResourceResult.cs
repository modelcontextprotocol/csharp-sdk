using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a server's response to a <see cref="RequestMethods.ResourcesRead"/> request from the client.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class ReadResourceResult : Result, ICacheableResult
{
    /// <summary>
    /// Gets or sets a list of <see cref="ResourceContents"/> objects that this resource contains.
    /// </summary>
    /// <remarks>
    /// This property contains the actual content of the requested resource, which can be
    /// either text-based (<see cref="TextResourceContents"/>) or binary (<see cref="BlobResourceContents"/>).
    /// The type of content included depends on the resource being accessed.
    /// </remarks>
    [JsonPropertyName("contents")]
    public IList<ResourceContents> Contents { get; set; } = [];

    /// <inheritdoc />
    [JsonPropertyName("ttlMs")]
    [JsonConverter(typeof(TimeSpanMillisecondsConverter))]
    public TimeSpan? TimeToLive { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("cacheScope")]
    [JsonConverter(typeof(CacheScopeConverter))]
    public CacheScope? CacheScope { get; set; }
}
