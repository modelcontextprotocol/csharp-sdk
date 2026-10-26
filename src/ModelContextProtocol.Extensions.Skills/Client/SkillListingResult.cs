namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// A client-side listing page with valid entries and explicit per-entry failures.
/// </summary>
public sealed class SkillListingResult
{
    internal SkillListingResult(ListSkillsResult page, IReadOnlyList<SkillEntryRejection> rejectedEntries)
    {
        Page = page;
        RejectedEntries = rejectedEntries;
    }

    /// <summary>
    /// Gets the valid entries and the server's pagination and cache hints.
    /// </summary>
    public ListSkillsResult Page { get; }

    /// <summary>
    /// Gets the rejected entries. Callers must inspect these even when <see cref="Page"/> contains no skills.
    /// </summary>
    public IReadOnlyList<SkillEntryRejection> RejectedEntries { get; }
}
