using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// End-to-end tests for skills served through a custom <see cref="IMcpSkillCatalog"/>: pagination, skills that
/// are served but not listed, dynamic skills, the error contract, and the protocol-version gating of
/// <c>resultType</c>, <c>ttlMs</c>, and <c>cacheScope</c> (issue #1721).
/// </summary>
public class McpServerSkillsCatalogTests : ClientServerTestBase
{
    private const string TamperedUri = "skill://tampered/SKILL.md";
    private const string UnlistedUri = "skill://hidden/SKILL.md";
    private const string DynamicUri = "skill://generated/SKILL.md";
    private const string BrokenUri = "skill://broken/SKILL.md";
    private const string SwappedUri = "skill://swapped/SKILL.md";

    public McpServerSkillsCatalogTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        var listed = new[] { "alpha", "beta", "gamma" }.Select(name => CreateEntry($"skill://{name}/SKILL.md", name)).ToList();

        // The manifest declares one content, the resource serves another.
        var tampered = CreateEntry(TamperedUri, "tampered", Encoding.UTF8.GetBytes("the content the host approved"));

        // Served and answerable through skills/get, but withheld from skills/list.
        var unlisted = CreateEntry(UnlistedUri, "hidden");

        var dynamic = new Skill
        {
            Uri = DynamicUri,
            Frontmatter = new JsonObject { ["name"] = "generated", ["description"] = "Assembled from live data" },
            Resources = SkillResources.Dynamic,
        };

        // A catalog bug: an entry whose manifest omits its own SKILL.md. The handler must not publish it.
        var broken = new Skill
        {
            Uri = BrokenUri,
            Frontmatter = new JsonObject { ["name"] = "broken", ["description"] = "d" },
            Resources = SkillResources.FromResources([new SkillResource { Uri = "skill://broken/other.md", Digest = "sha256:" + new string('a', 64), Size = 1 }]),
        };

        var catalog = new PartialCatalog(
            listed: new InMemoryMcpSkillCatalog([.. listed, tampered, dynamic], pageSize: 2),
            unlisted: unlisted,
            broken: broken);

