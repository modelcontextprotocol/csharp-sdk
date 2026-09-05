using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Configures the behavior of the MCP Skills extension on a server.
/// </summary>
public sealed class McpSkillsOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether <c>skills/list</c> results carry the base protocol's
    /// list-caching attributes (<c>ttlMs</c> and <c>cacheScope</c>) only when the negotiated protocol
    /// version is 2026-07-28 or later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SEP-2640 scopes those attributes to protocol versions 2026-07-28 and later, so gating is the
    /// conservative reading and the default. The condition is under active discussion in the working group
    /// however: the Go SDK and the specification restructure both dropped it independently, and it may be
    /// removed from the SEP rather than added to implementations. Set this to <see langword="false"/> to
    /// emit the attributes on every negotiated version.
    /// </para>
    /// </remarks>
    public bool GateCacheAttributesByProtocolVersion { get; set; } = true;

    /// <summary>
    /// Gets or sets the freshness hint advertised on <c>skills/list</c> results, or <see langword="null"/>
    /// to advertise none.
    /// </summary>
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the cache scope advertised on <c>skills/list</c> results, or <see langword="null"/>
    /// to advertise none.
    /// </summary>
    /// <remarks>
    /// Set this to a public scope only when the catalog is identical for every caller. A listing that varies
    /// by principal must not be advertised as publicly cacheable.
    /// </remarks>
    public CacheScope? CacheScope { get; set; }
}
