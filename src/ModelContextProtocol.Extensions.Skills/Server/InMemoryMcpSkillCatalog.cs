using System.Text;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// An <see cref="IMcpSkillCatalog"/> over a fixed set of skill entries held in memory.
/// </summary>
/// <remarks>
/// Entries are ordered by URI so that pagination is stable across calls. Cursors are keyset cursors over
/// that order rather than offsets, so entries added or removed between pages cannot cause a page to be
/// skipped or repeated wholesale.
/// </remarks>
public sealed class InMemoryMcpSkillCatalog : IMcpSkillCatalog
{
    private readonly List<SkillEntry> _ordered;
    private readonly Dictionary<string, SkillEntry> _byUri;
    private readonly int _pageSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMcpSkillCatalog"/> class.
    /// </summary>
    /// <param name="skills">The skills this catalog serves.</param>
    /// <param name="pageSize">
    /// The maximum number of entries returned per <see cref="ListAsync"/> call. Defaults to 50.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="skills"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageSize"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">Two skills share the same URI.</exception>
    public InMemoryMcpSkillCatalog(IEnumerable<SkillEntry> skills, int pageSize = 50)
    {
#if NET
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
#else
        if (skills is null) throw new ArgumentNullException(nameof(skills));
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
#endif

        _pageSize = pageSize;
        _byUri = new Dictionary<string, SkillEntry>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            if (_byUri.ContainsKey(skill.Uri))
            {
                throw new ArgumentException($"Duplicate skill URI '{skill.Uri}'.", nameof(skills));
            }

            _byUri.Add(skill.Uri, skill);
        }

        _ordered = [.. _byUri.Values];
        _ordered.Sort(static (left, right) => string.CompareOrdinal(left.Uri, right.Uri));
    }

    /// <inheritdoc />
    public ValueTask<McpSkillPage> ListAsync(string? cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? afterUri = DecodeCursor(cursor);
        int start = 0;
        if (afterUri is not null)
        {
            while (start < _ordered.Count &&
                   string.CompareOrdinal(_ordered[start].Uri, afterUri) <= 0)
            {
                start++;
            }
        }

        int count = Math.Min(_pageSize, _ordered.Count - start);
        if (count <= 0)
        {
            return new ValueTask<McpSkillPage>(McpSkillPage.Empty);
        }

        var page = _ordered.GetRange(start, count);
        bool hasMore = start + count < _ordered.Count;
        string? nextCursor = hasMore ? EncodeCursor(page[count - 1].Uri) : null;
        return new ValueTask<McpSkillPage>(new McpSkillPage(page, nextCursor));
    }

    /// <inheritdoc />
    public ValueTask<SkillEntry?> GetAsync(string uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _byUri.TryGetValue(uri, out var skill);
        return new ValueTask<SkillEntry?>(skill);
    }

    private string EncodeCursor(string uri) => Convert.ToBase64String(Encoding.UTF8.GetBytes(uri));

    private string? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(cursor!));
        }
        catch (FormatException)
        {
            throw new McpProtocolException($"Invalid cursor '{cursor}'.", McpErrorCode.InvalidParams);
        }
    }
}
