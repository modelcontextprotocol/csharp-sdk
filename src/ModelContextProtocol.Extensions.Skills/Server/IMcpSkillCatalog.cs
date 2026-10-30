using ModelContextProtocol.Server;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Supplies the skills a server serves.
/// </summary>
/// <remarks>
/// <para>
/// A catalog is the source of truth behind a server's <c>skills/list</c> and <c>skills/get</c> methods.
/// <see cref="InMemoryMcpSkillCatalog"/> serves a fixed set of entries; implement this interface directly when
/// skills come from a database, a file share, or another source that should not be loaded up front.
/// </para>
/// <para>
/// The two methods are deliberately independent. A server may enumerate only part of its catalog, or none of
/// it, while still answering for every skill it serves by URI.
/// </para>
/// <para>
/// A catalog is responsible only for the entries. The skills' files must additionally be served as ordinary
/// resources through <c>resources/read</c>, since that is how hosts fetch skill content.
/// </para>
/// <para>
/// Each entry's <see cref="Skill.Frontmatter"/> must be identical to the frontmatter of the <c>SKILL.md</c> it
/// describes. The server validates an entry's structure but does not read the files to check this; building
/// entries with <see cref="McpServerSkill"/> is one way to keep the two in agreement.
/// </para>
/// <para>
/// The skills methods do not pass through the request filters that guard the built-in resource methods, so a
/// catalog whose skills are not visible to every caller must decide what to return from the
/// <see cref="RequestContext{TParams}"/> it receives, and the corresponding resources must be guarded the same way.
/// </para>
/// <para>
/// Register a catalog with a dependency injection lifetime appropriate for its dependencies, or resolve
/// request-scoped dependencies from <see cref="MessageContext.Services"/> on the
/// supplied context. An externally supplied catalog instance or a singleton catalog is shared across requests
/// and must not capture scoped services. The request infrastructure owns any request scope and disposes it
/// asynchronously; a catalog must not dispose or retain services resolved from that scope.
/// </para>
/// </remarks>
public interface IMcpSkillCatalog
{
    /// <summary>
    /// Lists a page of the skills this catalog publishes.
    /// </summary>
    /// <param name="context">
    /// The request being answered, including the pagination cursor in <c>context.Params.Cursor</c> and the
    /// caller's identity when the transport supplies one. A null cursor starts at the first page.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>
    /// A page of entries, and the cursor for the following page when more entries remain. A skill's manifest is
    /// never split across pages.
    /// </returns>
    /// <remarks>
    /// Returning an empty page is valid. Hosts must not treat an empty listing as proof that a server has no skills.
    /// Throw <see cref="McpProtocolException"/> with <see cref="McpErrorCode.InvalidParams"/> for a cursor this
    /// catalog cannot interpret. A cursor is an opaque position, not a capability: a catalog may resume from a
    /// well-formed cursor it does not recognize, as <see cref="InMemoryMcpSkillCatalog"/> does.
    /// </remarks>
    ValueTask<McpSkillPage> ListAsync(RequestContext<ListSkillsRequestParams> context, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the entry for a single skill by the URI of its <c>SKILL.md</c>.
    /// </summary>
    /// <param name="context">
    /// The request being answered, including the skill URI in <c>context.Params.Uri</c> and the caller's identity
    /// when the transport supplies one.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>
    /// The skill's entry, or <see langword="null"/> if this catalog does not serve a skill at the requested URI to this caller.
    /// </returns>
    /// <remarks>
    /// This must answer for every skill the server serves to the caller, including skills omitted from <see cref="ListAsync"/>.
    /// </remarks>
    ValueTask<Skill?> GetAsync(RequestContext<GetSkillRequestParams> context, CancellationToken cancellationToken);
}