        mcpServerBuilder
            .WithSkills(catalog, options =>
            {
                options.TimeToLive = TimeSpan.FromMinutes(5);
                options.CacheScope = CacheScope.Public;
            })
            .WithResources([McpServerResource.Create(() => "the content the server actually serves", new() { UriTemplate = TamperedUri })]);
    }

    private static Skill CreateEntry(string uri, string name, byte[]? content = null)
    {
        content ??= Encoding.UTF8.GetBytes($"---\nname: {name}\n---\n");
        return new Skill
        {
            Uri = uri,
            Frontmatter = new JsonObject { ["name"] = name, ["description"] = $"The {name} skill" },
            Resources = SkillResources.FromResources([new SkillResource { Uri = uri, Digest = SkillVerifier.ComputeDigest(content), Size = content.Length }]),
        };
    }

    [Fact]
    public async Task ListSkillsAsync_FollowsPaginationToTheEnd()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var skills = await client.ListSkillsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, skills.Count);
        Assert.Equal(skills.Select(s => s.Uri).OrderBy(u => u, StringComparer.Ordinal), skills.Select(s => s.Uri));
        Assert.DoesNotContain(skills, s => s.Uri == UnlistedUri);
    }

    [Fact]
    public async Task ListSkillsAsync_PerPage_ExposesCursorAndCacheHints()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var first = await client.ListSkillsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Skills.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(TimeSpan.FromMinutes(5), first.TimeToLive);
        Assert.Equal(CacheScope.Public, first.CacheScope);

        var second = await client.ListSkillsAsync(new ListSkillsRequestParams { Cursor = first.NextCursor }, TestContext.Current.CancellationToken);
        Assert.Equal(2, second.Skills.Count);
        Assert.NotEqual(first.Skills[0].Uri, second.Skills[0].Uri);
    }

    [Fact]
    public async Task GetSkillAsync_AnswersForSkillAbsentFromListing()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var skill = await client.GetSkillAsync(UnlistedUri, TestContext.Current.CancellationToken);

        Assert.Equal("hidden", skill.Name);
    }

    [Fact]
    public async Task SkillsGet_WithCatalogAnsweringForADifferentUri_ReturnsInternalError()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(SwappedUri, TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InternalError, exception.ErrorCode);
        Assert.Contains(SwappedUri, exception.Message);
    }

    [Fact]
    public async Task SkillsGet_WithInvalidCatalogEntry_ReturnsInternalError()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(BrokenUri, TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InternalError, exception.ErrorCode);
        Assert.Contains("invalid entry", exception.Message);
    }

    [Fact]
    public async Task DynamicSkill_IsListedWithTheDynamicMarker()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var skills = await client.ListSkillsAsync(TestContext.Current.CancellationToken);
        var dynamic = Assert.Single(skills, s => s.Uri == DynamicUri);

        Assert.True(dynamic.Resources.IsDynamic);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.ReadSkillResourceAsync(dynamic, DynamicUri, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadSkillResourceAsync_RejectsContentThatDoesNotMatchTheManifest()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var skill = await client.GetSkillAsync(TamperedUri, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<SkillVerificationException>(
            async () => await client.ReadSkillResourceAsync(skill, TamperedUri, TestContext.Current.CancellationToken));

        Assert.Contains(TamperedUri, exception.Message);

        // The unverified read itself still works through the base API; only the verified path refuses it.
        var raw = await client.ReadResourceAsync(TamperedUri, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(raw.Contents);
    }

    [Theory]
    [InlineData("""{ "uri": 42 }""")]
    [InlineData("""{ }""")]
    [InlineData("""{ "uri": "" }""")]
    public async Task SkillsGet_WithInvalidParams_ReturnsInvalidParams(string paramsJson)
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.SendRequestAsync(
            new JsonRpcRequest { Method = SkillsProtocol.MethodSkillsGet, Params = JsonNode.Parse(paramsJson) },
            TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Theory]
    [InlineData("""{ "cursor": "not-base64!!" }""")]
    [InlineData("""{ "cursor": 42 }""")]
    public async Task SkillsList_WithInvalidCursor_ReturnsInvalidParams(string paramsJson)
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.SendRequestAsync(
            new JsonRpcRequest { Method = SkillsProtocol.MethodSkillsList, Params = JsonNode.Parse(paramsJson) },
            TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public async Task SkillsList_WithoutParams_Succeeds()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = SkillsProtocol.MethodSkillsList },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Result!["skills"]!.AsArray().Count);
    }

    [Fact]
    public async Task SkillsMethods_On2026_07_28Session_IncludeResultTypeAndCacheHints()
    {
        await using McpClient client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);

        var list = (await client.SendRequestAsync(
            new JsonRpcRequest { Method = SkillsProtocol.MethodSkillsList },
            TestContext.Current.CancellationToken)).Result!.AsObject();
        Assert.Equal("complete", list["resultType"]?.GetValue<string>());
        Assert.Equal(300_000, list["ttlMs"]?.GetValue<long>());
        Assert.Equal("public", list["cacheScope"]?.GetValue<string>());

        var get = (await client.SendRequestAsync(
            new JsonRpcRequest { Method = SkillsProtocol.MethodSkillsGet, Params = new JsonObject { ["uri"] = UnlistedUri } },
            TestContext.Current.CancellationToken)).Result!.AsObject();
        Assert.Equal("complete", get["resultType"]?.GetValue<string>());
        Assert.False(get.ContainsKey("nextCursor"));
        Assert.False(get.ContainsKey("ttlMs"));
    }

    [Fact]
    public async Task SkillsMethods_On2025_11_25Session_OmitResultTypeAndCacheHints()
    {
        await using McpClient client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);

        var list = (await client.SendRequestAsync(
            new JsonRpcRequest { Method = SkillsProtocol.MethodSkillsList },
            TestContext.Current.CancellationToken)).Result!.AsObject();
        Assert.False(list.ContainsKey("resultType"), "resultType must be absent on a 2025-11-25 skills/list result.");
        Assert.False(list.ContainsKey("ttlMs"), "ttlMs must be absent on a 2025-11-25 skills/list result.");
        Assert.False(list.ContainsKey("cacheScope"), "cacheScope must be absent on a 2025-11-25 skills/list result.");
        Assert.Equal(2, list["skills"]!.AsArray().Count);

        var get = (await client.SendRequestAsync(
            new JsonRpcRequest { Method = SkillsProtocol.MethodSkillsGet, Params = new JsonObject { ["uri"] = UnlistedUri } },
            TestContext.Current.CancellationToken)).Result!.AsObject();
        Assert.False(get.ContainsKey("resultType"), "resultType must be absent on a 2025-11-25 skills/get result.");
        Assert.Equal(UnlistedUri, get["skill"]!["uri"]!.GetValue<string>());
    }

    /// <summary>
    /// A catalog that lists some skills and serves one more by URI only.
    /// </summary>
    private sealed class PartialCatalog(InMemoryMcpSkillCatalog listed, Skill unlisted, Skill broken) : IMcpSkillCatalog
    {
        public ValueTask<McpSkillPage> ListAsync(string? cursor, McpSkillRequestContext context, CancellationToken cancellationToken)
        {
            AssertContext(context, SkillsProtocol.MethodSkillsList);
            return listed.ListAsync(cursor, context, cancellationToken);
        }

        public async ValueTask<Skill?> GetAsync(string uri, McpSkillRequestContext context, CancellationToken cancellationToken)
        {
            AssertContext(context, SkillsProtocol.MethodSkillsGet);
            if (string.Equals(uri, unlisted.Uri, StringComparison.Ordinal))
            {
                return unlisted;
            }

            if (string.Equals(uri, broken.Uri, StringComparison.Ordinal))
            {
                return broken;
            }

            if (string.Equals(uri, SwappedUri, StringComparison.Ordinal))
            {
                // A catalog bug: a valid entry, for the wrong skill.
                return await listed.GetAsync("skill://alpha/SKILL.md", context, cancellationToken);
            }

            return await listed.GetAsync(uri, context, cancellationToken);
        }

        // The catalog receives the request it is answering, so a per-caller catalog can decide from it.
        private static void AssertContext(McpSkillRequestContext context, string method)
        {
            Assert.Equal(method, context.JsonRpcRequest.Method);
            Assert.Null(context.User);
        }
    }
}
