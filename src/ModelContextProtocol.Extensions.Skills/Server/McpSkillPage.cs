namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents one page of skill entries returned by <see cref="IMcpSkillCatalog.ListAsync"/>.
/// </summary>
public sealed class McpSkillPage
{
    /// <summary>
    /// Gets an empty page with no following page.
    /// </summary>
    public static McpSkillPage Empty { get; } = new() { Skills = [] };

    /// <summary>
    /// Gets the entries in this page.
    /// </summary>
    public required IReadOnlyList<Skill> Skills { get; init; }

    /// <summary>
    /// Gets the cursor to pass back for the following page, or <see langword="null"/> when no entries remain.
    /// </summary>
    public string? NextCursor { get; init; }
}
