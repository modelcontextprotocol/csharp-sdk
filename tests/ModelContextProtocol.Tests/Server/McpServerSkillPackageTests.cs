using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for <see cref="McpServerSkillPackage"/>: manifest computation, resource creation, path handling, validation, and
/// loading from a directory.
/// </summary>
public class McpServerSkillPackageTests
{
    private const string SkillUri = "skill://git-workflow/SKILL.md";
    private const string SkillMarkdown = "---\nname: git-workflow\ndescription: Git conventions\n---\n\n# Git workflow\n";

    private JsonObject Frontmatter(string name = "git-workflow", string? description = "Git conventions") => new()
    {
        ["name"] = name,
        ["description"] = description,
    };

    [Fact]
    public void Create_ComputesManifestFromFileBytes()
    {
        byte[] guide = Encoding.UTF8.GetBytes("# Guide\n");

        var frontmatter = Frontmatter();
        var skill = McpServerSkillPackage.Create(
        [
            new McpServerSkillFile { Path = "references/GUIDE.md", Content = guide },
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
        ], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = frontmatter });

        var entry = skill.Skill.ProtocolSkill!;
        Assert.Equal(SkillUri, entry.Uri);
        Assert.True(JsonNode.DeepEquals(frontmatter, entry.Frontmatter));
        Assert.False(entry.Resources.IsDynamic);

