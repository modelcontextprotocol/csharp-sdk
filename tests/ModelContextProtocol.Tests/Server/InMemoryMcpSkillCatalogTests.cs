using ModelContextProtocol.Extensions.Skills;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for <see cref="InMemoryMcpSkillCatalog"/>, covering ordering, keyset pagination, lookup of
/// unlisted skills, and cursor validation.
/// </summary>
public class InMemoryMcpSkillCatalogTests
{
    private SkillEntry CreateSkill(string name) => new()
    {
        Uri = $"skill://{name}/SKILL.md",
        Frontmatter = new JsonObject
        {
            ["name"] = name,
            ["description"] = $"The {name} skill",
        },
        Resources = SkillResources.Dynamic,
    };

    private InMemoryMcpSkillCatalog CreateCatalog(int pageSize, params string[] names) =>
        new([.. names.Select(CreateSkill)], pageSize);

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
    }

    [Fact]
    public async Task ListAsync_PaginatesWithoutRepeatingOrSkippingEntries()
    {
        var catalog = CreateCatalog(2, "a", "b", "c", "d", "e");

        var seen = new List<string>();
        string? cursor = null;
        int iterations = 0;
        do
        {
            var page = await catalog.ListAsync(cursor, TestContext.Current.CancellationToken);
            seen.AddRange(page.Skills.Select(skill => skill.Uri));
            cursor = page.NextCursor;
            iterations++;
            Assert.True(iterations < 20, "Pagination did not terminate.");
        }
        while (cursor is not null);

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

        Assert.Equal(2, second.Skills.Count);
        Assert.Null(second.NextCursor);
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
        Assert.Equal("alpha", skill.Frontmatter["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetAsync_WithUnknownUri_ReturnsNull()
    {
        var catalog = CreateCatalog(10, "alpha");

        var skill = await catalog.GetAsync("skill://missing/SKILL.md", TestContext.Current.CancellationToken);

        Assert.Null(skill);
    }

    [Fact]
    public async Task GetAsync_IsCaseSensitive()
    {
        var catalog = CreateCatalog(10, "alpha");

        var skill = await catalog.GetAsync("SKILL://ALPHA/SKILL.md", TestContext.Current.CancellationToken);

        Assert.Null(skill);
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
}
