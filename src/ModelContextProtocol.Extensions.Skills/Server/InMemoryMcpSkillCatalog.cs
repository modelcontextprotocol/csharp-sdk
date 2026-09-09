using System.Text;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// An <see cref="IMcpSkillCatalog"/> over a fixed set of skill entries held in memory.
/// </summary>
/// <remarks>
/// <para>
/// Every entry is validated against the specification's structural requirements when the catalog is constructed,
/// so a server cannot publish an entry a conforming host would refuse to load.
/// </para>
/// <para>
/// The catalog keeps its own copy of every entry, so later changes to the objects passed to the constructor do
/// not affect what is served. Entries are ordered by URI so that pagination is stable across calls. Cursors are
/// keyset cursors over that order rather than offsets.
/// </para>
/// <para>
/// Every caller sees the same entries. For a catalog whose contents depend on the caller, implement
/// <see cref="IMcpSkillCatalog"/> directly and consult <see cref="McpSkillRequestContext.User"/>.
/// </para>
/// </remarks>
public sealed class InMemoryMcpSkillCatalog : IMcpSkillCatalog
{
    private readonly Skill[] _ordered;
    private readonly string[] _orderedUris;
    private readonly Dictionary<string, Skill> _byUri;
    private readonly int _pageSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMcpSkillCatalog"/> class.
    /// </summary>
    /// <param name="skills">The skills this catalog serves.</param>
    /// <param name="pageSize">The maximum number of entries returned per <see cref="ListAsync"/> call. Defaults to 50.</param>
    /// <exception cref="ArgumentNullException"><paramref name="skills"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageSize"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// Two skills share the same URI, or an entry violates the specification's structural requirements (for example,
    /// its name does not match its URI, its manifest omits its own <c>SKILL.md</c>, or a digest is malformed).
    /// </exception>
    public InMemoryMcpSkillCatalog(IEnumerable<Skill> skills, int pageSize = 50)
    {
#if NET
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
#else
        if (skills is null) throw new ArgumentNullException(nameof(skills));
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
#endif

        _pageSize = pageSize;
        _byUri = new Dictionary<string, Skill>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            SkillValidation.Validate(skill, nameof(skills));

            if (_byUri.ContainsKey(skill.Uri))
            {
                throw new ArgumentException($"Duplicate skill URI '{skill.Uri}'.", nameof(skills));
            }

            _byUri.Add(skill.Uri, SkillValidation.Snapshot(skill));
        }

        _ordered = [.. _byUri.Values];
        Array.Sort(_ordered, static (left, right) => string.CompareOrdinal(left.Uri, right.Uri));
        _orderedUris = Array.ConvertAll(_ordered, static skill => skill.Uri);
    }

    /// <summary>
    /// Gets the number of skills in this catalog.
    /// </summary>
    public int Count => _ordered.Length;

    /// <inheritdoc />
    public ValueTask<McpSkillPage> ListAsync(string? cursor, McpSkillRequestContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int start = 0;
        if (DecodeCursor(cursor) is { } afterUri)
        {
            int index = Array.BinarySearch(_orderedUris, afterUri, StringComparer.Ordinal);
            start = index >= 0 ? index + 1 : ~index;
        }

        int count = Math.Min(_pageSize, _ordered.Length - start);
        if (count <= 0)
        {
            return new ValueTask<McpSkillPage>(McpSkillPage.Empty);
        }

        var page = new Skill[count];
        Array.Copy(_ordered, start, page, 0, count);

        bool hasMore = start + count < _ordered.Length;
        return new ValueTask<McpSkillPage>(new McpSkillPage
        {
            Skills = page,
            NextCursor = hasMore ? EncodeCursor(page[count - 1].Uri) : null,
        });
    }

    /// <inheritdoc />
    public ValueTask<Skill?> GetAsync(string uri, McpSkillRequestContext context, CancellationToken cancellationToken)
    {
#if NET
        ArgumentNullException.ThrowIfNull(uri);
#else
        if (uri is null) throw new ArgumentNullException(nameof(uri));
#endif

        cancellationToken.ThrowIfCancellationRequested();

        _byUri.TryGetValue(uri, out var skill);
        return new ValueTask<Skill?>(skill);
    }

    private static string EncodeCursor(string uri) => Convert.ToBase64String(Encoding.UTF8.GetBytes(uri));

    private static string? DecodeCursor(string? cursor)
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
