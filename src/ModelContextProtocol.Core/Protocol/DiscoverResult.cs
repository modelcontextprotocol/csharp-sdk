using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result returned from a <see cref="RequestMethods.ServerDiscover"/> request.
/// </summary>
/// <remarks>
/// <para>
/// Introduced by the draft protocol revision (SEP-2575) as the canonical way for a client
/// to learn what a server supports without performing the legacy <c>initialize</c> handshake.
/// </para>
/// </remarks>
public sealed class DiscoverResult : Result, ICacheableResult
{
    /// <summary>
    /// Gets or sets the list of MCP protocol version strings that the server supports.
    /// </summary>
    /// <remarks>
    /// The client should choose a version from this list for use in subsequent requests.
    /// </remarks>
    [JsonPropertyName("supportedVersions")]
    public required IList<string> SupportedVersions { get; set; }

    /// <summary>
    /// Gets or sets the capabilities of the server.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public required ServerCapabilities Capabilities { get; set; }

    /// <summary>
    /// Gets or sets information about the server implementation.
    /// </summary>
    [JsonPropertyName("serverInfo")]
    public required Implementation ServerInfo { get; set; }

    /// <summary>
    /// Gets or sets optional instructions describing how to use the server and its features.
    /// </summary>
    /// <remarks>
    /// This can be used by clients to improve an LLM's understanding of the server,
    /// for example by including it in a system prompt.
    /// </remarks>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Spec PR #2855 makes <c>ttlMs</c> a required field on <see cref="DiscoverResult"/>. The
    /// server emits a safe default (<see cref="TimeSpan.Zero"/>, i.e. immediately stale) on
    /// draft sessions when the application has not set an explicit value, preserving today's
    /// "do not cache" behavior while satisfying the wire requirement.
    /// </remarks>
    [JsonPropertyName("ttlMs")]
    [JsonConverter(typeof(TimeSpanMillisecondsConverter))]
    public TimeSpan? TimeToLive { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Spec PR #2855 makes <c>cacheScope</c> a required field on <see cref="DiscoverResult"/>. The
    /// server emits a safe default (<see cref="Protocol.CacheScope.Private"/>) on draft sessions
    /// when the application has not set an explicit value.
    /// </remarks>
    [JsonPropertyName("cacheScope")]
    [JsonConverter(typeof(CacheScopeConverter))]
    public CacheScope? CacheScope { get; set; }
}
