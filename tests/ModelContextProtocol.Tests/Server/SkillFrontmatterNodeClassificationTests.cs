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
    [InlineData("metadata:\n  {version: 1}", """{"metadata":{"version":1}}""")]
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

    private string CreateMarkdown(string body) => "---\nname: alpha\ndescription: d\n" + body + "\n---\n";

    private JsonObject CreateFrontmatter(string extraJson) => new()
    {
        ["name"] = "alpha",
        ["description"] = "d",
        ["extra"] = JsonNode.Parse(extraJson),
    };
}
