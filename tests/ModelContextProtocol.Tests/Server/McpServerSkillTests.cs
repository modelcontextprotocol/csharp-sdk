using ModelContextProtocol.Extensions.Skills;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for <see cref="McpServerSkill"/>: manifest computation, resource creation, path handling, validation, and
/// loading from a directory.
/// </summary>
public class McpServerSkillTests
{
    private const string SkillUri = "skill://git-workflow/SKILL.md";
    private const string SkillMarkdown = "---\nname: git-workflow\ndescription: Git conventions\n---\n\n# Git workflow\n";

    private static JsonObject Frontmatter(string name = "git-workflow", string? description = "Git conventions") => new()
    {
        ["name"] = name,
        ["description"] = description,
    };

    [Fact]
    public void Create_ComputesManifestFromFileBytes()
    {
        byte[] guide = Encoding.UTF8.GetBytes("# Guide\n");

        var skill = McpServerSkill.Create(SkillUri, Frontmatter(),
        [
            new McpServerSkillFile { Path = "references/GUIDE.md", Content = guide },
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
        ]);

        var entry = skill.ProtocolSkill;
        Assert.Equal(SkillUri, entry.Uri);
        Assert.Same(entry.Frontmatter, entry.Frontmatter);
        Assert.False(entry.Resources.IsDynamic);

        var manifest = entry.Resources.Resources!;
        Assert.Equal(2, manifest.Count);

        // SKILL.md is always first, regardless of input order.
        Assert.Equal(SkillUri, manifest[0].Uri);
        Assert.Equal(Encoding.UTF8.GetByteCount(SkillMarkdown), manifest[0].Size);
        Assert.Equal(SkillVerifier.ComputeDigest(Encoding.UTF8.GetBytes(SkillMarkdown)), manifest[0].Digest);

        Assert.Equal("skill://git-workflow/references/GUIDE.md", manifest[1].Uri);
        Assert.Equal(guide.Length, manifest[1].Size);
        Assert.Equal(SkillVerifier.ComputeDigest(guide), manifest[1].Digest);
    }

    [Fact]
    public void Create_ProducesOneResourcePerFileWithSpecMetadata()
    {
        var skill = McpServerSkill.Create(SkillUri, Frontmatter(),
        [
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
            McpServerSkillFile.FromText("scripts/run.py", "print('hi')\n"),
            McpServerSkillFile.FromText("data/table.csv", "a,b\n", mimeType: "text/x-custom"),
        ]);

        Assert.Equal(3, skill.Resources.Count);

        var skillFile = skill.Resources[0].ProtocolResource;
        Assert.NotNull(skillFile);
        Assert.Equal(SkillUri, skillFile.Uri);
        Assert.Equal("git-workflow", skillFile.Name);
        Assert.Equal("Git conventions", skillFile.Description);
        Assert.Equal("text/markdown", skillFile.MimeType);

        var script = skill.Resources[2].ProtocolResource;
        Assert.NotNull(script);
        Assert.Equal("skill://git-workflow/scripts/run.py", script.Uri);
        Assert.Equal("scripts/run.py", script.Name);
        Assert.Equal("text/x-python", script.MimeType);

        var table = skill.Resources[1].ProtocolResource;
        Assert.NotNull(table);
        Assert.Equal("text/x-custom", table.MimeType);
    }

    [Theory]
    [InlineData("./SKILL.md", "SKILL.md")]
    [InlineData("references\\GUIDE.md", "references/GUIDE.md")]
    public void Create_NormalizesFilePaths(string input, string expectedRelative)
    {
        var files = new List<McpServerSkillFile> { McpServerSkillFile.FromText(input, "x") };
        if (expectedRelative != "SKILL.md")
        {
            files.Add(McpServerSkillFile.FromText("SKILL.md", SkillMarkdown));
        }

        var skill = McpServerSkill.Create(SkillUri, Frontmatter(), files);

        Assert.Contains(skill.ProtocolSkill.Resources.Resources!, r => r.Uri == "skill://git-workflow/" + expectedRelative);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/SKILL.md")]
    [InlineData("references/")]
    [InlineData("../SKILL.md")]
    [InlineData("a/./b.md")]
    [InlineData("a//b.md")]
    public void Create_RejectsUnsafePaths(string path)
    {
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(SkillUri, Frontmatter(),
        [
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
            McpServerSkillFile.FromText(path, "x"),
        ]));
    }

    [Fact]
    public void Create_RejectsMissingSkillFile()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create(SkillUri, Frontmatter(), [McpServerSkillFile.FromText("README.md", "x")]));

        Assert.Contains("SKILL.md", exception.Message);
    }

    [Fact]
    public void Create_RejectsDuplicatePaths()
    {
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(SkillUri, Frontmatter(),
        [
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
            McpServerSkillFile.FromText("./SKILL.md", SkillMarkdown),
        ]));
    }

    [Fact]
    public void Create_RejectsUriNotEndingInSkillFile()
    {
        Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create("skill://git-workflow", Frontmatter(), [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)]));
    }

    [Fact]
    public void Create_RejectsFrontmatterNameMismatch()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create(SkillUri, Frontmatter(name: "git-flow"), [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)]));

        Assert.Equal("frontmatter", exception.ParamName);
        Assert.Contains("git-flow", exception.Message);
    }

    [Fact]
    public void Create_RejectsMissingDescription()
    {
        Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create(SkillUri, Frontmatter(description: null), [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)]));
    }

    [Fact]
    public void Create_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.Create(null!, Frontmatter(), []));
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.Create(SkillUri, null!, []));
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.Create(SkillUri, Frontmatter(), null!));
    }

    [Fact]
    public void CreateFromDirectory_LoadsFilesRecursively()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mcp-skill-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "templates", "regional"));
            File.WriteAllText(Path.Combine(directory, "SKILL.md"), SkillMarkdown);
            File.WriteAllText(Path.Combine(directory, "templates", "invoice.md"), "# Invoice\n");
            File.WriteAllBytes(Path.Combine(directory, "templates", "regional", "logo.png"), [0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE]);

            var skill = McpServerSkill.CreateFromDirectory(SkillUri, Frontmatter(), directory);

            var uris = skill.ProtocolSkill.Resources.Resources!.Select(r => r.Uri).ToList();
            Assert.Equal(
            [
                "skill://git-workflow/SKILL.md",
                "skill://git-workflow/templates/invoice.md",
                "skill://git-workflow/templates/regional/logo.png",
            ], uris);

            Assert.Equal("image/png", skill.Resources[2].ProtocolResource!.MimeType);
            Assert.Equal(6, skill.ProtocolSkill.Resources.Resources![2].Size);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateFromDirectory_WithMissingDirectory_Throws()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mcp-skill-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(() => McpServerSkill.CreateFromDirectory(SkillUri, Frontmatter(), directory));
    }
}
