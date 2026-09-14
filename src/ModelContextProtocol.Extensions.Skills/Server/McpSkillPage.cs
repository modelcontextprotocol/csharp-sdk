namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents one page of skill entries returned by <see cref="IMcpSkillCatalog.ListAsync"/>.
/// </summary>
/// <param name="Skills">The entries in this page.</param>
/// <param name="NextCursor">
/// The cursor to pass back for the following page, or <see langword="null"/> when no entries remain.
/// </param>
public sealed record McpSkillPage(IReadOnlyList<SkillEntry> Skills, string? NextCursor = null)
{
    /// <summary>
    /// Gets an empty page with no following page.
    /// </summary>
    public static McpSkillPage Empty { get; } = new([]);
}
