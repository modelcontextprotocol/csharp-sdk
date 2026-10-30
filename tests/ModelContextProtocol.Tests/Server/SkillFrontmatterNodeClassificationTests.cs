using ModelContextProtocol.Extensions.Skills;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class SkillFrontmatterNodeClassificationTests
{
    [Theory]
    [InlineData("extra:\n  [x]: value", """{"[ x ]":"value"}""")]
    [InlineData("extra:\n  {a: b}: value", """{"{ a: b }":"value"}""")]
    [InlineData("extra:\n  -\n    [x]: value", """[{"[ x ]":"value"}]""")]
    [InlineData("extra:\n  [.inf]: value", """{"[ .inf ]":"value"}""")]
    [InlineData("extra:\n  [team's]: value", """{"[ team's ]":"value"}""")]
    [InlineData("extra:\n  [\"a]: b\"]: value", """{"[ \"a]: b\" ]":"value"}""")]
    public void FlowCollectionKeys_KeepTheExplicitFrontmatterFallback(string body, string extraJson)
    {
        var markdown = CreateMarkdown(body);
        var supplied = CreateFrontmatter(extraJson);
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        McpServerSkill? skill = null;

        var parseException = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        var automaticException = Record.Exception(() => McpServerSkill.Create(files));
        var explicitException = Record.Exception(() => skill = McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));

        Assert.Equal("UnsupportedYamlException", parseException?.GetType().Name);
        Assert.Contains("flow collections are not supported as mapping keys", parseException!.Message);
        Assert.IsType<ArgumentException>(automaticException);
        Assert.Null(explicitException);
        Assert.NotNull(skill);
        Assert.True(JsonNode.DeepEquals(supplied, skill.ProtocolSkill.Frontmatter));
    }

    [Theory]
    [InlineData("extra:\n  {version: 1}", """{"extra":{"version":1}}""")]
    [InlineData("tags:\n  [read]", """{"tags":["read"]}""")]
    [InlineData("metadata:\n  \"note\": value", """{"metadata":{"note":"value"}}""")]
    [InlineData("extra:\n  [\"a]: b\"]", """{"extra":["a]: b"]}""")]
    [InlineData("metadata:\n  {note: \"}: not a key\"}", """{"metadata":{"note":"}: not a key"}}""")]
    [InlineData("extra:\n  [team's]", """{"extra":["team's"]}""")]
    [InlineData("extra:\n  -\n    [read]", """{"extra":[["read"]]}""")]
    [InlineData("note:\n  Key:value#hash", """{"note":"Key:value#hash"}""")]
    [InlineData("note:\n  |2\n    text", """{"note":"  text\n"}""")]
    public void NextLineValuesAndQuotedKeys_RemainSupported(string body, string expectedFields)
    {
        var markdown = CreateMarkdown(body);
        var expected = JsonNode.Parse(expectedFields)!.AsObject();
        expected["name"] = "alpha";
        expected["description"] = "d";
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        Assert.True(JsonNode.DeepEquals(expected, SkillFrontmatter.Parse(markdown)));
        Assert.True(JsonNode.DeepEquals(expected, McpServerSkill.Create(files).ProtocolSkill.Frontmatter));
        Assert.True(JsonNode.DeepEquals(expected,
            McpServerSkill.Create("skill://alpha/SKILL.md", expected, files).ProtocolSkill.Frontmatter));
    }

    [Theory]
    [InlineData("extra:\n  &anchor value\nbroken: [never", "\"value\"")]
    [InlineData("extra:\n  -\n    &anchor value\nbroken: [never", "[\"value\"]")]
    [InlineData("extra:\n  &anchor value", "\"value\"")]
    [InlineData("extra:\n  !!str value\nbroken: [never", "\"value\"")]
    [InlineData("extra:\n  *undefined\nbroken: [never", "\"value\"")]
    public void UnsupportedNextLineNodeProperties_KeepTheImportedRejection(string body, string extraJson)
    {
        var markdown = CreateMarkdown(body);
        var supplied = CreateFrontmatter(extraJson);
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        Assert.Throws<FormatException>(() => SkillFrontmatter.Parse(markdown));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));
    }

    [Theory]
    [InlineData("extra:\n  [x]#comment", "[\"x\"]")]
    [InlineData("extra:\n  {version: 1}#comment", """{"version":1}""")]
    [InlineData("extra:\n  [x]:value", "[\"x\"]")]
    public void MalformedFlowValueSuffixes_DoNotBecomeUnsupportedKeys(string body, string extraJson)
    {
        var markdown = CreateMarkdown(body);
        var supplied = CreateFrontmatter(extraJson);
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        Assert.Throws<FormatException>(() => SkillFrontmatter.Parse(markdown));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));
    }

    [Fact]
    public void ExplicitFlowKeyInTags_IsUnsupportedAndKeepsTheExplicitFrontmatterFallback()
    {
        // The reported case: valid YAML (a single-pair mapping with an explicit key) that the reader does not support.
        var markdown = CreateMarkdown("tags: [? a]");
        var supplied = new JsonObject
        {
            ["name"] = "alpha",
            ["description"] = "d",
            ["tags"] = JsonNode.Parse("""[{"a":null}]"""),
        };
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        var parseException = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        var automaticException = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        var skill = McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files);

        Assert.Equal("UnsupportedYamlException", parseException?.GetType().Name);
        Assert.Contains("explicit mapping keys", parseException!.Message);
        Assert.Contains("does not support", automaticException.Message);
        Assert.Contains("explicit mapping keys", automaticException.Message);
        Assert.True(JsonNode.DeepEquals(supplied, skill.ProtocolSkill.Frontmatter));
    }

    [Theory]
    [InlineData("extra: [? a, b]", """[{"a":null},"b"]""", "explicit mapping keys")]
    [InlineData("extra: [x, ? a]", """["x",{"a":null}]""", "explicit mapping keys")]
    [InlineData("extra: [? ]", """[{"":null}]""", "explicit mapping keys")]
    [InlineData("extra: [?\ta]", """[{"a":null}]""", "explicit mapping keys")]
    [InlineData("extra: {? a}", """{"a":null}""", "explicit mapping keys")]
    [InlineData("extra: {? a: b}", """{"a":"b"}""", "explicit mapping keys")]
    [InlineData("extra: {? : b}", """{"":"b"}""", "explicit mapping keys")]
    [InlineData("extra: {? }", """{"":null}""", "explicit mapping keys")]
    [InlineData("extra: {k: v, ? a}", """{"k":"v","a":null}""", "explicit mapping keys")]
    [InlineData("extra: [?\n  ]", """[{"":null}]""", "multi-line flow collections")]
    [InlineData("extra:\n  : a", """{"":"a"}""", "empty mapping keys")]
    [InlineData("extra:\n  :", """{"":null}""", "empty mapping keys")]
    [InlineData("extra:\n  :\ta", """{"":"a"}""", "empty mapping keys")]
    [InlineData("extra:\n  - : a", """[{"":"a"}]""", "empty mapping keys")]
    [InlineData("extra:\n  - ? a", """[{"a":null}]""", "complex mapping keys")]
    [InlineData("extra:\n  - ?", """[{"":null}]""", "complex mapping keys")]
    [InlineData("extra:\n  - ?\ta", """[{"a":null}]""", "complex mapping keys")]
    [InlineData("extra:\n  - ? a\n    : b", """[{"a":"b"}]""", "complex mapping keys")]
    public void ValidYamlOutsideTheSubset_IsUnsupportedRatherThanMalformed(string body, string extraJson, string messageFragment)
    {
        AssertUnsupportedWithExplicitFallback(body, extraJson, messageFragment);
    }

    [Fact]
    public void CommentInsideAFlowSequence_IsUnsupportedAndKeepsTheExplicitFrontmatterFallback()
    {
        // The reported case. The yaml package and PyYAML both read it as the JSON supplied below. The comment runs to
        // the end of its line, so the flow sequence continues on the next one.
        var markdown = "---\nname: test\ndescription: Test\ntags: [a, # comment\n  b]\n---\nbody\n";
        var supplied = new JsonObject
        {
            ["name"] = "test",
            ["description"] = "Test",
            ["tags"] = new JsonArray("a", "b"),
        };
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        var parseException = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        var automaticException = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        var skill = McpServerSkill.Create("skill://test/SKILL.md", supplied, files);

        Assert.Equal("UnsupportedYamlException", parseException?.GetType().Name);
        Assert.Contains("multi-line flow collections", parseException!.Message);
        Assert.Contains("does not support", automaticException.Message);
        Assert.True(JsonNode.DeepEquals(supplied, skill.ProtocolSkill.Frontmatter));
    }

    [Theory]
    [InlineData("extra: [ # c\n  a, b]", """["a","b"]""")]
    [InlineData("extra: [a, b, # c\n  ]", """["a","b"]""")]
    [InlineData("extra: [a # c\n  , b]", """["a","b"]""")]
    [InlineData("extra: [a, # c\n  b\n]", """["a","b"]""")]
    [InlineData("extra: [a, # c\n\n# column 0\n  b]", """["a","b"]""")]
    [InlineData("extra: [\"a\" # c\n  , b]", """["a","b"]""")]
    [InlineData("extra: {a: 1, # c\n  b: 2}", """{"a":1,"b":2}""")]
    [InlineData("extra: {a: # c\n  1}", """{"a":1}""")]
    [InlineData("extra: {a # c\n  : 1}", """{"a":1}""")]
    [InlineData("extra: {a:\n  1}", """{"a":1}""")]
    [InlineData("extra: [\"a\n  b\"]", """["a b"]""")]
    [InlineData("extra:\n  - [a, # c\n    b]", """[["a","b"]]""")]
    public void MultiLineFlowCollections_AreUnsupportedRatherThanMalformed(string body, string extraJson)
    {
        AssertUnsupportedWithExplicitFallback(body, extraJson, "multi-line flow collections");
    }

    [Theory]
    [InlineData("extra: [a, b")]
    [InlineData("extra: [a, # c\nnext: x]")]
    [InlineData("extra: [a # c\n  b]")]
    [InlineData("extra: [a # c\n  : b]")]
    [InlineData("extra: [\"a\" # c\n  b]")]
    [InlineData("extra: [a,# c\n  b]")]
    [InlineData("extra: [a, # c\n  b]\n  other: 1")]
    [InlineData("extra: [a,\nb]")]
    [InlineData("extra: [a\n  # c\n  b]")]
    [InlineData("extra: {a: b c: d}")]
    [InlineData("extra: {k: : a}")]
    [InlineData("extra: [a: b: c]")]
    public void InvalidFlowCollections_StayMalformedForEveryAuthoringPath(string body)
    {
        // YAML rejects all of these. The last five used to be reported as unsupported.
        AssertMalformedForEveryAuthoringPath(body);
    }

    [Fact]
    public void NestingDeeperThanTheLimit_IsUnsupportedAndKeepsTheExplicitFrontmatterFallback()
    {
        // 33 levels of block mappings: valid YAML, but the reader stops at 32 to bound its recursion.
        string body = "extra:\n" + string.Join("\n", Enumerable.Range(1, 32).Select(i => new string(' ', i) + $"k{i}:")) + " v";
        JsonNode extra = "v";
        for (int i = 32; i >= 1; i--)
        {
            extra = new JsonObject { [$"k{i}"] = extra };
        }

        AssertUnsupportedWithExplicitFallback(body, extra.ToJsonString(), "nested more than");
    }

    [Theory]
    [InlineData("  name: alpha\n  description: d\n  tags: [a, b]", """{"name":"alpha","description":"d","tags":["a","b"]}""")]
    [InlineData("    name: alpha\n    description: d\n    meta:\n      k: v", """{"name":"alpha","description":"d","meta":{"k":"v"}}""")]
    [InlineData("  name: alpha\n  description: |\n    line1\n    line2", """{"name":"alpha","description":"line1\nline2\n"}""")]
    [InlineData("  name: alpha\n  description: d\n  tags:\n  - a\n  - b", """{"name":"alpha","description":"d","tags":["a","b"]}""")]
    [InlineData("# comment\n  name: alpha\n\n  description: d", """{"name":"alpha","description":"d"}""")]
    public void IndentedRootMapping_IsReadForEveryAuthoringPath(string frontmatter, string expectedJson)
    {
        // YAML lets the root mapping start at any column, as long as all of its keys share it.
        var markdown = "---\n" + frontmatter + "\n---\nbody\n";
        var expected = JsonNode.Parse(expectedJson)!.AsObject();
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        Assert.True(JsonNode.DeepEquals(expected, SkillFrontmatter.Parse(markdown)));
        Assert.True(JsonNode.DeepEquals(expected, McpServerSkill.Create(files).ProtocolSkill.Frontmatter));
        Assert.True(JsonNode.DeepEquals(expected,
            McpServerSkill.Create("skill://alpha/SKILL.md", expected, files).ProtocolSkill.Frontmatter));
    }

    [Theory]
    [InlineData("  name: alpha\n description: d")]
    [InlineData("  name: alpha\ndescription: d")]
    [InlineData("  name: alpha\n    description: d")]
    [InlineData("\tname: alpha\n\tdescription: d")]
    [InlineData(" \tname: alpha\n \tdescription: d")]
    public void MismatchedOrTabIndentedRoot_StaysMalformedForEveryAuthoringPath(string frontmatter)
    {
        AssertMalformedMarkdownForEveryAuthoringPath("---\n" + frontmatter + "\n---\n");
    }

    [Theory]
    [InlineData("extra: ? a")]
    [InlineData("extra: ?")]
    [InlineData("extra: : a")]
    [InlineData("extra: - a")]
    [InlineData("extra: [- a]")]
    [InlineData("extra: [-]")]
    [InlineData("extra: [@a]")]
    [InlineData("extra: [|a]")]
    [InlineData("extra: {k: ? a}")]
    [InlineData("extra: {k: ?}")]
    [InlineData("extra: {- a: b}")]
    [InlineData("extra: {-}")]
    [InlineData("extra: {-, a}")]
    [InlineData("extra: {@a: b}")]
    [InlineData("extra: [?\nnext: x")]
    [InlineData("@a: x")]
    public void InvalidIndicatorUse_StaysMalformedForEveryAuthoringPath(string body)
    {
        AssertMalformedForEveryAuthoringPath(body);
    }

    [Theory]
    [InlineData("extra: [?]")]
    [InlineData("extra: [?,]")]
    [InlineData("extra: {?}")]
    [InlineData("extra: {?, a}")]
    public void ExplicitKeyIndicatorWithoutWhiteSpace_StaysMalformed(string body)
    {
        // YAML 1.2 requires white space after the '?' of an explicit key. Some parsers accept these anyway; the
        // reader does not treat them as valid YAML outside its subset.
        AssertMalformedForEveryAuthoringPath(body);
    }

    [Theory]
    [InlineData("extra: {?: b}", """{"?":"b"}""")]
    [InlineData("extra: {-: b}", """{"-":"b"}""")]
    [InlineData("extra: {:: b}", """{":":"b"}""")]
    public void IndicatorFollowedByColon_IsAPlainFlowMappingKey(string body, string extraJson)
    {
        var markdown = CreateMarkdown(body);
        var expected = CreateFrontmatter(extraJson);
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        Assert.True(JsonNode.DeepEquals(expected, SkillFrontmatter.Parse(markdown)));
        Assert.True(JsonNode.DeepEquals(expected, McpServerSkill.Create(files).ProtocolSkill.Frontmatter));
        Assert.True(JsonNode.DeepEquals(expected,
            McpServerSkill.Create("skill://alpha/SKILL.md", expected, files).ProtocolSkill.Frontmatter));
    }

    [Theory]
    [InlineData("just a scalar")]
    [InlineData("just a\nscalar")]
    [InlineData("\"quoted\"")]
    [InlineData("'quoted'")]
    [InlineData("|\n  block")]
    [InlineData("42")]
    [InlineData(".inf")]
    [InlineData("0x1FFFFFFFFFFFFFFFFF")]
    [InlineData("\"\\ud800\"")]
    [InlineData("&a value")]
    [InlineData("!!str\nvalue")]
    [InlineData("&a: x")]
    [InlineData("!t: x")]
    [InlineData("\"a\n  b\"")]
    [InlineData("[a, b]")]
    [InlineData("[a,\n  b]")]
    [InlineData("[[a]]")]
    [InlineData("&a [x: 1]")]
    [InlineData("- a\n- b")]
    [InlineData("- &a x")]
    [InlineData("- a\n- [b,\n  c]")]
    public void RootThatIsNotAMapping_IsRejectedAsNotAMappingForEveryAuthoringPath(string frontmatter)
    {
        // Valid YAML, but frontmatter must be a mapping, so no supplied JsonObject can stand in for it. In YAML 1.2,
        // "&a: x" is the scalar "x" with the anchor "a:", and "!t: x" is "x" with the tag "!t:".
        var markdown = "---\n" + frontmatter + "\n---\n";
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        var parseException = Assert.ThrowsAny<FormatException>(() => SkillFrontmatter.Parse(markdown));
        var automaticException = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        var explicitException = Assert.Throws<ArgumentException>(
            () => McpServerSkill.Create("skill://alpha/SKILL.md", CreateFrontmatter("null"), files));

        Assert.NotEqual("UnsupportedYamlException", parseException.GetType().Name);
        Assert.Contains("must be a YAML mapping", parseException.Message);
        Assert.Contains("must be a YAML mapping", automaticException.Message);
        Assert.Contains("must be a YAML mapping", explicitException.Message);
    }

    [Theory]
    [InlineData("{name: alpha, description: d}")]
    [InlineData("  {name: alpha, description: d} # comment")]
    public void RootFlowMapping_IsReadForEveryAuthoringPath(string frontmatter)
    {
        var markdown = "---\n" + frontmatter + "\n---\n";
        var expected = new JsonObject { ["name"] = "alpha", ["description"] = "d" };
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        Assert.True(JsonNode.DeepEquals(expected, SkillFrontmatter.Parse(markdown)));
        Assert.True(JsonNode.DeepEquals(expected, McpServerSkill.Create(files).ProtocolSkill.Frontmatter));
        Assert.True(JsonNode.DeepEquals(expected,
            McpServerSkill.Create("skill://alpha/SKILL.md", expected, files).ProtocolSkill.Frontmatter));
    }

    [Theory]
    [InlineData("{name: alpha,\n  description: d}", "multi-line flow collections")]
    [InlineData("&a\nname: alpha\ndescription: d", "anchors")]
    [InlineData("!!map\nname: alpha\ndescription: d", "anchors")]
    [InlineData("&a {name: alpha, description: d}", "anchors")]
    public void RootMappingOutsideTheSubset_IsUnsupportedRatherThanNotAMapping(string frontmatter, string messageFragment)
    {
        var supplied = new JsonObject { ["name"] = "alpha", ["description"] = "d" };

        AssertUnsupportedMarkdownWithExplicitFallback("---\n" + frontmatter + "\n---\n", supplied, messageFragment);
    }

    [Theory]
    [InlineData("{name: alpha, description: d}\nextra: 1")]
    [InlineData("{name: alpha, name: beta}")]
    [InlineData("{name: alpha, description: d} extra")]
    [InlineData("*a: x")]
    [InlineData("[a, b]\nextra: 1")]
    [InlineData("[a,\n b]: v")]
    public void InvalidRoot_StaysMalformedForEveryAuthoringPath(string frontmatter)
    {
        AssertMalformedMarkdownForEveryAuthoringPath("---\n" + frontmatter + "\n---\n");
    }

    private void AssertUnsupportedWithExplicitFallback(string body, string extraJson, string messageFragment)
    {
        AssertUnsupportedMarkdownWithExplicitFallback(CreateMarkdown(body), CreateFrontmatter(extraJson), messageFragment);
    }

    private void AssertUnsupportedMarkdownWithExplicitFallback(string markdown, JsonObject supplied, string messageFragment)
    {
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        McpServerSkill? skill = null;

        var parseException = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        var automaticException = Record.Exception(() => McpServerSkill.Create(files));
        var explicitException = Record.Exception(() => skill = McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));

        Assert.Equal("UnsupportedYamlException", parseException?.GetType().Name);
        Assert.Contains(messageFragment, parseException!.Message);
        Assert.IsType<ArgumentException>(automaticException);
        Assert.Null(explicitException);
        Assert.True(JsonNode.DeepEquals(supplied, skill!.ProtocolSkill.Frontmatter));
    }

    private void AssertMalformedForEveryAuthoringPath(string body)
    {
        AssertMalformedMarkdownForEveryAuthoringPath(CreateMarkdown(body));
    }

    private void AssertMalformedMarkdownForEveryAuthoringPath(string markdown)
    {
        var supplied = CreateFrontmatter("null");
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        var parseException = Assert.ThrowsAny<FormatException>(() => SkillFrontmatter.Parse(markdown));
        Assert.NotEqual("UnsupportedYamlException", parseException.GetType().Name);
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        var explicitException = Assert.Throws<ArgumentException>(() => McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));
        Assert.Contains("could not be read", explicitException.Message);
    }

    private string CreateMarkdown(string body) => "---\nname: alpha\ndescription: d\n" + body + "\n---\n";

    private JsonObject CreateFrontmatter(string extraJson) => new()
    {
        ["name"] = "alpha",
        ["description"] = "d",
        ["extra"] = JsonNode.Parse(extraJson),
    };
}
