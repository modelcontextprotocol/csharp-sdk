using ModelContextProtocol.Extensions.Skills;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class SkillFrontmatterRegressionTests
{
    [Theory]
    [InlineData("description:\n  Builds reusable components with isolated tables.\n  Use for new components.",
        "Builds reusable components with isolated tables. Use for new components.")]
    [InlineData("description:\n  One line on the next line.", "One line on the next line.")]
    [InlineData("description:\n  \"Quoted on the next line\"", "Quoted on the next line")]
    [InlineData("description:\n  'Quoted on the next line'", "Quoted on the next line")]
    [InlineData("description: # note\n  Text on the next line", "Text on the next line")]
    [InlineData("description:\n  # note\n\n  Text on the next line", "Text on the next line")]
    [InlineData("description:\n    First line\n  continued with less indentation", "First line continued with less indentation")]
    public void NextLineScalar_IsParsedAndPublishedWithoutChangingItsValue(string body, string expectedDescription)
    {
        var markdown = "---\nname: alpha\n" + body + "\n---\n";
        var expected = new JsonObject { ["name"] = "alpha", ["description"] = expectedDescription };

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData("tags:\n  -\n    text on next line", """{"tags":["text on next line"]}""")]
    [InlineData("tags:\n  - # note\n    \"text on next line\"", """{"tags":["text on next line"]}""")]
    [InlineData("metadata:\n  note:\n    first\n    second", """{"metadata":{"note":"first second"}}""")]
    public void NestedNextLineScalar_IsParsedAndPublished(string body, string expectedFields)
    {
        var markdown = "---\nname: alpha\ndescription: d\n" + body + "\n---\n\n# Body\n";
        var expected = JsonNode.Parse(expectedFields)!.AsObject();
        expected["name"] = "alpha";
        expected["description"] = "d";

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData(">", "  First\n  \tCode\n  Last\n", "First\n\tCode\nLast\n")]
    [InlineData(">-", "  First\n  \tCode\n  Last\n", "First\n\tCode\nLast")]
    [InlineData(">+", "  First\n  \tCode\n  Last\n\n", "First\n\tCode\nLast\n\n")]
    [InlineData(">", "  First\n\n  \tCode\n\n  Last\n", "First\n\n\tCode\n\nLast\n")]
    [InlineData(">2", "  First\n  \tCode\n  Last\n", "First\n\tCode\nLast\n")]
    public void FoldedScalar_PreservesLineBreaksAroundTabLedContent(string header, string content, string expectedDescription)
    {
        var markdown = "---\nname: comparison\ndescription: " + header + "\n" + content + "---\n";
        var expected = new JsonObject { ["name"] = "comparison", ["description"] = expectedDescription };

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData("|+", "Kept\n\n")]
    [InlineData("|", "Kept\n")]
    [InlineData("|-", "Kept")]
    public void BlockScalarAtClosingFence_PreservesAuthoredLineBreaks(string header, string expectedDescription)
    {
        var markdown = "---\nname: alpha\ndescription: " + header + "\n  Kept\n\n---\n";
        var expected = new JsonObject { ["name"] = "alpha", ["description"] = expectedDescription };

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData("\"Useful\"#comment", "{version: 1}", "[read]")]
    [InlineData("'Useful'#comment", "{version: 1}", "[read]")]
    [InlineData("Useful", "{version: 1}#comment", "[read]")]
    [InlineData("Useful", "{version: 1}", "[read]#comment")]
    public void UnseparatedTrailingComment_IsRejectedByParserAndBothAuthoringOverloads(
        string description, string metadata, string tags)
    {
        var markdown = $"---\nname: comparison\ndescription: {description}\nmetadata: {metadata}\ntags: {tags}\n---\n";
        var expected = new JsonObject
        {
            ["name"] = "comparison",
            ["description"] = "Useful",
            ["metadata"] = new JsonObject { ["version"] = 1 },
            ["tags"] = new JsonArray("read"),
        };
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        var parseException = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        var automaticException = Record.Exception(() => McpServerSkill.Create(files));
        var explicitException = Record.Exception(() => McpServerSkill.Create("skill://comparison/SKILL.md", expected, files));

        Assert.IsType<FormatException>(parseException);
        Assert.IsType<ArgumentException>(automaticException);
        Assert.IsType<ArgumentException>(explicitException);
    }

    [Theory]
    [InlineData("Useful#hash", "{version: 1}", "[read]", "Useful#hash")]
    [InlineData("\"Useful\" #comment", "{version: 1}", "[read]", "Useful")]
    [InlineData("'Useful'\t#comment", "{version: 1}", "[read]", "Useful")]
    [InlineData("Useful", "{version: 1} #comment", "[read]", "Useful")]
    [InlineData("Useful", "{version: 1}\t#comment", "[read]", "Useful")]
    [InlineData("Useful", "{version: 1}", "[read] #comment", "Useful")]
    [InlineData("Useful", "{version: 1}", "[read]\t#comment", "Useful")]
    public void SeparatedCommentsAndPlainHashes_RemainValid(
        string description, string metadata, string tags, string expectedDescription)
    {
        var markdown = $"---\nname: comparison\ndescription: {description}\nmetadata: {metadata}\ntags: {tags}\n---\n";
        var expected = new JsonObject
        {
            ["name"] = "comparison",
            ["description"] = expectedDescription,
            ["metadata"] = new JsonObject { ["version"] = 1 },
            ["tags"] = new JsonArray("read"),
        };

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData(".inf")]
    [InlineData("-.Inf")]
    [InlineData(".nan")]
    public void NonFiniteScalar_CannotBeReplacedWithJsonNullThroughExplicitFrontmatter(string value)
    {
        var markdown = $"---\nname: alpha\ndescription: d\nextra: {value}\n---\n";
        var supplied = new JsonObject { ["name"] = "alpha", ["description"] = "d", ["extra"] = null };
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        var exception = Assert.Throws<FormatException>(() => SkillFrontmatter.Parse(markdown));
        Assert.Contains("cannot be represented in JSON", exception.Message);
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));
    }

    private void AssertParsedAndPublished(string markdown, JsonObject expected)
    {
        var parsed = SkillFrontmatter.Parse(markdown);
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        var automatic = McpServerSkill.Create(files);
        var explicitMetadata = McpServerSkill.Create(automatic.ProtocolSkill.Uri, expected, files);

        Assert.True(JsonNode.DeepEquals(expected, parsed), $"Expected {expected}, parsed {parsed}.");
        Assert.True(JsonNode.DeepEquals(expected, automatic.ProtocolSkill.Frontmatter));
        Assert.True(JsonNode.DeepEquals(expected, explicitMetadata.ProtocolSkill.Frontmatter));
    }
}
