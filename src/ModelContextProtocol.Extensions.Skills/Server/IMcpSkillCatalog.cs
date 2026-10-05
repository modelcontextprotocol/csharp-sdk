namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Supplies the skills a server serves.
/// </summary>
/// <remarks>
/// <para>
/// A catalog is the single source of truth behind every view of a server's skills. Implementations back
/// <c>skills/list</c> through <see cref="ListAsync"/> and <c>skills/get</c> through <see cref="GetAsync"/>.
/// </para>
/// <para>
/// The two are deliberately separate. A server may enumerate only part of its catalog, or none of it, while
/// still answering for every skill it serves by URI.
/// </para>
/// </remarks>
public interface IMcpSkillCatalog
{
    /// <summary>
    /// Lists a page of the skills this catalog publishes.
    /// </summary>
    /// <param name="cursor">
    /// An opaque cursor from a previous call, or <see langword="null"/> to start at the first page.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A page of entries, and the cursor for the following page when more entries remain. A skill's manifest
    /// is never split across pages.
    /// </returns>
    /// <remarks>
    /// Returning an empty page is valid. Hosts must not treat an empty listing as proof that a server has
    /// no skills.
    /// </remarks>
    ValueTask<McpSkillPage> ListAsync(string? cursor, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the entry for a single skill by the URI of its <c>SKILL.md</c>.
    /// </summary>
    /// <param name="uri">The URI of the skill's <c>SKILL.md</c>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The skill's entry, or <see langword="null"/> if this catalog does not serve a skill at
    /// <paramref name="uri"/>.
    /// </returns>
    /// <remarks>
    /// This must answer for every skill the server serves, including skills omitted from
    /// <see cref="ListAsync"/>.
    /// </remarks>
    ValueTask<SkillEntry?> GetAsync(string uri, CancellationToken cancellationToken);
}
