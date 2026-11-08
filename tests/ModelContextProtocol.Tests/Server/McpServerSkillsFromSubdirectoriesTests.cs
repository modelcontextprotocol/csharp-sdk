using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// End-to-end tests for <c>WithSkillsFromSubdirectories</c>: a directory of skill folders becomes a served catalog with
/// frontmatter read from each <c>SKILL.md</c>, URIs derived from the frontmatter names under the given prefix, and
/// non-skill subdirectories ignored.
/// </summary>
public class McpServerSkillsFromSubdirectoriesTests : ClientServerTestBase
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-skills-root-" + Guid.NewGuid().ToString("N"));

    public McpServerSkillsFromSubdirectoriesTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
        Directory.CreateDirectory(Path.Combine(_root, "git-workflow", "references"));
        File.WriteAllText(Path.Combine(_root, "git-workflow", "SKILL.md"), "---\nname: git-workflow\ndescription: Git conventions\nlicense: MIT\n---\n\n# Git\n");
        File.WriteAllText(Path.Combine(_root, "git-workflow", "references", "STYLE.md"), "# Style\n");
        File.WriteAllText(Path.Combine(_root, "git-workflow", ".env"), "synthetic-private-value");
        Directory.CreateDirectory(Path.Combine(_root, "git-workflow", ".git"));
        File.WriteAllText(Path.Combine(_root, "git-workflow", ".git", "config"), "synthetic-config");

        Directory.CreateDirectory(Path.Combine(_root, "refunds"));
        File.WriteAllText(Path.Combine(_root, "refunds", "SKILL.md"), "---\nname: refunds\ndescription: >\n  Process refunds\n  per policy.\nmetadata:\n  version: \"2.1.0\"\n---\n");

        // Not a skill: no SKILL.md. Must be ignored.
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "README.md"), "not a skill");

        // A file at the root is ignored too.
        File.WriteAllText(Path.Combine(_root, "README.md"), "about these skills");
        Directory.CreateDirectory(Path.Combine(_root, ".private-skill"));
        File.WriteAllText(Path.Combine(_root, ".private-skill", "SKILL.md"), "---\nname: private-skill\ndescription: d\n---\n");

        McpServerBuilder.WithSkillsFromSubdirectories(_root, new McpServerSkillPackageDiscoveryOptions { UriPrefix = "skill://acme/billing" });
        StartServer();
    }

    [Fact]
    public async Task ServesEverySkillDirectory_WithFrontmatterFromTheFile()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var skills = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, skills.Count);

        var gitWorkflow = Assert.Single(skills, s => s.Uri == "skill://acme/billing/git-workflow/SKILL.md");
        Assert.Equal("MIT", gitWorkflow.Frontmatter["license"]?.GetValue<string>());
        Assert.Equal(2, gitWorkflow.Resources.Entries!.Count);
        Assert.Contains(gitWorkflow.Resources.Entries, r => r.Uri == "skill://acme/billing/git-workflow/references/STYLE.md");

        var refunds = Assert.Single(skills, s => s.Uri == "skill://acme/billing/refunds/SKILL.md");
        Assert.Equal("Process refunds per policy.\n", refunds.Description);
        Assert.Equal("2.1.0", refunds.Frontmatter["metadata"]?["version"]?.GetValue<string>());
    }

    [Fact]
    public async Task ServedFiles_VerifyAgainstTheirManifest()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var skill = await client.GetSkillAsync("skill://acme/billing/git-workflow/SKILL.md", cancellationToken: TestContext.Current.CancellationToken);

        foreach (var file in skill.Resources.Entries!)
        {
            var result = await client.ReadSkillResourceAsync(skill, file.Uri, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Single(result.Contents);
        }
    }

    [Fact]
    public async Task HiddenEntriesAreNeitherListedNorReadable()
    {
        await using var client = await CreateMcpClientForServer();
        var skills = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(skills, skill => skill.Name == "private-skill");
        Assert.DoesNotContain(skills.SelectMany(skill => skill.Resources.Entries!), resource => resource.Uri.Contains("/."));

        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ReadResourceAsync(
            "skill://acme/billing/git-workflow/.env", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuilderCanIncludeHiddenEntriesExplicitly()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithSkillsFromSubdirectories(_root, new McpServerSkillPackageDiscoveryOptions { IncludeHiddenAndSystemFiles = true });
        await using var provider = services.BuildServiceProvider();
        var resources = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ResourceCollection!
            .Select(resource => resource.ProtocolResource!.Uri).ToList();

        Assert.Contains("skill://private-skill/SKILL.md", resources);
        Assert.Contains("skill://git-workflow/.env", resources);
        Assert.Contains("skill://git-workflow/.git/config", resources);
    }

    [Fact]
    public async Task PackageBatchesDetectFilesThatCollideUnderUriEquivalence()
    {
        // The resource collection compares URIs case-insensitively in scheme and authority, so these two skills
        // would silently share one registered resource. With different content that must be an error.
        var upper = McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: refunds\ndescription: upper\n---\n")], new McpServerSkillPackageCreateOptions { Uri = "skill://Acme/refunds/SKILL.md" });
        var lower = McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: refunds\ndescription: lower\n---\n")], new McpServerSkillPackageCreateOptions { Uri = "skill://acme/refunds/SKILL.md" });

        await Assert.ThrowsAsync<ArgumentException>(() => BuildOptionsAsync(upper, lower));

        // Equivalent identities keep the first contribution when content is identical too.
        var same = McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: refunds\ndescription: upper\n---\n")], new McpServerSkillPackageCreateOptions { Uri = "skill://ACME/refunds/SKILL.md" });
        Assert.Same(upper.Resources[0], Assert.Single((await BuildOptionsAsync(upper, same)).ResourceCollection!));

        // The legitimate shared-file case: a nested skill's SKILL.md is also a file of the enclosing skill, under
        // the identical URI text and with identical content. That registers once and is fine.
        const string InnerMarkdown = "---\nname: inner\ndescription: nested\n---\n";
        var outer = McpServerSkillPackage.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", "---\nname: acme\ndescription: outer\n---\n"),
            McpServerSkillFile.FromText("inner/SKILL.md", InnerMarkdown),
        ], new McpServerSkillPackageCreateOptions { Uri = "skill://acme/SKILL.md" });
        var inner = McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", InnerMarkdown)], new McpServerSkillPackageCreateOptions { Uri = "skill://acme/inner/SKILL.md" });
        var shared = (await BuildOptionsAsync(outer, inner)).ResourceCollection!;
        Assert.Equal(2, shared.Count);
        Assert.Same(outer.Resources[1], shared[inner.Skill.Id]);
    }

    [Theory]
    [InlineData("café")]
    [InlineData("数据分析")]
    [InlineData("\U00010428")]
    public async Task WithSkills_RegistersNestedUnicodeSkillsWithIdenticalSharedUris(string name)
    {
        var innerMarkdown = $"---\nname: {name}\ndescription: nested\n---\n";
        var outer = McpServerSkillPackage.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", "---\nname: outer\ndescription: parent\n---\n"),
            McpServerSkillFile.FromText($"{name}/SKILL.md", innerMarkdown),
        ], new McpServerSkillPackageCreateOptions { Uri = "skill://outer/SKILL.md" });
        var uri = $"skill://outer/{Uri.EscapeDataString(name)}/SKILL.md";
        var inner = McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", innerMarkdown)], new McpServerSkillPackageCreateOptions { Uri = uri });
        var services = new ServiceCollection();
        services.AddMcpServer().WithSkillPackages([outer, inner]);
        await using var provider = services.BuildServiceProvider();
        var resources = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ResourceCollection!
            .Select(resource => resource.ProtocolResource!.Uri).ToArray();

        Assert.Equal(2, resources.Length);
        Assert.Contains(uri, resources);
        Assert.Equal(uri, inner.Skill.ProtocolSkill!.Uri);
        Assert.Contains(outer.Skill.ProtocolSkill!.Resources.Entries!, resource => resource.Uri == uri);
    }

    [Theory]
    [InlineData("café", "skill://outer/")]
    [InlineData("数据分析", "skill://outer/")]
    [InlineData("\U00010428", "skill://outer/")]
    [InlineData("café", "repo:/skills/")]
    [InlineData("数据分析", "repo:skills/")]
    [InlineData("\U00010428", "repo:/skills/")]
    public async Task WithSkillsFromSubdirectories_EscapesUnicodeNamePathSegments(string name, string prefix)
    {
        var root = Path.Combine(_root, "unicode-root");
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), $"---\nname: {name}\ndescription: d\n---\n");
        var services = new ServiceCollection();
        services.AddMcpServer().WithSkillsFromSubdirectories(root, new McpServerSkillPackageDiscoveryOptions { UriPrefix = prefix });
        await using var provider = services.BuildServiceProvider();

        var resource = Assert.Single(provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ResourceCollection!);
        Assert.Equal($"{prefix}{Uri.EscapeDataString(name)}/SKILL.md", resource.ProtocolResource!.Uri);
    }

    private static async Task<McpServerOptions> BuildOptionsAsync(params McpServerSkillPackage[] skills)
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithSkillPackages(skills);
        await using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
    }

    [Fact]
    public void WithSkillsFromSubdirectories_RejectsDirectoriesWithoutSkills()
    {
        string empty = Path.Combine(Path.GetTempPath(), "mcp-skills-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(empty, "not-a-skill"));
        try
        {
            var services = new ServiceCollection();
            var exception = Assert.Throws<ArgumentException>(() => services.AddMcpServer().WithSkillsFromSubdirectories(empty));
            Assert.Contains("No skill directories", exception.Message);

            Assert.Throws<DirectoryNotFoundException>(() => services.AddMcpServer().WithSkillsFromSubdirectories(Path.Combine(empty, "missing")));
            Assert.Throws<ArgumentException>(() => services.AddMcpServer().WithSkillsFromSubdirectories(empty, new McpServerSkillPackageDiscoveryOptions { UriPrefix = "" }));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void WithSkillsFromSubdirectories_RejectsAnInvalidSkillRatherThanSkippingIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "mcp-skills-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "good"));
            File.WriteAllText(Path.Combine(root, "good", "SKILL.md"), "---\nname: good\ndescription: d\n---\n");
            Directory.CreateDirectory(Path.Combine(root, "broken"));
            File.WriteAllText(Path.Combine(root, "broken", "SKILL.md"), "---\nname: broken\n---\n");

            var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddMcpServer().WithSkillsFromSubdirectories(root));
            Assert.Contains("description", exception.Message);
            Assert.Equal("directoryPath", exception.ParamName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

#if NET
    [Fact]
    public void WithSkillsFromSubdirectories_RejectsSymbolicLinkSkillDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "mcp-skills-linkroot-" + Guid.NewGuid().ToString("N"));
        try
        {
            string skills = Path.Combine(root, "skills");
            string outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(skills);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "SKILL.md"), "---\nname: alpha\ndescription: d\n---\n");
            File.WriteAllText(Path.Combine(outside, "private.txt"), "private data");

            try
            {
                Directory.CreateSymbolicLink(Path.Combine(skills, "alpha"), outside);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                Assert.Skip($"Cannot create symbolic links here: {e.Message}");
            }

            var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddMcpServer().WithSkillsFromSubdirectories(skills));
            Assert.Contains("symbolic link", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
#endif

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }
}
