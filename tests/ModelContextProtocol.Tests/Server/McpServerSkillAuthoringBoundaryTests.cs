using ModelContextProtocol.Extensions.Skills;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpServerSkillAuthoringBoundaryTests
{
    private const string SkillUri = "skill://owned/SKILL.md";

    [Theory]
    [InlineData("---\nname: &name owned\ndescription: d\n---\n")]
    [InlineData("---\nname: owned\ndescription: d\nextra: [never\n---\n")]
    [InlineData("# No frontmatter\n")]
    [InlineData("---\nname: owned\ndescription: d\n")]
    public void AutomaticAuthoring_RejectsUnsupportedMalformedOrMissingFrontmatter(string markdown)
    {
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(SkillUri, files));

        WithDirectory(markdown, directory =>
        {
            Assert.Throws<ArgumentException>(() => McpServerSkill.CreateFromDirectory(directory));
            Assert.Throws<ArgumentException>(() => McpServerSkill.CreateFromDirectory(SkillUri, directory));
        });
    }

    [Fact]
    public void ExplicitMetadata_DoesNotOverrideSupportedFrontmatterEquality()
    {
        const string Markdown = "---\nname: owned\ndescription: From the file\n---\n";
        var supplied = new JsonObject { ["name"] = "owned", ["description"] = "Not from the file" };

        var filesException = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(
            SkillUri, supplied, [McpServerSkillFile.FromText("SKILL.md", Markdown)]));
        Assert.Contains("does not match", filesException.Message);

        WithDirectory(Markdown, directory =>
        {
            var directoryException = Assert.Throws<ArgumentException>(() =>
                McpServerSkill.CreateFromDirectory(SkillUri, supplied, directory));
            Assert.Contains("does not match", directoryException.Message);
        });
    }

    [Theory]
    [InlineData("# No frontmatter\n")]
    [InlineData("---\nname: owned\ndescription: d\nextra: [never\n---\n")]
    public void ExplicitMetadata_DoesNotBypassDetectedBoundaryOrFormatErrors(string markdown)
    {
        var supplied = new JsonObject { ["name"] = "owned", ["description"] = "d" };
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(
            SkillUri, supplied, [McpServerSkillFile.FromText("SKILL.md", markdown)]));
        WithDirectory(markdown, directory =>
        {
            Assert.Throws<ArgumentException>(() => McpServerSkill.CreateFromDirectory(SkillUri, supplied, directory));
        });
    }

    private void WithDirectory(string markdown, Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mcp-skill-ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "SKILL.md"), markdown);
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
