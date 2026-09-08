using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Serialization and deserialization tests for the SEP-2640 skills protocol types, with particular attention to
/// the array-or-<c>"dynamic"</c> union used for a skill's manifest.
/// </summary>
public class SkillSerializationTests
{
    private static Skill CreateEntry(SkillResources resources) => new()
    {
        Uri = "skill://git-workflow/SKILL.md",
        Frontmatter = new JsonObject
        {
            ["name"] = "git-workflow",
            ["description"] = "Follow this team's Git conventions",
        },
        Resources = resources,
    };

    private static SkillResource CreateResource(string uri, string digest, long size) => new()
    {
        Uri = uri,
        Digest = digest,
        Size = size,
    };

    [Fact]
    public void Skill_WithEnumeratedResources_RoundTrips()
    {
        var original = CreateEntry(SkillResources.FromResources(
        [
            CreateResource("skill://git-workflow/SKILL.md", "sha256:" + new string('a', 64), 2314),
            CreateResource("skill://git-workflow/examples/email.md", "sha256:" + new string('b', 64), 962),
        ]));

        string json = JsonSerializer.Serialize(original, McpSkillsJsonContext.Default.Skill);
        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.Skill);

        Assert.NotNull(deserialized);
        Assert.Equal("skill://git-workflow/SKILL.md", deserialized.Uri);
        Assert.Equal("git-workflow", deserialized.Name);
        Assert.Equal("Follow this team's Git conventions", deserialized.Description);
        Assert.False(deserialized.Resources.IsDynamic);

