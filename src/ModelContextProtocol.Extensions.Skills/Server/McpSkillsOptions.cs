using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Configures the behavior of the MCP Skills extension on a server.
/// </summary>
public sealed class McpSkillsOptions
{
    /// <summary>
    /// Gets or sets the freshness hint advertised on <c>skills/list</c> results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the base protocol's <c>ttlMs</c> list-caching attribute, with the same semantics as on
    /// <c>tools/list</c>. It is a hint about the listing, not an integrity property of the skills' content.
    /// </para>
    /// <para>
    /// The attribute is defined from protocol revision <c>2026-07-28</c>. On a request negotiated under that
    /// revision or later, a <see langword="null"/> value is sent as <see cref="TimeSpan.Zero"/> (immediately stale),
    /// matching what the SDK does for the built-in list methods. On earlier revisions the attribute is not sent.
    /// </para>
    /// </remarks>
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the cache scope advertised on <c>skills/list</c> results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set this to <see cref="Protocol.CacheScope.Public"/> only when the catalog is identical for every caller.
    /// A listing that varies by principal must not be advertised as publicly cacheable.
    /// </para>
    /// <para>
    /// The attribute is defined from protocol revision <c>2026-07-28</c>. On a request negotiated under that
    /// revision or later, a <see langword="null"/> value is sent as <see cref="Protocol.CacheScope.Private"/>,
    /// matching what the SDK does for the built-in list methods. On earlier revisions the attribute is not sent.
    /// </para>
    /// </remarks>
    public CacheScope? CacheScope { get; set; }
}