        var manifest = entry.Resources.Entries;
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
    public async Task StandaloneAuthoringProducesMatchingResourcesWithSpecMetadata()
    {
        byte[] binary = new byte[1024];
        var skill = McpServerSkillPackage.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
            McpServerSkillFile.FromText("scripts/run.py", "print('hi')\n"),
            McpServerSkillFile.FromText("data/table.csv", "a,b\n", mimeType: "text/x-custom"),
            McpServerSkillFile.FromText("config.json", "{}"),
            new McpServerSkillFile { Path = "data.bin", Content = binary, MimeType = "application/octet-stream" },
        ], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() });

        Assert.Equal(5, skill.Resources.Count);

        var skillFile = skill.Resources[0].ProtocolResource;
        Assert.NotNull(skillFile);
        Assert.Equal(SkillUri, skillFile.Uri);
        Assert.Equal("git-workflow", skillFile.Name);
        Assert.Equal("Git conventions", skillFile.Description);
        Assert.Equal("text/markdown", skillFile.MimeType);

        var script = Assert.Single(skill.Resources, resource => resource.ProtocolResource!.Name == "scripts/run.py").ProtocolResource;
        Assert.NotNull(script);
        Assert.Equal("skill://git-workflow/scripts/run.py", script.Uri);
        Assert.Equal("scripts/run.py", script.Name);
        Assert.Equal("text/x-python", script.MimeType);

        var table = Assert.Single(skill.Resources, resource => resource.ProtocolResource!.Name == "data/table.csv").ProtocolResource;
        Assert.NotNull(table);
        Assert.Equal("text/x-custom", table.MimeType);

        Assert.True(Assert.IsAssignableFrom<IList<McpServerResource>>(skill.Resources).IsReadOnly);
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        await using var server = McpServer.Create(transport, new McpServerOptions());
        var entry = skill.Skill.ProtocolSkill!;
        foreach (var resource in skill.Resources)
        {
            var protocol = resource.ProtocolResource!;
            var context = new RequestContext<ReadResourceRequestParams>(
                server, new JsonRpcRequest { Method = RequestMethods.ResourcesRead },
                new ReadResourceRequestParams { Uri = protocol.Uri });
            var result = await resource.ReadAsync(context, TestContext.Current.CancellationToken);
            if (protocol.MimeType == "application/octet-stream")
            {
                Assert.Equal(binary, Assert.IsType<BlobResourceContents>(Assert.Single(result.Contents)).DecodedData.ToArray());
            }
            else
            {
                Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
            }

            SkillVerifier.Verify(entry, context.Params.Uri, result);
        }
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

        var skill = McpServerSkillPackage.Create(files, new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() });

        Assert.Contains(skill.Skill.ProtocolSkill!.Resources.Entries!, r => r.Uri == "skill://git-workflow/" + expectedRelative);
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
        Assert.Throws<ArgumentException>(() => McpServerSkillPackage.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
            McpServerSkillFile.FromText(path, "x"),
        ], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() }));
    }

    [Fact]
    public void Create_RejectsMissingSkillFile()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("README.md", "x")], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() }));

        Assert.Contains("SKILL.md", exception.Message);
    }

    [Fact]
    public void Create_RejectsDuplicatePaths()
    {
        Assert.Throws<ArgumentException>(() => McpServerSkillPackage.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
            McpServerSkillFile.FromText("./SKILL.md", SkillMarkdown),
        ], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() }));
    }

    [Fact]
    public void Create_RejectsUriNotEndingInSkillFile()
    {
        Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)], new McpServerSkillPackageCreateOptions { Uri = "skill://git-workflow", Frontmatter = Frontmatter() }));
    }

    [Fact]
    public void Create_RejectsFrontmatterNameMismatch()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter(name: "git-flow") }));

        Assert.Equal("options", exception.ParamName);
        Assert.Contains("git-flow", exception.Message);
    }

    [Fact]
    public void Create_RejectsMissingDescription()
    {
        Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter(description: null) }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_ReadsFrontmatterAndUsesImplicitOrExplicitUri(bool explicitUri)
    {
        var uri = explicitUri ? "skill://tenant/git-workflow/SKILL.md" : SkillUri;
        var skill = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", SkillMarkdown), McpServerSkillFile.FromText("a.md", "x")],
            explicitUri ? new McpServerSkillPackageCreateOptions { Uri = uri } : null);

        Assert.True(JsonNode.DeepEquals(Frontmatter(), skill.Skill.ProtocolSkill!.Frontmatter));
        Assert.Equal(uri, skill.Skill.ProtocolSkill.Uri);
        Assert.Equal(uri.Replace("SKILL.md", "a.md"), skill.Skill.ProtocolSkill.Resources.Entries![1].Uri);
    }

    [Fact]
    public void Create_RejectsUriWhoseNameDiffersFromTheFile()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)], new McpServerSkillPackageCreateOptions { Uri = "skill://other-name/SKILL.md" }));

        Assert.Contains("git-workflow", exception.Message);
    }

    [Fact]
    public void Create_WithUnreadableFrontmatter_ExplainsAndPointsAtTheExplicitOverload()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: &a git-workflow\ndescription: d\n---\n")]));

        Assert.Contains("anchors", exception.Message);
        Assert.Contains("JsonObject", exception.Message);

        var noFrontmatter = Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", "# No frontmatter\n")]));
        Assert.Contains("must begin", noFrontmatter.Message);
    }

    [Fact]
    public void Create_WithExplicitFrontmatter_RejectsMismatchWithTheFile()
    {
        var mismatched = Frontmatter();
        mismatched["license"] = "MIT";

        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = mismatched }));

        Assert.Equal("options", exception.ParamName);
        Assert.Contains("does not match", exception.Message);
        Assert.Contains("\"license\"", exception.Message);
    }

    [Fact]
    public void Create_WithExplicitFrontmatterMissingName_AttributesFailureToOptions()
    {
        var exception = Assert.Throws<ArgumentException>("options", () => McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: &name alpha\ndescription: Alpha\n---\n")],
            new McpServerSkillPackageCreateOptions
            {
                Frontmatter = new JsonObject { ["description"] = "Alpha" },
            }));

        Assert.Contains("'name'", exception.Message);
    }

    [Fact]
    public void Create_WithExplicitFrontmatter_AcceptsMatchAndUnreadableFile()
    {
        // Matches the file: fine.
        McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", SkillMarkdown)], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() });

        // Values are compared as typed JSON: a quoted "2" in the file is the string "2" in the object.
        McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: git-workflow\ndescription: d\nmetadata:\n  major: \"2\"\n---\n")], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = new JsonObject { ["name"] = "git-workflow", ["description"] = "d", ["metadata"] = new JsonObject { ["major"] = "2" } } });

        // The caller supplies the complete, prevalidated frontmatter for this unsupported YAML form.
        var escapeHatch = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: &n git-workflow\ndescription: Git conventions\n---\n")], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() });
        Assert.Equal("git-workflow", escapeHatch.Skill.ProtocolSkill!.Name);

        // The caller has also validated this complete multi-line quoted value.
        var multiLine = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: git-workflow\ndescription: \"Git\n  conventions\"\n---\n")], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() });
        Assert.Equal("Git conventions", multiLine.Skill.ProtocolSkill!.Description);

        // So is a flow collection that closes on a later line.
        var explicitTags = Frontmatter();
        explicitTags["tags"] = new JsonArray("Read", "Write");
        var multiLineFlow = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: git-workflow\ndescription: Git conventions\ntags: [Read,\n  Write]\n---\n")], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = explicitTags });
        Assert.Equal(2, multiLineFlow.Skill.ProtocolSkill!.Frontmatter["tags"]!.AsArray().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_AttributesInvalidFrontmatterToFilesOrOptions(bool explicitUri)
    {
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", "---\nname: git-workflow\ndescription: 123\n---\n") };
        var options = new McpServerSkillPackageCreateOptions { Uri = explicitUri ? SkillUri : null };

        Assert.Throws<ArgumentException>("files", () => McpServerSkillPackage.Create(files, options));

        options.Frontmatter = new JsonObject { ["name"] = "git-workflow", ["description"] = 123 };
        Assert.Throws<ArgumentException>("options", () => McpServerSkillPackage.Create(files, options));
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
        // These errors are detected by the bounded reader; explicit metadata does not bypass them.
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", markdown)], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() }));

        Assert.Contains(messageFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WithExplicitFrontmatter_RejectsNonUtf8SkillFile()
    {
        var exception = Assert.Throws<ArgumentException>(() => McpServerSkillPackage.Create(
            [new McpServerSkillFile { Path = "SKILL.md", Content = new byte[] { 0x2D, 0x2D, 0x2D, 0x0A, 0xFF, 0xFE } }], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() }));

        Assert.Contains("UTF-8", exception.Message);
    }

    [Fact]
    public void Create_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>("protocolSkill", () => McpServerSkill.Create((Skill)null!));
        Assert.Throws<ArgumentNullException>("files", () => McpServerSkillPackage.Create((IEnumerable<McpServerSkillFile>)null!));
        Assert.Throws<ArgumentNullException>("directoryPath", () => McpServerSkillPackage.CreateFromDirectory(null!));
        Assert.Throws<ArgumentNullException>("directoryPath", () => McpServerSkillPackage.CreateFromSubdirectories(null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateFromDirectory_LoadsFilesRecursivelyWithImplicitOrExplicitUri(bool explicitUri)
    {
        string directory = Path.Combine(Path.GetTempPath(), "mcp-skill-" + Guid.NewGuid().ToString("N"));
        string uri = explicitUri ? "skill://tenant/git-workflow/SKILL.md" : SkillUri;
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "templates", "regional"));
            File.WriteAllText(Path.Combine(directory, "SKILL.md"), SkillMarkdown);
            File.WriteAllText(Path.Combine(directory, "templates", "invoice.md"), "# Invoice\n");
            File.WriteAllBytes(Path.Combine(directory, "templates", "regional", "logo.png"), [0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE]);

            var skill = McpServerSkillPackage.CreateFromDirectory(directory, new McpServerSkillPackageCreateOptions
            {
                Uri = explicitUri ? uri : null,
                Frontmatter = Frontmatter(),
                Metadata = ["directory-metadata"],
                Meta = new JsonObject { ["example.com/category"] = "directory" },
            });
            Assert.Equal(uri, skill.Skill.ProtocolSkill!.Uri);
            Assert.Equal("directory-metadata", Assert.Single(skill.Skill.Metadata));
            Assert.Equal("directory", skill.Skill.ProtocolSkill!.Meta!["example.com/category"]!.GetValue<string>());

            var uris = skill.Skill.ProtocolSkill!.Resources.Entries!.Select(r => r.Uri).ToList();
            Assert.Equal(
            [
                uri,
                uri.Replace("SKILL.md", "templates/invoice.md"),
                uri.Replace("SKILL.md", "templates/regional/logo.png"),
            ], uris);

            Assert.Equal("image/png", skill.Resources[2].ProtocolResource!.MimeType);
            Assert.Equal(6, skill.Skill.ProtocolSkill!.Resources.Entries![2].Size);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateFromDirectory_AttributesInvalidFrontmatterToDirectoryOrOptions(bool explicitUri)
    {
        string directory = Path.Combine(Path.GetTempPath(), "mcp-skill-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "SKILL.md"), "---\nname: git-workflow\ndescription: 123\n---\n");
            var options = new McpServerSkillPackageCreateOptions { Uri = explicitUri ? SkillUri : null };

            Assert.Throws<ArgumentException>("directoryPath", () => McpServerSkillPackage.CreateFromDirectory(directory, options));

            options.Frontmatter = new JsonObject { ["name"] = "git-workflow", ["description"] = 123 };
            Assert.Throws<ArgumentException>("options", () => McpServerSkillPackage.CreateFromDirectory(directory, options));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SubdirectoryPackagesAreStandaloneOrderedAndIncludeCommonMetadata()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mcp-skill-packages-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var (child, name) in new[] { ("z-child", "beta"), ("a-child", "alpha") })
            {
                Directory.CreateDirectory(Path.Combine(directory, child));
                File.WriteAllText(Path.Combine(directory, child, "SKILL.md"), $"---\nname: {name}\ndescription: {name}\n---\n");
            }

            var metadata = new List<object> { "package-tag" };
            var meta = new JsonObject { ["example.com/category"] = new JsonObject { ["value"] = "original" } };
            var options = new McpServerSkillPackageDiscoveryOptions
            {
                UriPrefix = "skill://tenant",
                Metadata = metadata,
                Meta = meta,
            };
            var packages = McpServerSkillPackage.CreateFromSubdirectories(directory, options);
            Assert.Equal("skill://tenant", options.UriPrefix);
            Assert.True(Assert.IsAssignableFrom<IList<McpServerSkillPackage>>(packages).IsReadOnly);
            Assert.Equal(["skill://tenant/alpha/SKILL.md", "skill://tenant/beta/SKILL.md"],
                packages.Select(package => package.Skill.Id));

            var skills = new McpServerSkillCollection();
            var resources = new McpServerResourceCollection();
            await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
            await using var server = McpServer.Create(transport, new McpServerOptions());
            foreach (var package in packages)
            {
                skills.TryAdd(package.Skill);
                Assert.Equal("package-tag", Assert.Single(package.Skill.Metadata));
                var entry = package.Skill.ProtocolSkill!;
                Assert.True(JsonNode.DeepEquals(meta, entry.Meta));
                foreach (var resource in package.Resources)
                {
                    resources.TryAdd(resource);
                    Assert.Null(resource.ProtocolResource!.Meta);
                    var context = new RequestContext<ReadResourceRequestParams>(
                        server, new JsonRpcRequest { Method = RequestMethods.ResourcesRead },
                        new ReadResourceRequestParams { Uri = resource.ProtocolResource.Uri });
                    SkillVerifier.Verify(entry, context.Params.Uri, await resource.ReadAsync(context, TestContext.Current.CancellationToken));
                }

            }

            Assert.Equal(2, skills.Count);
            Assert.Equal(2, resources.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubdirectoryBatchValidatesFilesEvenForDuplicateSkillIdentities(bool conflicting)
    {
        string directory = Path.Combine(Path.GetTempPath(), "mcp-skill-package-conflict-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string child in new[] { "first", "second" })
            {
                Directory.CreateDirectory(Path.Combine(directory, child));
                File.WriteAllText(Path.Combine(directory, child, "SKILL.md"),
                    SkillMarkdown + (conflicting && child == "second" ? "changed" : ""));
            }

            if (conflicting)
            {
                Assert.Throws<ArgumentException>(() => McpServerSkillPackage.CreateFromSubdirectories(directory));
            }
            else
            {
                var packages = McpServerSkillPackage.CreateFromSubdirectories(directory);
                Assert.Equal(2, packages.Count);
                Assert.All(packages, package => Assert.Equal(SkillUri, package.Skill.Id));
                Assert.Equal(packages[0].Skill.ProtocolSkill!.Resources.Entries![0].Digest,
                    packages[1].Skill.ProtocolSkill!.Resources.Entries![0].Digest);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Create_EscapesUriSyntaxCharactersInFilePaths()
    {
        var skill = McpServerSkillPackage.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", SkillMarkdown),
            McpServerSkillFile.FromText("templates/{name} v2.md", "template"),
            McpServerSkillFile.FromText("notes/a#b?c.md", "note"),
        ], new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() });

        var manifest = skill.Skill.ProtocolSkill!.Resources.Entries!;
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
    public void Create_RequiresAStringNameToDeriveTheUri()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: 123\ndescription: d\n---\n")]));

        Assert.Contains("must declare a string 'name'", exception.Message);
    }

    [Fact]
    public void Create_RejectsNullFileEntries()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", SkillMarkdown), null!], new McpServerSkillPackageCreateOptions { Uri = SkillUri }));

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

            var exception = Assert.Throws<ArgumentException>(() => McpServerSkillPackage.CreateFromDirectory(skillDirectory, new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() }));
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

            var exception = Assert.Throws<ArgumentException>(() => McpServerSkillPackage.CreateFromDirectory(link));
            Assert.Equal("directoryPath", exception.ParamName);

            // The target itself is fine.
            Assert.Equal(SkillUri, McpServerSkillPackage.CreateFromDirectory(target).Skill.ProtocolSkill!.Uri);
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

            Assert.Throws<ArgumentException>(() => McpServerSkillPackage.CreateFromDirectory(skillDirectory, new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
#endif

    [Fact]
    public void CreateFromDirectory_WithMissingDirectory_Throws()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mcp-skill-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(() => McpServerSkillPackage.CreateFromDirectory(directory, new McpServerSkillPackageCreateOptions { Uri = SkillUri, Frontmatter = Frontmatter() }));
    }
}
