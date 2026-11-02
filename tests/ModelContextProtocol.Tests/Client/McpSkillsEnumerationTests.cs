#pragma warning disable MCPEXP002

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

public class McpSkillsEnumerationTests : ClientServerTestBase
{
    private Func<int, JsonObject>? _page;
    private readonly List<string?> _cursors = [];

    public McpSkillsEnumerationTests(ITestOutputHelper output) : base(output)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder builder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            options.Capabilities.Extensions[SkillsProtocol.ExtensionId] = new JsonObject();
            options.RequestHandlers ??= [];
            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = SkillsProtocol.MethodSkillsList,
                Handler = (request, _) =>
                {
                    _cursors.Add(request.Params?["cursor"]?.GetValue<string>());
                    return new ValueTask<JsonNode?>(_page!(_cursors.Count));
                },
            });
        });
    }

    [Theory]
    [InlineData("repeat")]
    [InlineData("")]
    public async Task RepeatedCursorFailsInsteadOfLooping(string cursor)
    {
        _page = number => Page(cursor, $"skill-{number}");
        await using var client = await CreateMcpClientForServer();

        var error = await Assert.ThrowsAsync<McpException>(
            async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("repeated", error.Message);
        Assert.Equal(2, _cursors.Count);
    }

    [Fact]
    public async Task CursorCyclesAreRejectedEvenWhenTheRepeatIsNotAdjacent()
    {
        _page = number => Page(number == 2 ? "second" : "first", $"skill-{number}");
        await using var client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<McpException>(
            async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(3, _cursors.Count);
    }

    [Fact]
    public async Task EmptyOpaqueCursorIsUsedBeforeNullEndsTheListing()
    {
        _page = number => number == 1 ? Page("", "first") : Page(null, "second");
        await using var client = await CreateMcpClientForServer();

        var skills = await client.ListSkillsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "first", "second" }, skills.Select(skill => skill.Name));
        Assert.Equal(2, _cursors.Count);
        Assert.Null(_cursors[0]);
        Assert.Equal("", _cursors[1]);
    }

    [Fact]
    public async Task EverChangingCursorsStopAtTheRequestedPageLimit()
    {
        _page = number => Page($"cursor-{number}");
        await using var client = await CreateMcpClientForServer();

        var error = await Assert.ThrowsAsync<McpException>(
            async () => await client.ListSkillsAsync(3, 10, TestContext.Current.CancellationToken));

        Assert.Contains("3 pages", error.Message);
        Assert.Equal(3, _cursors.Count);
    }

    [Fact]
    public async Task DefaultPageLimitIsOneHundred()
    {
        _page = number => Page($"cursor-{number}");
        await using var client = await CreateMcpClientForServer();

        var error = await Assert.ThrowsAsync<McpException>(
            async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("100 pages", error.Message);
        Assert.Equal(100, _cursors.Count);
    }

    [Fact]
    public async Task EntryLimitFailsWithoutReturningATruncatedList()
    {
        _page = number => number == 1 ? Page("next", "a", "b") : Page(null, "c", "d");
        await using var client = await CreateMcpClientForServer();

        var error = await Assert.ThrowsAsync<McpException>(
            async () => await client.ListSkillsAsync(10, 3, TestContext.Current.CancellationToken));

        Assert.Contains("3 entries", error.Message);
        Assert.Equal(2, _cursors.Count);
    }

    [Fact]
    public async Task LastPageMayExactlyReachBothLimits()
    {
        _page = number => number == 1 ? Page("next", "a", "b") : Page(null, "c");
        await using var client = await CreateMcpClientForServer();

        var skills = await client.ListSkillsAsync(2, 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, skills.Count);
        Assert.Equal(2, _cursors.Count);
    }

    [Fact]
    public async Task DefaultEntryLimitIsOneThousandAndCanBeChangedExplicitly()
    {
        _page = _ => Page(null, Enumerable.Range(0, 1001).Select(index => $"skill-{index}").ToArray());
        await using var client = await CreateMcpClientForServer();

        var error = await Assert.ThrowsAsync<McpException>(
            async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        Assert.Contains("1000 entries", error.Message);

        var skills = await client.ListSkillsAsync(1, 1001, TestContext.Current.CancellationToken);
        Assert.Equal(1001, skills.Count);
        Assert.Equal(2, _cursors.Count);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task InvalidLimitsFailBeforeRequestingAPage(int maxPages, int maxSkills)
    {
        _page = _ => Page(null);
        await using var client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await client.ListSkillsAsync(maxPages, maxSkills, TestContext.Current.CancellationToken));
        Assert.Empty(_cursors);
    }

    [Fact]
    public async Task CancellationStopsBeforeRequestingAPage()
    {
        _page = _ => Page(null);
        await using var client = await CreateMcpClientForServer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.ListSkillsAsync(cancellation.Token));
        Assert.Empty(_cursors);
    }

    private JsonObject Page(string? nextCursor, params string[] names)
    {
        var entries = new JsonArray();
        foreach (var name in names)
        {
            entries.Add(new JsonObject
            {
                ["uri"] = $"skill://{name}/SKILL.md",
                ["frontmatter"] = new JsonObject { ["name"] = name, ["description"] = "d" },
                ["resources"] = "dynamic",
            });
        }
        return new JsonObject { ["skills"] = entries, ["nextCursor"] = nextCursor };
    }
}
