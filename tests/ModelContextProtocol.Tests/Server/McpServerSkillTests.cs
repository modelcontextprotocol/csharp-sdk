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

        var frontmatter = Frontmatter();
        var skill = McpServerSkill.Create(SkillUri, frontmatter,
        [
            new McpServerSkillFile { Path = "references/GUIDE.md", Content = guide },
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
        ]);

        var entry = skill.ProtocolSkill;
        Assert.Equal(SkillUri, entry.Uri);
        Assert.True(JsonNode.DeepEquals(frontmatter, entry.Frontmatter));
        Assert.NotSame(frontmatter, entry.Frontmatter);
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
        var files = new List<McpServerSkillFile> { McpServerSkillFile.FromText(input, expectedRelative == "SKILL.md" ? SkillMarkdown : "x") };
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
    public void Create_ReadsFrontmatterFromSkillFile()
    {
        var skill = McpServerSkill.Create(SkillUri, [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)]);

        Assert.True(JsonNode.DeepEquals(Frontmatter(), skill.ProtocolSkill.Frontmatter));
    }

    [Fact]
    public void Create_DerivesUriFromFrontmatterName()
    {
        var skill = McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", SkillMarkdown), McpServerSkillFile.FromText("a.md", "x")]);

        Assert.Equal(SkillUri, skill.ProtocolSkill.Uri);
        Assert.Equal("skill://git-workflow/a.md", skill.ProtocolSkill.Resources.Resources![1].Uri);
    }

    [Fact]
    public void Create_RejectsUriWhoseNameDiffersFromTheFile()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create("skill://other-name/SKILL.md", [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)]));

        Assert.Contains("git-workflow", exception.Message);
    }

    [Fact]
    public void Create_WithUnreadableFrontmatter_ExplainsAndPointsAtTheExplicitOverload()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: &a git-workflow\ndescription: d\n---\n")]));

        Assert.Contains("anchors", exception.Message);
        Assert.Contains("JsonObject", exception.Message);

        var noFrontmatter = Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", "# No frontmatter\n")]));
        Assert.Contains("must begin", noFrontmatter.Message);
    }

    [Fact]
    public void Create_WithExplicitFrontmatter_RejectsMismatchWithTheFile()
    {
        var mismatched = Frontmatter();
        mismatched["license"] = "MIT";

        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create(SkillUri, mismatched, [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)]));

        Assert.Equal("frontmatter", exception.ParamName);
        Assert.Contains("does not match", exception.Message);
        Assert.Contains("\"license\"", exception.Message);
    }

    [Fact]
    public void Create_WithExplicitFrontmatter_AcceptsMatchAndUnreadableFile()
    {
        // Matches the file: fine.
        McpServerSkill.Create(SkillUri, Frontmatter(), [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)]);

        // Values are compared as typed JSON: a quoted "2" in the file is the string "2" in the object.
        McpServerSkill.Create(SkillUri, new JsonObject { ["name"] = "git-workflow", ["description"] = "d", ["metadata"] = new JsonObject { ["major"] = "2" } },
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: git-workflow\ndescription: d\nmetadata:\n  major: \"2\"\n---\n")]);

        // File uses valid YAML the reader does not support: the explicit object stands on its own.
        var escapeHatch = McpServerSkill.Create(SkillUri, Frontmatter(),
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: &n git-workflow\ndescription: Git conventions\n---\n")]);
        Assert.Equal("git-workflow", escapeHatch.ProtocolSkill.Name);

        // A quoted scalar that closes on a later line is valid YAML the reader does not support, so it is bypassable too.
        var multiLine = McpServerSkill.Create(SkillUri, Frontmatter(),
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: git-workflow\ndescription: \"Git\n  conventions\"\n---\n")]);
        Assert.Equal("Git conventions", multiLine.ProtocolSkill.Description);

        // So is a flow collection that closes on a later line.
        var explicitTools = Frontmatter();
        explicitTools["allowed-tools"] = new JsonArray("Read", "Write");
        var multiLineFlow = McpServerSkill.Create(SkillUri, explicitTools,
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: git-workflow\ndescription: Git conventions\nallowed-tools: [Read,\n  Write]\n---\n")]);
        Assert.Equal(2, multiLineFlow.ProtocolSkill.Frontmatter["allowed-tools"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("# no frontmatter block\n", "must begin")]
    [InlineData("---\nname: git-workflow\n", "not closed")]
    [InlineData("---\nname: a: b\n---\n", "cannot contain")]
    [InlineData("---\n\tname: x\n---\n", "tabs")]
    [InlineData("---\nname: git-workflow\ndescription: \"never closed\n---\n", "unterminated")]
    [InlineData("---\nname: git-workflow\ndescription: d\nallowed-tools: [Read, Write\n---\n", "unterminated flow")]
    [InlineData("---\nname: git-workflow\ndescription: d\n  extra # comment\n    more\n---\n", "a comment ended that value")]
    public void Create_WithExplicitFrontmatter_StillRejectsMalformedSkillFile(string markdown, string messageFragment)
    {
        // Only valid-but-unsupported YAML may be bypassed. A file no host can parse must not be published.
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create(SkillUri, Frontmatter(), [McpServerSkillFile.FromText("SKILL.md", markdown)]));

        Assert.Contains(messageFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WithExplicitFrontmatter_RejectsNonUtf8SkillFile()
    {
        var exception = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(SkillUri, Frontmatter(),
            [new McpServerSkillFile { Path = "SKILL.md", Content = new byte[] { 0x2D, 0x2D, 0x2D, 0x0A, 0xFF, 0xFE } }]));

        Assert.Contains("UTF-8", exception.Message);
    }

    [Fact]
    public void Create_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.Create(null!, Frontmatter(), []));
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.Create(SkillUri, null!, []));
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.Create(SkillUri, Frontmatter(), null!));
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.Create((IEnumerable<McpServerSkillFile>)null!));
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.Create((string)null!, []));
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.CreateFromDirectory((string)null!));
        Assert.Throws<ArgumentNullException>(() => McpServerSkill.CreateFromDirectory(SkillUri, (string)null!));
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

            // All three overloads agree.
            var skill = McpServerSkill.CreateFromDirectory(directory);
            Assert.Equal(SkillUri, skill.ProtocolSkill.Uri);
            Assert.Equal(SkillUri, McpServerSkill.CreateFromDirectory(SkillUri, directory).ProtocolSkill.Uri);
            Assert.Equal(SkillUri, McpServerSkill.CreateFromDirectory(SkillUri, Frontmatter(), directory).ProtocolSkill.Uri);

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
    public void Create_EscapesUriSyntaxCharactersInFilePaths()
    {
        var skill = McpServerSkill.Create(SkillUri, Frontmatter(),
        [
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
            McpServerSkillFile.FromText("templates/{name} v2.md", "template"),
            McpServerSkillFile.FromText("notes/a#b?c.md", "note"),
        ]);

        var manifest = skill.ProtocolSkill.Resources.Resources!;
        Assert.Equal("skill://git-workflow/templates/%7Bname%7D%20v2.md", manifest[2].Uri);
        Assert.Equal("skill://git-workflow/notes/a%23b%3Fc.md", manifest[1].Uri);

        // Every file is a concrete resource, never a template, and its resource URI equals its manifest URI.
        for (int i = 0; i < manifest.Count; i++)
        {
            Assert.False(skill.Resources[i].IsTemplated);
            Assert.Equal(manifest[i].Uri, skill.Resources[i].ProtocolResource!.Uri);
        }

        Assert.Equal("templates/{name} v2.md", skill.Resources[2].ProtocolResource!.Name);
    }

    [Fact]
    public void Create_KeepsItsOwnCopyOfTheEntry()
    {
        var frontmatter = Frontmatter();
        var skill = McpServerSkill.Create(SkillUri, frontmatter, [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)]);
        var manifestEntry = skill.ProtocolSkill.Resources.Resources![0];

        // The caller goes on to edit the frontmatter object it passed in, and an entry it was handed back.
        frontmatter["description"] = "changed";
        var returned = skill.ProtocolSkill;
        returned.Frontmatter["name"] = "changed";
        returned.Resources.Resources![0].Digest = "sha256:" + new string('0', 64);
        returned.Resources = SkillResources.Dynamic;

        // None of that reaches the skill, so what WithSkills registers is what Create computed. (That the served
        // bytes are likewise a copy of the caller's buffer is checked end to end in McpServerSkillsIntegrityTests.)
        var fresh = skill.ProtocolSkill;
        Assert.Equal("git-workflow", fresh.Name);
        Assert.Equal("Git conventions", fresh.Description);
        Assert.Equal(manifestEntry.Digest, fresh.Resources.Resources![0].Digest);
        Assert.NotSame(returned, fresh);
    }

    [Fact]
    public void Create_RequiresAStringNameToDeriveTheUri()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: 123\ndescription: d\n---\n")]));

        Assert.Contains("must declare a string 'name'", exception.Message);
    }

    [Fact]
    public void Create_RejectsNullFileEntries()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkill.Create(SkillUri, [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown), null!]));

        Assert.Contains("null entries", exception.Message);
        Assert.Equal("files", exception.ParamName);
    }

