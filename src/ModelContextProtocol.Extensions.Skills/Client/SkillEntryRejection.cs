namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Describes one entry rejected while reading a diagnostic listing page.
/// </summary>
public sealed class SkillEntryRejection
{
    internal SkillEntryRejection(int index, Exception error)
    {
        Index = index;
        Error = error;
    }

    /// <summary>
    /// Gets the zero-based position in the server's original page.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the parsing or structural-validation failure. No rejected entry is returned as a valid skill.
    /// </summary>
    public Exception Error { get; }
}
