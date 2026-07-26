namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a result that carries time-to-live (TTL) caching hints, allowing clients to cache
/// the response for a period of time before re-fetching.
/// </summary>
/// <remarks>
/// <para>
/// This interface corresponds to the <c>CacheableResult</c> type in the Model Context Protocol
/// schema and is implemented by the results of <c>server/discover</c>, <c>tools/list</c>,
/// <c>prompts/list</c>, <c>resources/list</c>, <c>resources/templates/list</c>, and
/// <c>resources/read</c>.
/// </para>
/// <para>
/// The TTL is a freshness hint, not a guarantee. It supplements rather than replaces the existing
/// <c>list_changed</c> and <c>resources/updated</c> notification mechanisms; both can coexist. A
/// relevant notification invalidates a cached response regardless of any remaining TTL.
/// </para>
/// </remarks>
public interface ICacheableResult
{
    /// <summary>
    /// Gets or sets a hint indicating how long the client may cache this response before re-fetching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The semantics are analogous to the HTTP <c>Cache-Control: max-age</c> directive. The value is
    /// serialized as an integer number of milliseconds under the <c>ttlMs</c> JSON property.
    /// </para>
    /// <para>
    /// A value of <see cref="TimeSpan.Zero"/> indicates the response should be considered immediately
    /// stale; a positive value indicates the client should consider the response fresh for that
    /// duration from the time it was received.
    /// </para>
    /// <para>
    /// When this property is <see langword="null"/> (the field was absent from the response), clients
    /// should assume a default of <see cref="TimeSpan.Zero"/> (immediately stale) and rely on their
    /// own caching heuristics or notifications. The SDK preserves whatever value the server sent and
    /// does not coerce it; a client that receives a negative value should treat it as immediately stale.
    /// </para>
    /// </remarks>
    TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the intended scope of the cached response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When this property is <see langword="null"/> (the field was absent from the response), clients
    /// should treat the response as <see cref="Protocol.CacheScope.Public"/>.
    /// </para>
    /// <para>
    /// An unrecognized or future scope value sent by a server (or a non-string value) is tolerated and
    /// surfaced as <see langword="null"/> rather than causing deserialization of the whole result to
    /// fail, so a single unexpected hint never prevents a client from reading the result.
    /// </para>
    /// </remarks>
    CacheScope? CacheScope { get; set; }
}
