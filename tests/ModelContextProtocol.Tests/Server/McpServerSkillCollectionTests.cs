using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests skill creation, validation, and collection identity.
/// </summary>
public class McpServerSkillCollectionTests
{
    private static readonly string s_validDigest = "sha256:" + new string('a', 64);

    // Static because the InvalidSkills member data, which xUnit requires to be static, builds its cases with it.
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

    [Theory]
    [InlineData("skill://catalog/alpha/SKILL.md", "SKILL://CATALOG/alpha/SKILL.md")]
    [InlineData("skill://catalog/alpha/SKILL.md", "skill://catalog/%61lpha/SKILL.md")]
    [InlineData("repo:/skills/alpha/SKILL.md", "REPO:/skills/alpha/SKILL.md")]
    public void CollectionUsesResourceEquivalentIdentitiesForLookupAndDuplicateDetection(string uri, string alias)
    {
        var entry = CreateSkill("alpha", resources: SkillResources.Dynamic);
        entry.Uri = uri;
        var skill = McpServerSkill.Create(entry);
        entry.Uri = alias;
        var equivalent = McpServerSkill.Create(entry);
        var collection = new McpServerSkillCollection { skill };
        var resources = new McpServerResourceCollection
        {
            McpServerResource.Create(() => "content", new() { UriTemplate = uri }),
        };

        Assert.True(resources.TryGetPrimitive(alias, out _));
        Assert.True(collection.TryGetPrimitive(alias, out var found));
        Assert.Same(skill, found);
        Assert.Same(skill, collection[alias]);
        Assert.False(collection.TryAdd(equivalent));
        Assert.Throws<ArgumentException>(() => collection.Add(equivalent));
        Assert.Same(skill, Assert.Single(collection));
        Assert.Equal(alias, skill.ProtocolSkill!.Uri);
        Assert.True(collection.Remove(skill));
        Assert.False(collection.TryGetPrimitive(alias, out _));
    }

    [Theory]
    [InlineData("skill://catalog/Alpha/SKILL.md")]
    [InlineData("skill://other/alpha/SKILL.md")]
    [InlineData("skill://catalog/alpha/SKILL.md?query")]
    [InlineData("skill://catalog/alpha/SKILL.md#fragment")]
    [InlineData("skill://catalog/other/../alpha/SKILL.md")]
    [InlineData("skill://catalog/other/%2e%2e/alpha/SKILL.md")]
    [InlineData("skill://catalog//alpha/SKILL.md")]
    [InlineData("skill://catalog/other%2f../alpha/SKILL.md")]
    [InlineData("skill://[invalid]/alpha/SKILL.md")]
    [InlineData("alpha/SKILL.md")]
    public void CollectionDoesNotMatchDifferentOrUnsupportedUriShapes(string uri)
    {
        var entry = CreateSkill("alpha", resources: SkillResources.Dynamic);
        entry.Uri = "skill://catalog/alpha/SKILL.md";
        var collection = new McpServerSkillCollection { McpServerSkill.Create(entry) };

        Assert.False(collection.TryGetPrimitive(uri, out var found));
        Assert.Null(found);
    }

    [Theory]
    [InlineData("café")]
    [InlineData("数据分析")]
    [InlineData("données-2024")]
    [InlineData("\U00010428")]
    [InlineData("\U00020000")]
    public void Create_AcceptsUnicodeLowercaseNames(string name)
    {
        Assert.Equal(name, McpServerSkill.Create(CreateSkill(name)).ProtocolSkill!.Name);
    }

    [Theory]
    [InlineData("café")]
    [InlineData("数据分析")]
    [InlineData("\U00010428")]
    public void Create_AcceptsPercentEncodedUnicodeNameSegments(string name)
    {
        var skill = CreateSkill(name);
        skill.Uri = $"skill://catalog/{Uri.EscapeDataString(name)}/SKILL.md";
        skill.Resources = SkillResources.FromResources([new SkillResource { Uri = skill.Uri, Digest = s_validDigest, Size = 1 }]);

        Assert.Equal(skill.Uri, McpServerSkill.Create(skill).Id);
    }

