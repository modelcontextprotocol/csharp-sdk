using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Configures the behavior of the MCP Skills extension on a server.
/// </summary>
public sealed class McpSkillsOptions
{
    private readonly McpServerSkillCollection _skillCollection = new();
    private int _pageSize = 50;

    /// <summary>
    /// Gets the registered server-side skills, indexed by their exact URI text.
    /// </summary>
    /// <remarks>
    /// The default handlers read this live collection. Duplicate identities are rejected, and each primitive
    /// retains its local metadata. Conflicting shared-file manifests are rejected before addition, including
    /// later mutations. Files belonging to the initial collection are registered by <c>WithSkills</c>.
    /// Later collection changes affect Skills metadata only: register new file resources separately, and remove
    /// them explicitly when appropriate. Removing an entry does not revoke access to its resources.
    /// </remarks>
    public McpServerPrimitiveCollection<McpServerSkill> SkillCollection => _skillCollection;

    internal McpServerSkill[] GetSortedSkills() => _skillCollection.GetSortedSnapshot();

    /// <summary>
    /// Gets or sets the maximum number of registered skills returned by the default list handler.
    /// </summary>
    /// <remarks>
    /// Defaults to 50. Registered skills are ordered by exact URI text and paginated with keyset cursors.
    /// This setting does not apply to <see cref="ListSkillsHandler"/>.
    /// </remarks>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _pageSize = value;
        }
    }

    /// <summary>
    /// Gets or sets a handler that replaces the default listing of <see cref="SkillCollection"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler owns the complete page and cursor. Registered entries are not appended automatically.
    /// To combine sources, the application must define one pagination scheme and include the desired registered
    /// entries itself. Omitting an entry from a listing does not prevent direct retrieval. The handler may
    /// return caller-specific entries at registered identities; it owns agreement with the files it serves.
    /// Registered entries and manifests are not consulted to validate replacement content.
    /// </para>
    /// <para>
    /// Resolve application dependencies from the supplied context's <see cref="MessageContext.Services"/>.
    /// The SDK owns serialization, entry validation, and protocol-version gating of cache hints. A handler's
    /// cache hints override the defaults on these options. The handler must enforce per-caller visibility;
    /// the built-in resource authorization filters do not run for Skills requests.
    /// </para>
    /// </remarks>
    public McpRequestHandler<ListSkillsRequestParams, ListSkillsResult>? ListSkillsHandler { get; set; }

    /// <summary>
    /// Gets or sets a handler that replaces the default lookup in <see cref="SkillCollection"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler is responsible for every URI it serves, including registered skills and skills absent from
    /// listings. There is no automatic fallback. Throw <see cref="McpProtocolException"/> with
    /// <see cref="McpErrorCode.InvalidParams"/> when the caller cannot retrieve the requested skill.
    /// </para>
    /// <para>
    /// The context's <see cref="RequestContext{TParams}.MatchedPrimitive"/> identifies a registered skill at
    /// the requested URI, if present, even when this handler replaces lookup. It retains server-local metadata;
    /// that metadata is not serialized, does not constrain replacement content, and does not grant authorization automatically.
    /// Services, serialization, validation, caching, and authorization follow <see cref="ListSkillsHandler"/>.
    /// </para>
    /// </remarks>
    public McpRequestHandler<GetSkillRequestParams, GetSkillResult>? GetSkillHandler { get; set; }

    /// <summary>
    /// Gets or sets the freshness hint advertised on <c>skills/list</c> and <c>skills/get</c> results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the base protocol's <c>ttlMs</c> caching attribute, with the same semantics as on
    /// <c>tools/list</c>. It is a freshness hint about the returned listing or entry, not an integrity property
    /// of the skills' content.
    /// </para>
    /// <para>
    /// The attribute is defined from protocol revision <c>2026-07-28</c>. On a request negotiated under that
    /// revision or later, a <see langword="null"/> value is sent as <see cref="TimeSpan.Zero"/> (immediately stale),
    /// matching what the SDK does for the built-in list methods. On earlier revisions the attribute is not sent.
    /// </para>
    /// </remarks>
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the cache scope advertised on <c>skills/list</c> and <c>skills/get</c> results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set this to <see cref="Protocol.CacheScope.Public"/> only when the catalog is identical for every caller.
    /// A listing or entry that varies by principal must not be advertised as publicly cacheable.
    /// </para>
    /// <para>
    /// The attribute is defined from protocol revision <c>2026-07-28</c>. On a request negotiated under that
    /// revision or later, a <see langword="null"/> value is sent as <see cref="Protocol.CacheScope.Private"/>,
    /// matching what the SDK does for the built-in list methods. On earlier revisions the attribute is not sent.
    /// </para>
    /// </remarks>
    public CacheScope? CacheScope { get; set; }
}