#if NET
    [Fact]
    public void CreateFromDirectory_RejectsSymbolicLinks()
    {
        string root = Path.Combine(Path.GetTempPath(), "mcp-skill-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            string skillDirectory = Path.Combine(root, "skill");
            Directory.CreateDirectory(skillDirectory);
            File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), SkillMarkdown);
            File.WriteAllText(Path.Combine(root, "outside.txt"), "private data");

            try
            {
                File.CreateSymbolicLink(Path.Combine(skillDirectory, "linked.txt"), Path.Combine(root, "outside.txt"));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                Assert.Skip($"Cannot create symbolic links here: {e.Message}");
            }

            var exception = Assert.Throws<ArgumentException>(() => McpServerSkill.CreateFromDirectory(SkillUri, Frontmatter(), skillDirectory));
            Assert.Contains("linked.txt", exception.Message);
            Assert.Equal("directoryPath", exception.ParamName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateFromDirectory_RejectsASymbolicLinkAsTheRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "mcp-skill-rootlink-" + Guid.NewGuid().ToString("N"));
        try
        {
            string target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "SKILL.md"), SkillMarkdown);

            string link = Path.Combine(root, "link");
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                Assert.Skip($"Cannot create symbolic links here: {e.Message}");
            }

            var exception = Assert.Throws<ArgumentException>(() => McpServerSkill.CreateFromDirectory(link));
            Assert.Equal("directoryPath", exception.ParamName);

            // The target itself is fine.
            Assert.Equal(SkillUri, McpServerSkill.CreateFromDirectory(target).ProtocolSkill.Uri);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateFromDirectory_RejectsDirectorySymbolicLinks()
    {
        string root = Path.Combine(Path.GetTempPath(), "mcp-skill-dirlink-" + Guid.NewGuid().ToString("N"));
        try
        {
            string skillDirectory = Path.Combine(root, "skill");
            Directory.CreateDirectory(skillDirectory);
            Directory.CreateDirectory(Path.Combine(root, "outside"));
            File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), SkillMarkdown);
            File.WriteAllText(Path.Combine(root, "outside", "secret.txt"), "private data");

            try
            {
                Directory.CreateSymbolicLink(Path.Combine(skillDirectory, "linked"), Path.Combine(root, "outside"));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                Assert.Skip($"Cannot create symbolic links here: {e.Message}");
            }

            Assert.Throws<ArgumentException>(() => McpServerSkill.CreateFromDirectory(SkillUri, Frontmatter(), skillDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
#endif

    [Fact]
    public void CreateFromDirectory_RejectsTooManyFilesBeforeReadingThem()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mcp-skill-many-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "SKILL.md"), SkillMarkdown);
            for (int i = 0; i < SkillsProtocol.MaxResourcesPerSkill; i++)
            {
                File.WriteAllText(Path.Combine(directory, $"f{i:D4}.txt"), "x");
            }

            var exception = Assert.Throws<ArgumentException>(() => McpServerSkill.CreateFromDirectory(SkillUri, directory));
            Assert.Contains(SkillsProtocol.MaxResourcesPerSkill.ToString(), exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateFromDirectory_RejectsOversizedContentBeforeReadingIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mcp-skill-big-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "SKILL.md"), SkillMarkdown);
            using (var big = new FileStream(Path.Combine(directory, "big.bin"), FileMode.CreateNew))
            {
                // A sparse file: the size check must trip without the bytes ever being read.
                big.SetLength(SkillsProtocol.MaxTotalSizeBytes + 1);
            }

            var exception = Assert.Throws<ArgumentException>(() => McpServerSkill.CreateFromDirectory(SkillUri, directory));
            Assert.Contains(SkillsProtocol.MaxTotalSizeBytes.ToString(), exception.Message);
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
