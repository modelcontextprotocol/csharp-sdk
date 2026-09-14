using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Serialization and deserialization tests for the SEP-2640 skills protocol types, with particular
/// attention to the array-or-<c>"dynamic"</c> union used for a skill's manifest.
/// </summary>
public class SkillSerializationTests
{
    private SkillEntry CreateEntry(SkillResources resources) => new()
    {
        Uri = "skill://git-workflow/SKILL.md",
        Frontmatter = new JsonObject
        {
            ["name"] = "git-workflow",
            ["description"] = "Follow this team's Git conventions",
        },
        Resources = resources,
    };

    private SkillResource CreateResource(string uri, string digest, long size) => new()
    {
        Uri = uri,
        Digest = digest,
        Size = size,
    };

    [Fact]
    public void SkillEntry_WithEnumeratedResources_RoundTrips()
    {
        var original = CreateEntry(SkillResources.FromResources(
        [
            CreateResource("skill://git-workflow/SKILL.md", "sha256:" + new string('a', 64), 2314),
            CreateResource("skill://git-workflow/examples/email.md", "sha256:" + new string('b', 64), 962),
        ]));

        string json = JsonSerializer.Serialize(original, McpSkillsJsonContext.Default.SkillEntry);
        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.SkillEntry);

        Assert.NotNull(deserialized);
        Assert.Equal("skill://git-workflow/SKILL.md", deserialized.Uri);
        Assert.Equal("git-workflow", deserialized.Frontmatter["name"]?.GetValue<string>());
        Assert.False(deserialized.Resources.IsDynamic);

        var resources = deserialized.Resources.Resources;
        Assert.NotNull(resources);
        Assert.Equal(2, resources.Count);
        Assert.Equal("skill://git-workflow/SKILL.md", resources[0].Uri);
        Assert.Equal(2314, resources[0].Size);
        Assert.Equal("sha256:" + new string('b', 64), resources[1].Digest);
    }

    [Fact]
    public void SkillEntry_WithDynamicResources_SerializesAsTheDynamicString()
    {
        var original = CreateEntry(SkillResources.Dynamic);

        string json = JsonSerializer.Serialize(original, McpSkillsJsonContext.Default.SkillEntry);

        var node = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("dynamic", node["resources"]?.GetValue<string>());
    }

    [Fact]
    public void SkillEntry_WithDynamicResources_RoundTrips()
    {
        var original = CreateEntry(SkillResources.Dynamic);

        string json = JsonSerializer.Serialize(original, McpSkillsJsonContext.Default.SkillEntry);
        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.SkillEntry);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Resources.IsDynamic);
        Assert.Null(deserialized.Resources.Resources);
    }

    [Fact]
    public void SkillEntry_WithEmptyResourceArray_RoundTripsAsNonDynamic()
    {
        var original = CreateEntry(SkillResources.FromResources([]));

        string json = JsonSerializer.Serialize(original, McpSkillsJsonContext.Default.SkillEntry);
        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.SkillEntry);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.Resources.IsDynamic);
        Assert.Empty(deserialized.Resources.Resources!);
    }

    [Theory]
    [InlineData("\"static\"")]
    [InlineData("\"Dynamic\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("{}")]
    public void SkillEntry_WithInvalidResourcesValue_Throws(string resourcesJson)
    {
        string json = $$"""
            {
              "uri": "skill://git-workflow/SKILL.md",
              "frontmatter": { "name": "git-workflow", "description": "d" },
              "resources": {{resourcesJson}}
            }
            """;

        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.SkillEntry));
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
        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.ListSkillsResult);

        Assert.NotNull(deserialized);
        Assert.Equal("cursor-1", deserialized.NextCursor);
        Assert.Equal("complete", deserialized.ResultType);
        Assert.Equal(TimeSpan.FromMinutes(5), deserialized.TimeToLive);
        Assert.Equal(CacheScope.Public, deserialized.CacheScope);
        Assert.Single(deserialized.Skills);
    }

    [Fact]
    public void ListSkillsResult_WritesCacheScopeUsingSpecifiedWireValues()
    {
        var result = new ListSkillsResult { CacheScope = CacheScope.Private };

        string json = JsonSerializer.Serialize(result, McpSkillsJsonContext.Default.ListSkillsResult);

        Assert.Equal("private", JsonNode.Parse(json)!["cacheScope"]?.GetValue<string>());
    }

    [Fact]
    public void ListSkillsResult_OmitsCacheAttributesWhenUnset()
    {
        var result = new ListSkillsResult { Skills = [] };

        string json = JsonSerializer.Serialize(result, McpSkillsJsonContext.Default.ListSkillsResult);

        var node = JsonNode.Parse(json)!.AsObject();
        Assert.False(node.ContainsKey("ttlMs"));
        Assert.False(node.ContainsKey("cacheScope"));
        Assert.False(node.ContainsKey("nextCursor"));
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
        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.GetSkillResult);

        Assert.NotNull(deserialized);
        Assert.Equal("skill://git-workflow/SKILL.md", deserialized.Skill.Uri);
        Assert.Equal("complete", deserialized.ResultType);
    }

    [Fact]
    public void GetSkillResult_HasNoPaginationCursor()
    {
        var result = new GetSkillResult { Skill = CreateEntry(SkillResources.Dynamic) };

        string json = JsonSerializer.Serialize(result, McpSkillsJsonContext.Default.GetSkillResult);

        Assert.False(JsonNode.Parse(json)!.AsObject().ContainsKey("nextCursor"));
    }

    [Fact]
    public void ListSkillsRequestParams_RoundTripsCursor()
    {
        var original = new ListSkillsRequestParams { Cursor = "abc" };

        string json = JsonSerializer.Serialize(original, McpSkillsJsonContext.Default.ListSkillsRequestParams);
        var deserialized = JsonSerializer.Deserialize(json, McpSkillsJsonContext.Default.ListSkillsRequestParams);

        Assert.NotNull(deserialized);
        Assert.Equal("abc", deserialized.Cursor);
    }
}
