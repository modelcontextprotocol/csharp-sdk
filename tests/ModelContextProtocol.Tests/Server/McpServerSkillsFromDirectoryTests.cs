using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// End-to-end tests for <c>WithSkillsFromDirectory</c>: a directory of skill folders becomes a served catalog with
/// frontmatter read from each <c>SKILL.md</c>, URIs derived from the frontmatter names under the given prefix, and
/// non-skill subdirectories ignored.
/// </summary>
public class McpServerSkillsFromDirectoryTests : ClientServerTestBase
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-skills-root-" + Guid.NewGuid().ToString("N"));

    public McpServerSkillsFromDirectoryTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
        Directory.CreateDirectory(Path.Combine(_root, "git-workflow", "references"));
        File.WriteAllText(Path.Combine(_root, "git-workflow", "SKILL.md"), "---\nname: git-workflow\ndescription: Git conventions\nlicense: MIT\n---\n\n# Git\n");
        File.WriteAllText(Path.Combine(_root, "git-workflow", "references", "STYLE.md"), "# Style\n");

        Directory.CreateDirectory(Path.Combine(_root, "refunds"));
        File.WriteAllText(Path.Combine(_root, "refunds", "SKILL.md"), "---\nname: refunds\ndescription: >\n  Process refunds\n  per policy.\nmetadata:\n  version: \"2.1.0\"\n---\n");

        // Not a skill: no SKILL.md. Must be ignored.
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "README.md"), "not a skill");

        // A file at the root is ignored too.
        File.WriteAllText(Path.Combine(_root, "README.md"), "about these skills");

        McpServerBuilder.WithSkillsFromDirectory(_root, uriPrefix: "skill://acme/billing");
        StartServer();
    }

    [Fact]
    public async Task ServesEverySkillDirectory_WithFrontmatterFromTheFile()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var skills = await client.ListSkillsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, skills.Count);

        var gitWorkflow = Assert.Single(skills, s => s.Uri == "skill://acme/billing/git-workflow/SKILL.md");
        Assert.Equal("MIT", gitWorkflow.Frontmatter["license"]?.GetValue<string>());
        Assert.Equal(2, gitWorkflow.Resources.Resources!.Count);
        Assert.Contains(gitWorkflow.Resources.Resources, r => r.Uri == "skill://acme/billing/git-workflow/references/STYLE.md");

        var refunds = Assert.Single(skills, s => s.Uri == "skill://acme/billing/refunds/SKILL.md");
        Assert.Equal("Process refunds per policy.\n", refunds.Description);
        Assert.Equal("2.1.0", refunds.Frontmatter["metadata"]?["version"]?.GetValue<string>());
    }

    [Fact]
    public async Task ServedFiles_VerifyAgainstTheirManifest()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var skill = await client.GetSkillAsync("skill://acme/billing/git-workflow/SKILL.md", TestContext.Current.CancellationToken);

        foreach (var file in skill.Resources.Resources!)
        {
            var result = await client.ReadSkillResourceAsync(skill, file.Uri, TestContext.Current.CancellationToken);
            Assert.Single(result.Contents);
        }
    }

    [Fact]
    public void WithSkillsFromDirectory_RejectsDirectoriesWithoutSkills()
    {
        string empty = Path.Combine(Path.GetTempPath(), "mcp-skills-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(empty, "not-a-skill"));
        try
        {
            var services = new ServiceCollection();
            var exception = Assert.Throws<ArgumentException>(() => services.AddMcpServer().WithSkillsFromDirectory(empty));
            Assert.Contains("No skill directories", exception.Message);

            Assert.Throws<DirectoryNotFoundException>(() => services.AddMcpServer().WithSkillsFromDirectory(Path.Combine(empty, "missing")));
            Assert.Throws<ArgumentException>(() => services.AddMcpServer().WithSkillsFromDirectory(empty, uriPrefix: ""));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }
}
