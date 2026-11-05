#pragma warning disable MCPEXP002 // Raw request handlers are the point of this test.

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// The client extensions validate every entry a server returns against the specification's structural
/// requirements, since hosts must not load invalid entries. These tests stand up a server whose raw
/// <c>skills/list</c> and <c>skills/get</c> handlers return entries the SDK's own server side would never publish.
/// </summary>
public class McpSkillsClientValidationTests : ClientServerTestBase
{
    private JsonObject? _listResponse;
    private JsonObject? _getResponse;

    public McpSkillsClientValidationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
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
                    if (_listResponse is not null)
                    {
                        return new ValueTask<JsonNode?>(_listResponse.DeepClone());
                    }

                    var json = request.Params?["cursor"]?.GetValue<string>() switch
                    {
                        "empty-skills-array" => """{ "skills": [] }""",
                        _ => """
                            { "skills": [ { "uri": "skill://good/SKILL.md", "frontmatter": { "name": "good", "description": "d" },
                                            "resources": [ { "uri": "skill://good/SKILL.md", "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "size": 1 } ] },
                                          { "uri": "skill://bad/SKILL.md", "frontmatter": { "name": "mismatch", "description": "d" },
                                            "resources": [ { "uri": "skill://bad/SKILL.md", "digest": "not-a-digest", "size": 1 } ] } ] }
                            """,
                    };
                    return new ValueTask<JsonNode?>(JsonNode.Parse(json));
                },
            });
            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = SkillsProtocol.MethodSkillsGet,
                Handler = (request, _) =>
                {
                    if (_getResponse is not null)
                    {
                        return new ValueTask<JsonNode?>(_getResponse.DeepClone());
                    }

                    // Any request other than the escaping skill is answered with a valid entry for a different URI.
                    string? requested = request.Params?["uri"]?.GetValue<string>();
                    string json = requested == "skill://escape/SKILL.md"
                        ? """
                          { "skill": { "uri": "skill://escape/SKILL.md", "frontmatter": { "name": "escape", "description": "d" },
                                       "resources": [ { "uri": "skill://escape/SKILL.md", "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "size": 1 },
                                                      { "uri": "skill://escape/../secret.md", "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "size": 1 } ] } }
                          """
                        : """
                          { "skill": { "uri": "skill://good/SKILL.md", "frontmatter": { "name": "good", "description": "d" },
                                       "resources": [ { "uri": "skill://good/SKILL.md", "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "size": 1 } ] } }
                          """;
                    return new ValueTask<JsonNode?>(JsonNode.Parse(json));
                },
            });
        });
    }

    [Fact]
    public void SupportsSkillsChecksTheExtensionDeclaration()
    {
        var capabilities = new ServerCapabilities();
        Assert.False(capabilities.SupportsSkills());

        capabilities.Extensions = new Dictionary<string, object> { ["example.com/other"] = new JsonObject() };
        Assert.False(capabilities.SupportsSkills());

        capabilities.Extensions[SkillsProtocol.ExtensionId] = new JsonObject();
        Assert.True(capabilities.SupportsSkills());

        Assert.Throws<ArgumentNullException>("capabilities", () => McpSkillsClientExtensions.SupportsSkills(null!));
    }

    [Fact]
    public async Task ListSkillsAsync_RejectsAnInvalidEntry()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.ListSkillsAsync(new ListSkillsRequestParams(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("skills/list", exception.Message);
        Assert.Contains("skill://bad/SKILL.md", exception.Message);
    }

    [Theory]
    [InlineData("""{"skills":null}""")]
    [InlineData("{}")]
    public async Task ListSkillsAsync_RejectsAPageWithoutASkillsArray(string json)
    {
        _listResponse = JsonNode.Parse(json)!.AsObject();
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            async () => await client.ListSkillsAsync(new ListSkillsRequestParams(), cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            async () => await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListSkillsAsync_AcceptsAnEmptySkillsArray()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.ListSkillsAsync(new ListSkillsRequestParams { Cursor = "empty-skills-array" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Skills);
    }

    [Theory]
    [InlineData("\U00010428")]
    [InlineData("\U00020000")]
    public async Task ListSkillsAsync_AcceptsSupplementaryUnicodeNames(string name)
    {
        _listResponse = CreateListResponse(name);
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(name, Assert.Single(result).Name);
    }

    [Theory]
    [InlineData("café")]
    [InlineData("数据分析")]
    [InlineData("\U00010428")]
    public async Task ClientMethodsAcceptPercentEncodedUnicodeNameSegments(string name)
    {
        var uri = $"skill://catalog/{Uri.EscapeDataString(name)}/SKILL.md";
        _listResponse = CreateListResponse(name, uri: uri);
        var entry = _listResponse["skills"]![0]!;
        _getResponse = new JsonObject { ["skill"] = entry.DeepClone() };
        await using var client = await CreateMcpClientForServer();

        var listed = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var fetched = await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(uri, Assert.Single(listed).Uri);
        Assert.Equal(name, fetched.Name);
        Assert.Equal(uri, fetched.Uri);
    }

    [Theory]
    [InlineData("repo:/skills/alpha/SKILL.md")]
    [InlineData("repo:skills/alpha/SKILL.md")]
    [InlineData("repo:alpha/SKILL.md")]
    [InlineData("urn:alpha/SKILL.md")]
    public async Task ClientMethodsAcceptNativeAbsoluteResourceUris(string uri)
    {
        _listResponse = CreateListResponse("alpha", uri: uri);
        var entry = _listResponse["skills"]![0]!;
        _getResponse = new JsonObject { ["skill"] = entry.DeepClone() };
        await using var client = await CreateMcpClientForServer();

        var listed = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var fetched = await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(uri, Assert.Single(listed).Uri);
        Assert.Equal(uri, fetched.Uri);
    }

    [Theory]
    [InlineData("description", 1024, false)]
    [InlineData("description", 1024, true)]
    [InlineData("compatibility", 500, false)]
    [InlineData("compatibility", 500, true)]
    public async Task ClientMethodsUseScalarFrontmatterLimits(string field, int limit, bool overLimit)
    {
        int length = limit + (overLimit ? 1 : 0);
        var text = "a" + string.Concat(Enumerable.Repeat("\U0001f600", length - 1));
        _listResponse = CreateListResponse("alpha");
        var entry = _listResponse["skills"]![0]!.AsObject();
        entry["frontmatter"]![field] = text;
        _getResponse = new JsonObject { ["skill"] = entry.DeepClone() };
        var uri = entry["uri"]!.GetValue<string>();
        await using var client = await CreateMcpClientForServer();

        if (overLimit)
        {
            var listError = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await client.ListSkillsAsync(new ListSkillsRequestParams(), cancellationToken: TestContext.Current.CancellationToken));
            var getError = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains($"{length} characters", listError.Message);
            Assert.Contains($"{length} characters", getError.Message);
        }
        else
        {
            var listed = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);
            var fetched = await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(text, Assert.Single(listed).Frontmatter[field]!.GetValue<string>());
            Assert.Equal(text, fetched.Frontmatter[field]!.GetValue<string>());
        }

    }

    [Fact]
    public async Task ClientMethodsPreserveEntryMetaSeparatelyFromFrontmatterAndPageMeta()
    {
        _listResponse = CreateListResponse("alpha", new JsonObject { ["version"] = "2.1" });
        var entry = _listResponse["skills"]![0]!.AsObject();
        var meta = JsonNode.Parse("""{"example.com/extension":{"items":[{"value":"original"}]}}""")!.AsObject();
        entry["_meta"] = meta.DeepClone();
        _listResponse["_meta"] = new JsonObject { ["scope"] = "page" };
        _getResponse = new JsonObject { ["skill"] = entry.DeepClone() };
        var uri = entry["uri"]!.GetValue<string>();
        await using var client = await CreateMcpClientForServer();

        var page = await client.ListSkillsAsync(new ListSkillsRequestParams(), cancellationToken: TestContext.Current.CancellationToken);
        var listed = Assert.Single(page.Skills);
        Assert.True(JsonNode.DeepEquals(meta, listed.Meta));
        Assert.Equal("page", page.Meta!["scope"]!.GetValue<string>());
        Assert.Equal("2.1", listed.Frontmatter["metadata"]!["version"]!.GetValue<string>());
        listed.Meta!["example.com/extension"]!["items"]![0]!["value"] = "changed";

        var fetched = await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(JsonNode.DeepEquals(meta, fetched.Meta));
    }

    [Fact]
    public async Task ListSkillsAsync_RejectsTitlecaseNames()
    {
        _listResponse = CreateListResponse("\u01c5");
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.ListSkillsAsync(new ListSkillsRequestParams(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task ListSkillsAsync_RejectsNonStringMetadataValues(string value)
    {
        _listResponse = CreateListResponse("alpha", new JsonObject { ["value"] = JsonNode.Parse(value) });
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.ListSkillsAsync(new ListSkillsRequestParams(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("metadata", exception.Message);
    }

    [Fact]
    public async Task ListSkillsAsync_PreservesStringMetadataAndStructuredExtensionFields()
    {
        _listResponse = CreateListResponse("alpha", new JsonObject { ["version"] = "2.1" });
        var frontmatter = _listResponse["skills"]![0]!["frontmatter"]!.AsObject();
        frontmatter["custom"] = new JsonObject { ["count"] = 1, ["values"] = new JsonArray(true) };
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(JsonNode.DeepEquals(frontmatter, Assert.Single(result).Frontmatter));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{}")]
    public async Task ListingRejectsMalformedEntriesWithoutReturningPartialSuccess(string malformed)
    {
        _listResponse = CreateListResponse("alpha");
        var entries = _listResponse["skills"]!.AsArray();
        entries.Add(JsonNode.Parse(malformed));
        _listResponse["nextCursor"] = "next-page";
        _listResponse["ttlMs"] = 3000;
        _listResponse["cacheScope"] = "public";
        _listResponse["resultType"] = "complete";
        await using var client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            async () => await client.ListSkillsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            async () => await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task GetSkillAsync_RejectsAnExplicitNullSkill()
    {
        _getResponse = new JsonObject { ["skill"] = null };
        await using var client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            async () => await client.GetSkillAsync("skill://alpha/SKILL.md", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("allowed-tools", "[\"Read\",\"Write\"]")]
    [InlineData("allowed-tools", "1")]
    [InlineData("allowed-tools", "{\"Read\":true}")]
    [InlineData("allowed-tools", "true")]
    [InlineData("allowed-tools", "null")]
    [InlineData("license", "[\"MIT\"]")]
    [InlineData("license", "2")]
    [InlineData("license", "{\"name\":\"MIT\"}")]
    [InlineData("license", "false")]
    [InlineData("license", "null")]
    public async Task ClientMethodsRejectSpecFieldsThatAreNotStrings(string field, string valueJson)
    {
        _listResponse = CreateListResponse("alpha");
        var entry = _listResponse["skills"]![0]!.AsObject();
        entry["frontmatter"]![field] = JsonNode.Parse(valueJson);
        _getResponse = new JsonObject { ["skill"] = entry.DeepClone() };
        var uri = entry["uri"]!.GetValue<string>();
        await using var client = await CreateMcpClientForServer();

        var listError = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.ListSkillsAsync(new ListSkillsRequestParams(), cancellationToken: TestContext.Current.CancellationToken));
        var getError = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken));

        var expected = $"'{field}' frontmatter field that is not a string";
        Assert.Contains(expected, listError.Message);
        Assert.Contains(expected, getError.Message);
    }

    [Fact]
    public async Task GetSkillAsync_RejectsAValidEntryForADifferentUri()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // The entry itself is valid; it is just not the skill that was asked for.
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.GetSkillAsync("skill://other/SKILL.md", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("skill://other/SKILL.md", exception.Message);
        Assert.Contains("skill://good/SKILL.md", exception.Message);

        // Asking for the URI the server actually returns succeeds.
        var good = await client.GetSkillAsync("skill://good/SKILL.md", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("good", good.Name);
    }

    [Fact]
    public async Task GetSkillAsync_RejectsAnEntryWhoseManifestEscapesTheSkill()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.GetSkillAsync("skill://escape/SKILL.md", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("skills/get", exception.Message);
        Assert.Contains("'..'", exception.Message);
    }

    private JsonObject CreateListResponse(string name, JsonObject? metadata = null, string? uri = null)
    {
        uri ??= $"skill://catalog/{name}/SKILL.md";
        var frontmatter = new JsonObject { ["name"] = name, ["description"] = "d" };
        if (metadata is not null)
        {
            frontmatter["metadata"] = metadata;
        }

        return new JsonObject
        {
            ["skills"] = new JsonArray(new JsonObject
            {
                ["uri"] = uri,
                ["frontmatter"] = frontmatter,
                ["resources"] = new JsonArray(new JsonObject
                {
                    ["uri"] = uri,
                    ["digest"] = "sha256:" + new string('a', 64),
                    ["size"] = 1,
                }),
            }),
        };
    }
}
