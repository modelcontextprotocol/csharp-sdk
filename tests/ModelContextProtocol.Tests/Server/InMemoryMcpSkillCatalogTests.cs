using ModelContextProtocol.Extensions.Skills;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for <see cref="InMemoryMcpSkillCatalog"/>: ordering, keyset pagination, lookup, cursor validation, and
/// the structural validation of entries against the specification.
/// </summary>
public class InMemoryMcpSkillCatalogTests
{
    private static readonly string s_validDigest = "sha256:" + new string('a', 64);

    private static Skill CreateSkill(string name, string? description = "A skill", SkillResources? resources = null)
    {
        string uri = $"skill://{name}/SKILL.md";
        return new()
        {
            Uri = uri,
            Frontmatter = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
            },
            Resources = resources ?? SkillResources.FromResources([new SkillResource { Uri = uri, Digest = s_validDigest, Size = 10 }]),
        };
    }

    private static InMemoryMcpSkillCatalog CreateCatalog(int pageSize, params string[] names) =>
        new([.. names.Select(name => CreateSkill(name))], pageSize);

    [Fact]
    public async Task ListAsync_ReturnsEntriesOrderedByUri()
    {
        var catalog = CreateCatalog(10, "zebra", "alpha", "middle");

        var page = await catalog.ListAsync(null, TestContext.Current.CancellationToken);

        Assert.Collection(
            page.Skills,
            skill => Assert.Equal("skill://alpha/SKILL.md", skill.Uri),
            skill => Assert.Equal("skill://middle/SKILL.md", skill.Uri),
            skill => Assert.Equal("skill://zebra/SKILL.md", skill.Uri));
        Assert.Null(page.NextCursor);
        Assert.Equal(3, catalog.Count);
    }

    [Fact]
    public async Task ListAsync_PaginatesWithoutRepeatingOrSkippingEntries()
    {
        var catalog = CreateCatalog(2, "a", "b", "c", "d", "e");

        var seen = new List<string>();
        string? cursor = null;
        int pages = 0;
        do
        {
            var page = await catalog.ListAsync(cursor, TestContext.Current.CancellationToken);
            seen.AddRange(page.Skills.Select(skill => skill.Uri));
            cursor = page.NextCursor;
            Assert.True(++pages < 20, "Pagination did not terminate.");
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(5, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(seen.OrderBy(uri => uri, StringComparer.Ordinal), seen);
    }

    [Fact]
    public async Task ListAsync_LastPageHasNoNextCursor()
    {
        var catalog = CreateCatalog(2, "a", "b", "c", "d");

        var first = await catalog.ListAsync(null, TestContext.Current.CancellationToken);
        var second = await catalog.ListAsync(first.NextCursor, TestContext.Current.CancellationToken);

        Assert.NotNull(first.NextCursor);
        Assert.Equal(2, second.Skills.Count);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task ListAsync_WithCursorForUnknownUri_ResumesAfterItsPosition()
    {
        var catalog = CreateCatalog(10, "a", "c");
        string cursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("skill://b/SKILL.md"));

        var page = await catalog.ListAsync(cursor, TestContext.Current.CancellationToken);

        Assert.Single(page.Skills);
        Assert.Equal("skill://c/SKILL.md", page.Skills[0].Uri);
    }

    [Fact]
    public async Task ListAsync_WithEmptyCatalog_ReturnsEmptyPage()
    {
        var catalog = CreateCatalog(10);

        var page = await catalog.ListAsync(null, TestContext.Current.CancellationToken);

        Assert.Empty(page.Skills);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task ListAsync_WithMalformedCursor_ThrowsInvalidParams()
    {
        var catalog = CreateCatalog(10, "a");

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await catalog.ListAsync("not-base64!!", TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_ReturnsSkillByUri()
    {
        var catalog = CreateCatalog(10, "alpha");

        var skill = await catalog.GetAsync("skill://alpha/SKILL.md", TestContext.Current.CancellationToken);

        Assert.NotNull(skill);
        Assert.Equal("alpha", skill.Name);
    }

    [Theory]
    [InlineData("skill://missing/SKILL.md")]
    [InlineData("SKILL://ALPHA/SKILL.md")]
    [InlineData("skill://alpha")]
    public async Task GetAsync_WithUnknownUri_ReturnsNull(string uri)
    {
        var catalog = CreateCatalog(10, "alpha");

        Assert.Null(await catalog.GetAsync(uri, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_WithDuplicateUris_Throws()
    {
        var duplicate = new[] { CreateSkill("alpha"), CreateSkill("alpha") };

        Assert.Throws<ArgumentException>(() => new InMemoryMcpSkillCatalog(duplicate));
    }

    [Fact]
    public void Constructor_WithNullSkills_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemoryMcpSkillCatalog(null!));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositivePageSize_Throws(int pageSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryMcpSkillCatalog([], pageSize));

    [Fact]
    public void Constructor_AcceptsDynamicSkill()
    {
        var catalog = new InMemoryMcpSkillCatalog([CreateSkill("generated", resources: SkillResources.Dynamic)]);

        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public void Constructor_AcceptsNestedSkillPath()
    {
        var skill = CreateSkill("refunds");
        skill.Uri = "skill://acme/billing/refunds/SKILL.md";
        skill.Resources = SkillResources.FromResources([new SkillResource { Uri = skill.Uri, Digest = s_validDigest, Size = 1 }]);

        var catalog = new InMemoryMcpSkillCatalog([skill]);

        Assert.Equal(1, catalog.Count);
    }

    public static IEnumerable<object[]> InvalidSkills()
    {
        static object[] Case(string reason, Action<Skill> mutate)
        {
            var skill = CreateSkill("alpha");
            mutate(skill);
            return [reason, skill];
        }

        yield return Case("uri not ending in /SKILL.md", s => s.Uri = "skill://alpha/skill.md");
        yield return Case("name missing", s => s.Frontmatter.Remove("name"));
        yield return Case("name not a string", s => s.Frontmatter["name"] = 1);
        yield return Case("name does not match uri segment", s => s.Frontmatter["name"] = "beta");
        yield return Case("name violates naming rules", s =>
        {
            s.Uri = "skill://Alpha/SKILL.md";
            s.Frontmatter["name"] = "Alpha";
            s.Resources = SkillResources.FromResources([new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 }]);
        });
        yield return Case("description missing", s => s.Frontmatter.Remove("description"));
        yield return Case("description empty", s => s.Frontmatter["description"] = "");
        yield return Case("empty manifest", s => s.Resources = SkillResources.FromResources([]));
        yield return Case("manifest omits SKILL.md", s => s.Resources = SkillResources.FromResources(
            [new SkillResource { Uri = "skill://alpha/other.md", Digest = s_validDigest, Size = 1 }]));
        yield return Case("manifest lists file outside the skill", s => s.Resources = SkillResources.FromResources(
        [
            new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 },
            new SkillResource { Uri = "skill://alphabet/x.md", Digest = s_validDigest, Size = 1 },
        ]));
        yield return Case("duplicate manifest entry", s => s.Resources = SkillResources.FromResources(
        [
            new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 },
            new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 },
        ]));
        yield return Case("malformed digest (uppercase)", s => s.Resources = SkillResources.FromResources(
            [new SkillResource { Uri = s.Uri, Digest = "sha256:" + new string('A', 64), Size = 1 }]));
        yield return Case("malformed digest (wrong length)", s => s.Resources = SkillResources.FromResources(
            [new SkillResource { Uri = s.Uri, Digest = "sha256:abc", Size = 1 }]));
        yield return Case("malformed digest (wrong algorithm)", s => s.Resources = SkillResources.FromResources(
            [new SkillResource { Uri = s.Uri, Digest = "sha512:" + new string('a', 64), Size = 1 }]));
        yield return Case("negative size", s => s.Resources = SkillResources.FromResources(
            [new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = -1 }]));
        yield return Case("too many resources", s => s.Resources = SkillResources.FromResources(
            Enumerable.Range(0, SkillsProtocol.MaxResourcesPerSkill + 1).Select(i => new SkillResource
            {
                Uri = i == 0 ? s.Uri : $"skill://alpha/f{i}.md",
                Digest = s_validDigest,
                Size = 1,
            })));
        yield return Case("total size over limit", s => s.Resources = SkillResources.FromResources(
        [
            new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 },
            new SkillResource { Uri = "skill://alpha/big.bin", Digest = s_validDigest, Size = SkillsProtocol.MaxTotalSizeBytes },
        ]));
    }

    [Theory]
    [MemberData(nameof(InvalidSkills))]
    public void Constructor_RejectsInvalidSkill(string reason, Skill skill)
    {
        var exception = Assert.Throws<ArgumentException>(() => new InMemoryMcpSkillCatalog([skill]));

        Assert.Contains("skill://", exception.Message);
        Assert.NotEmpty(reason);
    }
}
