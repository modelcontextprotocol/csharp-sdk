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
/// </remarks>
public interface IMcpSkillCatalog
{
    /// <summary>
    /// Lists a page of the skills this catalog publishes.
    /// </summary>
    /// <param name="cursor">
    /// An opaque cursor returned by a previous call, or <see langword="null"/> to start at the first page.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>
    /// A page of entries, and the cursor for the following page when more entries remain. A skill's manifest is
    /// never split across pages.
    /// </returns>
    /// <remarks>
    /// Returning an empty page is valid. Hosts must not treat an empty listing as proof that a server has no skills.
    /// Throw <see cref="McpProtocolException"/> with <see cref="McpErrorCode.InvalidParams"/> for a cursor that
    /// this catalog did not issue.
    /// </remarks>
    ValueTask<McpSkillPage> ListAsync(string? cursor, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the entry for a single skill by the URI of its <c>SKILL.md</c>.
    /// </summary>
    /// <param name="uri">The URI of the skill's <c>SKILL.md</c>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>
    /// The skill's entry, or <see langword="null"/> if this catalog does not serve a skill at <paramref name="uri"/>.
    /// </returns>
    /// <remarks>
    /// This must answer for every skill the server serves, including skills omitted from <see cref="ListAsync"/>.
    /// </remarks>
    ValueTask<Skill?> GetAsync(string uri, CancellationToken cancellationToken);
}
