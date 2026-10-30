#if NET
using ModelContextProtocol.Extensions.Skills;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Samples;

/// <summary>
/// The SkillsClient sample loads a skill only if the frontmatter it parses from the fetched <c>SKILL.md</c> equals the
/// entry the server published. These tests run the sample's <c>YamlFrontmatterReader</c> on files the server authored.
/// </summary>
public class SkillsClientFrontmatterReaderTests
{
    [Theory]
    [InlineData("|+", "  Kept\n\n", "Kept\n\n", false)]
    [InlineData("|+", "  Kept\n\n", "Kept\n\n", true)]
    [InlineData("|", "  Kept\n\n", "Kept\n", false)]
    [InlineData("|", "  Kept\n", "Kept\n", false)]
    [InlineData("|-", "  Kept\n\n", "Kept", false)]
    public void BlockScalarAtClosingFence_MatchesTheServer(string header, string content, string expectedDescription, bool crlf)
    {
        var markdown = "---\nname: alpha\ndescription: " + header + "\n" + content + "---\n# Alpha\n";
        if (crlf)
        {
            markdown = markdown.Replace("\n", "\r\n");
        }

        var served = McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", markdown)]).ProtocolSkill.Frontmatter;

        var fetched = new YamlFrontmatterReader().Read(markdown);

        Assert.Equal(expectedDescription, fetched["description"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(served, fetched), $"The server published {served.ToJsonString()}, the sample read {fetched.ToJsonString()}.");
    }

    [Theory]
    [InlineData("extra: !!str [one]")]
    [InlineData("extra: !!str {key: value}")]
    [InlineData("extra: !!str\n  - one")]
    [InlineData("extra: !!str\n  key: value")]
    [InlineData("extra: !!map [one]")]
    [InlineData("extra: !!seq {key: value}")]
    [InlineData("extra: !!set {one}")]
    [InlineData("extra: !custom [one]")]
    [InlineData("extra: !<tag:yaml.org,2002:str> [one]")]
    public void CollectionTagThatChangesTheValue_IsRejected(string entry)
    {
        var exception = Assert.Throws<FormatException>(() => new YamlFrontmatterReader().Read("---\nname: alpha\ndescription: d\n" + entry + "\n---\n"));

        Assert.Contains("does not convert", exception.Message);
    }

    [Theory]
    [InlineData("!!str")]
    [InlineData("!!seq")]
    [InlineData("!custom")]
    public void RootTagThatChangesTheValue_IsRejected(string tag)
    {
        var exception = Assert.Throws<FormatException>(() => new YamlFrontmatterReader().Read("---\n" + tag + "\nname: alpha\ndescription: d\n---\n"));

        Assert.Contains("does not convert", exception.Message);
    }

    [Theory]
    [InlineData("extra: !!seq [one]", """["one"]""")]
    [InlineData("extra: ! [one]", """["one"]""")]
    [InlineData("extra: !!map {key: value}", """{"key":"value"}""")]
    [InlineData("extra: ! {key: value}", """{"key":"value"}""")]
    [InlineData("extra: !!map\n  key: value", """{"key":"value"}""")]
    [InlineData("extra: !<tag:yaml.org,2002:seq> [one]", """["one"]""")]
    [InlineData("extra: !<tag:yaml.org,2002:map> {key: value}", """{"key":"value"}""")]
    public void CollectionTagThatKeepsTheValue_IsAccepted(string entry, string expectedExtra)
    {
        var fetched = new YamlFrontmatterReader().Read("---\nname: alpha\ndescription: d\n" + entry + "\n---\n");

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedExtra), fetched["extra"]), fetched.ToJsonString());
    }

    [Theory]
    [InlineData("!!map")]
    [InlineData("!")]
    [InlineData("!<tag:yaml.org,2002:map>")]
    public void RootMappingTag_IsAccepted(string tag)
    {
        var fetched = new YamlFrontmatterReader().Read("---\n" + tag + "\nname: alpha\ndescription: d\n---\n");

        Assert.True(JsonNode.DeepEquals(new JsonObject { ["name"] = "alpha", ["description"] = "d" }, fetched), fetched.ToJsonString());
    }

    [Theory]
    [InlineData("description: x{0}y")]
    [InlineData("description: \"x{0}y\"")]
    [InlineData("description: 'x{0}y'")]
    [InlineData("description: |\n  x{0}y")]
    [InlineData("description: d\n# note{0}")]
    [InlineData("description: d # note{0}")]
    [InlineData("description: xy{0}\nlicense: MIT")]
    public void LiteralNul_IsRejectedLikeTheServer(string entry)
    {
        AssertRejectedLikeTheServer(string.Format(entry, '\0'), 0x0000);
    }

    [Theory]
    [InlineData(0x000B)]
    [InlineData(0x000C)]
    [InlineData(0x001B)]
    [InlineData(0x007F)]
    [InlineData(0x0080)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    public void RawCharacterOutsideYamlsPrintableSet_IsRejectedLikeTheServer(int codePoint)
    {
        AssertRejectedLikeTheServer("description: x" + char.ConvertFromUtf32(codePoint) + "y", codePoint);
    }

    [Theory]
    [InlineData("description: d # note{0}allowed-tools: Bash", 0x000D)]
    [InlineData("description: d\n# note{0}allowed-tools: Bash", 0x2028)]
    [InlineData("description: d # note{0}allowed-tools: Bash", 0x0085)]
    [InlineData("description: d # note{0}allowed-tools: Bash", 0x2029)]
    [InlineData("description: x{0}y", 0x0085)]
    [InlineData("description: d{0}{0}", 0x000D)]
    public void RawCharacterSomeParsersReadAsALineBreak_IsRejectedLikeTheServer(string entry, int codePoint)
    {
        AssertRejectedLikeTheServer(string.Format(entry, (char)codePoint), codePoint);
    }

    [Theory]
    [InlineData(new[] { 0xD800 }, false, 0xD800)]
    [InlineData(new[] { 0xD800 }, true, 0xD800)]
    [InlineData(new[] { 0xDC00 }, false, 0xDC00)]
    [InlineData(new[] { 0xDC00, 0xD800 }, false, 0xDC00)]
    public void UnpairedSurrogate_IsRejected(int[] codeUnits, bool atLineEnd, int expectedCodeUnit)
    {
        var markdown = "---\nname: alpha\ndescription: x" + new string(codeUnits.Select(codeUnit => (char)codeUnit).ToArray()) + (atLineEnd ? "" : "y") + "\n---\n";

        var exception = Assert.Throws<FormatException>(() => new YamlFrontmatterReader().Read(markdown));

        Assert.Contains($"U+{expectedCodeUnit:X4}", exception.Message);
    }

    [Fact]
    public void SecondByteOrderMark_IsRejectedLikeTheServer()
    {
        var markdown = "\uFEFF\uFEFF---\nname: alpha\ndescription: d\n---\n";

        Assert.Throws<FormatException>(() => new YamlFrontmatterReader().Read(markdown));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", markdown)]));
    }

    [Fact]
    public void EscapedNul_MatchesTheServer()
    {
        var markdown = "---\nname: alpha\ndescription: \"x\\0y\"\n---\n";

        var served = McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", markdown)]).ProtocolSkill.Frontmatter;
        var fetched = new YamlFrontmatterReader().Read(markdown);

        Assert.Equal("x\0y", fetched["description"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(served, fetched), $"The server published {served.ToJsonString()}, the sample read {fetched.ToJsonString()}.");
    }

    [Theory]
    [InlineData("\\r", 0x000D)]
    [InlineData("\\N", 0x0085)]
    [InlineData("\\L", 0x2028)]
    [InlineData("\\P", 0x2029)]
    public void EscapedCharacterSomeParsersReadAsALineBreak_MatchesTheServer(string escape, int codePoint)
    {
        var markdown = "---\nname: alpha\ndescription: \"x" + escape + "y\"\n---\n";

        var served = McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", markdown)]).ProtocolSkill.Frontmatter;
        var fetched = new YamlFrontmatterReader().Read(markdown);

        Assert.Equal("x" + char.ConvertFromUtf32(codePoint) + "y", fetched["description"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(served, fetched), $"The server published {served.ToJsonString()}, the sample read {fetched.ToJsonString()}.");
    }

    private void AssertRejectedLikeTheServer(string entry, int codePoint)
    {
        var markdown = $"---\nname: alpha\n{entry}\n---\n";

        var sampleException = Record.Exception(() => new YamlFrontmatterReader().Read(markdown));
        var serverException = Record.Exception(() => McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", markdown)]));

        string character = $"U+{codePoint:X4}";
        Assert.Contains(character, Assert.IsType<FormatException>(sampleException).Message);
        Assert.Contains(character, Assert.IsType<ArgumentException>(serverException).Message);
    }
}
#endif
