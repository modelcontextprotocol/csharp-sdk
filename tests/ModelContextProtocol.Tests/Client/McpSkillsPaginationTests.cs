#pragma warning disable MCPEXP002

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

public class McpSkillsPaginationTests(ITestOutputHelper output) : ClientServerTestBase(output)
{
    private Func<JsonObject> _page = () => Page("next", "alpha");
    private readonly List<string?> _cursors = [];

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder builder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities ??= new();
            options.Capabilities.Extensions = new Dictionary<string, object> { [SkillsProtocol.ExtensionId] = new JsonObject() };
            options.RequestHandlers =
            [
                new McpServerRequestHandler
                {
                    Method = SkillsProtocol.MethodSkillsList,
                    Handler = (context, _) =>
                    {
                        _cursors.Add(context.Params?["cursor"]?.GetValue<string>());
                        return new ValueTask<JsonNode?>(_page());
                    },
                },
            ];
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("opaque-cursor")]
    public async Task EachCallRequestsOnePageAndPreservesOpaqueCursorsAndHints(string? cursor)
    {
        _page = () => Page(cursor, "alpha");
        await using var client = await CreateMcpClientForServer();

        var page = await client.ListSkillsAsync(
            new ListSkillsRequestParams { Cursor = cursor }, TestContext.Current.CancellationToken);

        Assert.Equal(cursor, Assert.Single(_cursors));
        Assert.Equal(cursor, page.NextCursor);
        Assert.Equal("alpha", Assert.Single(page.Skills).Name);
        Assert.Equal(TimeSpan.FromMinutes(1), page.TimeToLive);
        Assert.Equal(CacheScope.Public, page.CacheScope);
    }

    [Fact]
    public async Task AggregationFollowsOpaqueCursorsUntilNull()
    {
        _page = () => _cursors.Count switch
        {
            1 => Page("", "alpha"),
            2 => Page("opaque-cursor"),
            3 => Page("opaque-cursor", "beta"),
            _ => Page(null, "gamma"),
        };

        await using var client = await CreateMcpClientForServer();

        var skills = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["alpha", "beta", "gamma"], skills.Select(skill => skill.Name));
        Assert.Equal([null, "", "opaque-cursor", "opaque-cursor"], _cursors);
    }

    [Fact]
    public async Task AggregationRejectsAnInvalidLaterPage()
    {
        _page = () => _cursors.Count == 1 ? Page("next", "alpha") : Page(null, "Bad");
        await using var client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, _cursors.Count);
    }

    private static JsonObject Page(string? nextCursor, params string[] names)
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

        return new JsonObject
        {
            ["skills"] = entries, ["nextCursor"] = nextCursor,
            ["resultType"] = "complete", ["ttlMs"] = 60_000, ["cacheScope"] = "public",
        };
    }
}