    [Theory]
    [InlineData("caf%25C3%25A9")]
    [InlineData("%2e%2e")]
    [InlineData("ca%2ffé")]
    [InlineData("ca%5cfé")]
    public void Create_DoesNotDecodeNamesRepeatedlyOrAllowEncodedTraversal(string segment)
    {
        var skill = CreateSkill("café");
        skill.Uri = $"skill://catalog/{segment}/SKILL.md";
        skill.Resources = SkillResources.FromResources([new SkillResource { Uri = skill.Uri, Digest = s_validDigest, Size = 1 }]);

        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(skill));
    }

    [Theory]
    [InlineData("\u01c5")]
    [InlineData("\U00010400")]
    public void Create_RejectsUppercaseOrTitlecaseNames(string name)
    {
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(CreateSkill(name)));
    }

    [Theory]
    [InlineData(0xd801)]
    [InlineData(0xdc28)]
    public void Create_RejectsUnpairedSurrogates(int codeUnit)
    {
        var name = new string((char)codeUnit, 1);

        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(CreateSkill(name)));
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void Create_CountsNameLengthByUnicodeScalar(int length, bool valid)
    {
        var name = string.Concat(Enumerable.Repeat("\U00010428", length));
        var skill = CreateSkill(name);
        skill.Uri = $"skill://catalog/{name}/SKILL.md";
        skill.Resources = SkillResources.FromResources([new SkillResource { Uri = skill.Uri, Digest = s_validDigest, Size = 10 }]);

        if (valid)
        {
            Assert.Equal(skill.Uri, McpServerSkill.Create(skill).Id);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => McpServerSkill.Create(skill));
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    public void Create_RejectsNonStringMetadataValues(string value)
    {
        var skill = CreateSkill("alpha");
        skill.Frontmatter["metadata"] = new JsonObject { ["value"] = JsonNode.Parse(value) };

        var exception = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(skill));

        Assert.Contains("metadata", exception.Message);
    }

    [Fact]
    public void Create_AcceptsFullAgentSkillsFrontmatter()
    {
        var skill = CreateSkill("alpha");
        skill.Frontmatter["license"] = "MIT";
        skill.Frontmatter["compatibility"] = "Needs git";
        skill.Frontmatter["allowed-tools"] = "Bash(git:*) Read";
        skill.Frontmatter["metadata"] = new JsonObject { ["author"] = "acme", ["version"] = "2.1.0" };
        skill.Frontmatter["description"] = new string('d', 1024);

        Assert.True(JsonNode.DeepEquals(skill.Frontmatter, McpServerSkill.Create(skill).ProtocolSkill!.Frontmatter));
    }

    [Theory]
    [InlineData("description", 1024, false)]
    [InlineData("description", 1024, true)]
    [InlineData("compatibility", 500, false)]
    [InlineData("compatibility", 500, true)]
    public void Create_UsesScalarCountsAtFrontmatterBoundaries(string field, int limit, bool mixed)
    {
        var text = (mixed ? "a" : "\U0001f600") +
            string.Concat(Enumerable.Repeat("\U0001f600", limit - 1));
        var skill = CreateSkill("alpha");
        skill.Frontmatter[field] = text;

        Assert.Equal(skill.Uri, McpServerSkill.Create(skill).Id);

        skill.Frontmatter[field] = text + "a";
        var error = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(skill));
        Assert.Contains($"{limit + 1} characters", error.Message);
    }

    [Theory]
    [InlineData("description", 1024)]
    [InlineData("compatibility", 500)]
    public void Create_CountsCombiningMarksSeparately(string field, int limit)
    {
        var skill = CreateSkill("alpha");
        var text = string.Concat(Enumerable.Repeat("e\u0301", limit / 2));
        skill.Frontmatter[field] = text;
        Assert.Equal(skill.Uri, McpServerSkill.Create(skill).Id);

        skill.Frontmatter[field] = text + "a";
        var error = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(skill));
        Assert.Contains($"{limit + 1} characters", error.Message);
    }

    [Fact]
    public void Create_PassesThroughFieldsHostsDoNotActOn()
    {
        // Unknown fields keep structured values. Fields the Agent Skills specification defines are type-checked, and the
        // known metadata map has its own string-value requirement.
        var skill = CreateSkill("alpha");
        skill.Frontmatter["allowed-tools"] = "Read Write";
        skill.Frontmatter["license"] = "Apache-2.0";
        skill.Frontmatter["custom"] = new JsonObject { ["version"] = 2.1, ["tags"] = new JsonArray("a") };

        Assert.True(JsonNode.DeepEquals(skill.Frontmatter, McpServerSkill.Create(skill).ProtocolSkill!.Frontmatter));

        skill.Frontmatter["allowed-tools"] = new JsonArray("Read", "Write");
        var exception = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(skill));
        Assert.Contains("'allowed-tools' frontmatter field that is not a string", exception.Message);
    }

    [Fact]
    public void Create_AcceptsEmptyAuthority()
    {
        var skill = CreateSkill("alpha");
        skill.Uri = "file:///alpha/SKILL.md";
        skill.Resources = SkillResources.FromResources([new SkillResource { Uri = skill.Uri, Digest = s_validDigest, Size = 1 }]);

        Assert.Equal(skill.Uri, McpServerSkill.Create(skill).Id);
    }

    [Fact]
    public void Create_AcceptsDynamicSkill()
    {
        var primitive = McpServerSkill.Create(CreateSkill("generated", resources: SkillResources.Dynamic));
        Assert.True(primitive.ProtocolSkill!.Resources.IsDynamic);
    }

    [Fact]
    public void Create_AcceptsNestedSkillPath()
    {
        var skill = CreateSkill("refunds");
        skill.Uri = "skill://acme/billing/refunds/SKILL.md";
        skill.Resources = SkillResources.FromResources([new SkillResource { Uri = skill.Uri, Digest = s_validDigest, Size = 1 }]);

        Assert.Equal(skill.Uri, McpServerSkill.Create(skill).Id);
    }

    [Theory]
    [InlineData("repo:/skills/alpha/SKILL.md")]
    [InlineData("repo:skills/alpha/SKILL.md")]
    [InlineData("repo:alpha/SKILL.md")]
    [InlineData("urn:alpha/SKILL.md")]
    public void Create_AcceptsNativeAbsoluteResourceUris(string uri)
    {
        var skill = CreateSkill("alpha");
        skill.Uri = uri;
        skill.Resources = SkillResources.FromResources([new SkillResource { Uri = uri, Digest = s_validDigest, Size = 1 }]);

        Assert.Equal(uri, McpServerSkill.Create(skill).Id);
    }

    [Theory]
    [InlineData("repo:/skills//alpha/SKILL.md")]
    [InlineData("repo:skills/../alpha/SKILL.md")]
    [InlineData("repo:skills/%2e%2e/alpha/SKILL.md")]
    [InlineData("repo:skills/ca%2ffé/alpha/SKILL.md")]
    [InlineData("repo:/skills/alpha/SKILL.md?query")]
    [InlineData("repo:/skills/alpha/SKILL.md#fragment")]
    public void Create_RejectsUnsafeNativeUriPaths(string uri)
    {
        var skill = CreateSkill("alpha");
        skill.Uri = uri;
        skill.Resources = SkillResources.FromResources([new SkillResource { Uri = uri, Digest = s_validDigest, Size = 1 }]);

        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(skill));
    }

    [Theory]
    [InlineData("repo:/skills/alpha/SKILL.md")]
    [InlineData("repo:alpha/SKILL.md")]
    public void Create_RejectsTraversalInNativeManifestUris(string uri)
    {
        var skill = CreateSkill("alpha");
        skill.Uri = uri;
        var root = uri.Substring(0, uri.Length - "/SKILL.md".Length);
        skill.Resources = SkillResources.FromResources(
        [
            new SkillResource { Uri = uri, Digest = s_validDigest, Size = 1 },
            new SkillResource { Uri = root + "/%2e%2e/secret.md", Digest = s_validDigest, Size = 1 },
        ]);

        var error = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(skill));
        Assert.Contains("encoded separator", error.Message);
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
        yield return Case("syntactically invalid uri", s =>
        {
            s.Uri = "1 bad://alpha/SKILL.md";
            s.Resources = SkillResources.FromResources([new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 }]);
        });
        yield return Case("metadata present but null", s => s.Frontmatter["metadata"] = null);
        yield return Case("compatibility present but null", s => s.Frontmatter["compatibility"] = null);
        yield return Case("relative uri", s =>
        {
            s.Uri = "alpha/SKILL.md";
            s.Resources = SkillResources.FromResources([new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 }]);
        });
        yield return Case("uri with query", s =>
        {
            s.Uri = "skill://alpha/SKILL.md?x=1";
            s.Resources = SkillResources.FromResources([new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 }]);
        });
        yield return Case("resource escapes the skill through '..'", s => s.Resources = SkillResources.FromResources(
        [
            new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 },
            new SkillResource { Uri = "skill://alpha/../secret.md", Digest = s_validDigest, Size = 1 },
        ]));
        yield return Case("resource with empty segment", s => s.Resources = SkillResources.FromResources(
        [
            new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 },
            new SkillResource { Uri = "skill://alpha//x.md", Digest = s_validDigest, Size = 1 },
        ]));
        yield return Case("resource with fragment", s => s.Resources = SkillResources.FromResources(
        [
            new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 },
            new SkillResource { Uri = "skill://alpha/x.md#frag", Digest = s_validDigest, Size = 1 },
        ]));
        yield return Case("description over 1024 characters", s => s.Frontmatter["description"] = new string('d', 1025));
        yield return Case("compatibility over 500 characters", s => s.Frontmatter["compatibility"] = new string('c', 501));
        yield return Case("compatibility empty", s => s.Frontmatter["compatibility"] = "");
        yield return Case("metadata not a mapping", s => s.Frontmatter["metadata"] = "x");
        yield return Case("allowed-tools as a list", s => s.Frontmatter["allowed-tools"] = new JsonArray("Read", "Write"));
        yield return Case("allowed-tools as a number", s => s.Frontmatter["allowed-tools"] = 1);
        yield return Case("allowed-tools as an object", s => s.Frontmatter["allowed-tools"] = new JsonObject { ["Read"] = true });
        yield return Case("allowed-tools as a boolean", s => s.Frontmatter["allowed-tools"] = true);
        yield return Case("allowed-tools present but null", s => s.Frontmatter["allowed-tools"] = null);
        yield return Case("license as a list", s => s.Frontmatter["license"] = new JsonArray("MIT"));
        yield return Case("license as a number", s => s.Frontmatter["license"] = 2);
        yield return Case("license as an object", s => s.Frontmatter["license"] = new JsonObject { ["name"] = "MIT" });
        yield return Case("license as a boolean", s => s.Frontmatter["license"] = false);
        yield return Case("license present but null", s => s.Frontmatter["license"] = null);
        yield return Case("name missing", s => s.Frontmatter.Remove("name"));
        yield return Case("name not a string", s => s.Frontmatter["name"] = 1);
        yield return Case("name does not match uri segment", s => s.Frontmatter["name"] = "beta");
        yield return Case("name with an uppercase letter", s =>
        {
            s.Uri = "skill://Café/SKILL.md";
            s.Frontmatter["name"] = "Café";
            s.Resources = SkillResources.FromResources([new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 }]);
        });
        yield return Case("resource escapes the skill through an encoded '..'", s => s.Resources = SkillResources.FromResources(
        [
            new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 },
            new SkillResource { Uri = "skill://alpha/%2e%2e/secret.md", Digest = s_validDigest, Size = 1 },
        ]));
        yield return Case("resource with an encoded slash", s => s.Resources = SkillResources.FromResources(
        [
            new SkillResource { Uri = s.Uri, Digest = s_validDigest, Size = 1 },
            new SkillResource { Uri = "skill://alpha/a%2Fb.md", Digest = s_validDigest, Size = 1 },
        ]));
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
    }

    [Theory]
    [MemberData(nameof(InvalidSkills))]
    public void Create_RejectsInvalidSkill(string reason, Skill skill)
    {
        var exception = Assert.Throws<ArgumentException>("protocolSkill", () => McpServerSkill.Create(skill));

        Assert.Contains("skill://", exception.Message);
        Assert.NotEmpty(reason);
    }

}