        var resources = deserialized.Resources.Resources;
        Assert.NotNull(resources);
        Assert.Equal(2, resources.Count);
        Assert.Equal("skill://git-workflow/SKILL.md", resources[0].Uri);
        Assert.Equal(2314, resources[0].Size);
        Assert.Equal("sha256:" + new string('b', 64), resources[1].Digest);
    }

    [Fact]
    public void Skill_WithDynamicResources_SerializesAsTheDynamicString()
    {
        string json = JsonSerializer.Serialize(CreateEntry(SkillResources.Dynamic), McpSkillsJsonContext.Default.Skill);

        var node = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("dynamic", node["resources"]?.GetValue<string>());
    }

    [Fact]
    public void Skill_WithDynamicResources_RoundTrips()
    {
        string json = JsonSerializer.Serialize(CreateEntry(SkillResources.Dynamic), McpSkillsJsonContext.Default.Skill);
        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.Skill);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Resources.IsDynamic);
        Assert.Null(deserialized.Resources.Resources);
    }

    [Fact]
    public void Skill_WithEmptyResourceArray_RoundTripsAsNonDynamic()
    {
        string json = JsonSerializer.Serialize(CreateEntry(SkillResources.FromResources([])), McpSkillsJsonContext.Default.Skill);
        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.Skill);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.Resources.IsDynamic);
        Assert.Empty(deserialized.Resources.Resources!);
    }

    [Fact]
    public void Skill_PreservesArbitraryFrontmatterFields()
    {
        const string Json = """
            {
              "uri": "skill://refunds/SKILL.md",
              "frontmatter": {
                "name": "refunds",
                "description": "Process refunds",
                "license": "Apache-2.0",
                "metadata": { "version": "2.1.0", "owner": "billing" },
                "allowed-tools": "Bash(git:*)"
              },
              "resources": "dynamic"
            }
            """;

        var skill = JsonSerializer.Deserialize(Json, McpSkillsJsonContext.Default.Skill);

        Assert.NotNull(skill);
        Assert.Equal("Apache-2.0", skill.Frontmatter["license"]?.GetValue<string>());
        Assert.Equal("2.1.0", skill.Frontmatter["metadata"]?["version"]?.GetValue<string>());
        Assert.Equal("Bash(git:*)", skill.Frontmatter["allowed-tools"]?.GetValue<string>());

        string reserialized = JsonSerializer.Serialize(skill, McpSkillsJsonContext.Default.Skill);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Json), JsonNode.Parse(reserialized)));
    }

    [Fact]
    public void Skill_NameAndDescription_AreNullWhenAbsentOrNotStrings()
    {
        var skill = new Skill
        {
            Uri = "skill://x/SKILL.md",
            Frontmatter = new JsonObject { ["name"] = 42 },
            Resources = SkillResources.Dynamic,
        };

        Assert.Null(skill.Name);
        Assert.Null(skill.Description);
    }

    [Theory]
    [InlineData("\"static\"")]
    [InlineData("\"Dynamic\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[null]")]
    public void Skill_WithInvalidResourcesValue_Throws(string resourcesJson)
    {
        string json = $$"""
            {
              "uri": "skill://git-workflow/SKILL.md",
              "frontmatter": { "name": "git-workflow", "description": "d" },
              "resources": {{resourcesJson}}
            }
            """;

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.Skill));
    }

    [Fact]
    public void Skill_WithoutResources_Throws()
    {
        const string Json = """
            {
              "uri": "skill://git-workflow/SKILL.md",
              "frontmatter": { "name": "git-workflow", "description": "d" }
            }
            """;

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(Json, McpSkillsJsonContext.Default.Skill));
    }

    [Fact]
    public void SkillResources_FromResources_CopiesTheSource()
    {
        var mutable = new List<SkillResource>
        {
            CreateResource("skill://a/SKILL.md", "sha256:" + new string('c', 64), 1),
        };

        var resources = SkillResources.FromResources(mutable);
        mutable.Add(CreateResource("skill://a/extra.md", "sha256:" + new string('d', 64), 2));

        Assert.Single(resources.Resources!);
    }

    [Fact]
    public void SkillResources_FromResources_WithNull_Throws() =>
        Assert.Throws<ArgumentNullException>(() => SkillResources.FromResources(null!));

    [Fact]
    public void ListSkillsResult_RoundTripsCursorAndCacheAttributes()
    {
        var original = new ListSkillsResult
        {
            Skills = [CreateEntry(SkillResources.Dynamic)],
            NextCursor = "cursor-1",
            ResultType = "complete",
            TimeToLive = TimeSpan.FromMinutes(5),
            CacheScope = CacheScope.Public,
        };

        string json = JsonSerializer.Serialize(original, McpSkillsJsonContext.Default.ListSkillsResult);
        var node = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(300_000, node["ttlMs"]?.GetValue<long>());
        Assert.Equal("public", node["cacheScope"]?.GetValue<string>());

        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.ListSkillsResult);
        Assert.NotNull(deserialized);
        Assert.Equal("cursor-1", deserialized.NextCursor);
        Assert.Equal("complete", deserialized.ResultType);
        Assert.Equal(TimeSpan.FromMinutes(5), deserialized.TimeToLive);
        Assert.Equal(CacheScope.Public, deserialized.CacheScope);
        Assert.Single(deserialized.Skills);
    }

    [Fact]
    public void ListSkillsResult_ToleratesUnknownCacheScopeOnRead()
    {
        const string Json = """{ "skills": [], "ttlMs": 0, "cacheScope": "regional" }""";

        var deserialized = JsonSerializer.Deserialize(Json, McpSkillsJsonContext.Default.ListSkillsResult);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.CacheScope);
        Assert.Equal(TimeSpan.Zero, deserialized.TimeToLive);
    }

    [Fact]
    public void ListSkillsResult_OmitsOptionalFieldsWhenUnset()
    {
        string json = JsonSerializer.Serialize(new ListSkillsResult(), McpSkillsJsonContext.Default.ListSkillsResult);

        var node = JsonNode.Parse(json)!.AsObject();
        Assert.False(node.ContainsKey("ttlMs"));
        Assert.False(node.ContainsKey("cacheScope"));
        Assert.False(node.ContainsKey("nextCursor"));
        Assert.False(node.ContainsKey("resultType"));
        Assert.Empty(node["skills"]!.AsArray());
    }

    [Fact]
    public void GetSkillResult_RoundTrips()
    {
        var original = new GetSkillResult
        {
            Skill = CreateEntry(SkillResources.FromResources(
            [
                CreateResource("skill://git-workflow/SKILL.md", "sha256:" + new string('e', 64), 10),
            ])),
            ResultType = "complete",
        };

        string json = JsonSerializer.Serialize(original, McpSkillsJsonContext.Default.GetSkillResult);
        Assert.False(JsonNode.Parse(json)!.AsObject().ContainsKey("nextCursor"));

        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.GetSkillResult);
        Assert.NotNull(deserialized);
        Assert.Equal("skill://git-workflow/SKILL.md", deserialized.Skill.Uri);
        Assert.Equal("complete", deserialized.ResultType);
    }

    [Fact]
    public void RequestParams_RoundTrip()
    {
        string listJson = JsonSerializer.Serialize(new ListSkillsRequestParams { Cursor = "abc" }, McpSkillsJsonContext.Default.ListSkillsRequestParams);
        Assert.Equal("abc", JsonSerializer.Deserialize(listJson, McpSkillsJsonContext.Default.ListSkillsRequestParams)?.Cursor);

        string getJson = JsonSerializer.Serialize(new GetSkillRequestParams { Uri = "skill://a/SKILL.md" }, McpSkillsJsonContext.Default.GetSkillRequestParams);
        Assert.Equal("skill://a/SKILL.md", JsonSerializer.Deserialize(getJson, McpSkillsJsonContext.Default.GetSkillRequestParams)?.Uri);
    }
}
