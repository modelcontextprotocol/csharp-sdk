using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// End-to-end tests for skills registered through <see cref="McpServerSkill"/>: capability declaration, listing,
/// retrieval, automatic resource registration, and verified reads through the client extensions.
/// </summary>
public class McpServerSkillsTests : ClientServerTestBase
{
    private const string GitWorkflowUri = "skill://git-workflow/SKILL.md";
    private const string GitWorkflowMarkdown = "---\nname: git-workflow\ndescription: Git conventions\nlicense: MIT\n---\n\n# Git workflow\n";
    private const string RefundsUri = "skill://acme/billing/refunds/SKILL.md";
    private static readonly byte[] s_binaryFile = [0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE, 0x00];

    public McpServerSkillsTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        var gitWorkflow = McpServerSkill.Create(
            GitWorkflowUri,
            new JsonObject { ["name"] = "git-workflow", ["description"] = "Git conventions", ["license"] = "MIT" },
            [
                McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown),
                McpServerSkillFile.FromText("references/GUIDE.md", "# Guide\n"),
                new McpServerSkillFile { Path = "assets/logo.png", Content = s_binaryFile },
            ]);

        var refunds = McpServerSkill.Create(
            RefundsUri,
            new JsonObject { ["name"] = "refunds", ["description"] = "Process refunds" },
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: refunds\ndescription: Process refunds\n---\n")]);

        mcpServerBuilder.WithSkills([gitWorkflow, refunds]);
    }

    [Fact]
    public async Task Server_DeclaresSkillsExtensionAndResourcesCapability()
    {
        await using McpClient client = await CreateMcpClientForServer();

        Assert.True(client.SupportsSkills());
        Assert.NotNull(client.ServerCapabilities.Resources);

        var settings = System.Text.Json.JsonSerializer.SerializeToNode(client.ServerCapabilities.Extensions![SkillsProtocol.ExtensionId], McpJsonUtilities.DefaultOptions)?.AsObject();
        Assert.NotNull(settings);
        Assert.Empty(settings);
    }

    [Fact]
    public async Task ListSkillsAsync_ReturnsEveryRegisteredSkill()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var skills = await client.ListSkillsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, skills.Count);
        var gitWorkflow = Assert.Single(skills, s => s.Uri == GitWorkflowUri);
        Assert.Equal("git-workflow", gitWorkflow.Name);
        Assert.Equal("MIT", gitWorkflow.Frontmatter["license"]?.GetValue<string>());
        Assert.Equal(3, gitWorkflow.Resources.Resources!.Count);
        Assert.Contains(skills, s => s.Uri == RefundsUri && s.Name == "refunds");
    }

    [Fact]
    public async Task GetSkillAsync_ReturnsTheRequestedEntry()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var skill = await client.GetSkillAsync(RefundsUri, TestContext.Current.CancellationToken);

        Assert.Equal(RefundsUri, skill.Uri);
        Assert.Equal("refunds", skill.Name);
        Assert.Single(skill.Resources.Resources!);
    }

    [Fact]
    public async Task GetSkillAsync_WithUnknownUri_ReturnsInvalidParams()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync("skill://missing/SKILL.md", TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public async Task SkillFiles_AreListedAsResources()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var skillFile = Assert.Single(resources, r => r.Uri == GitWorkflowUri);
        Assert.Equal("git-workflow", skillFile.Name);
        Assert.Equal("Git conventions", skillFile.Description);
        Assert.Equal("text/markdown", skillFile.MimeType);
        Assert.Contains(resources, r => r.Uri == "skill://git-workflow/references/GUIDE.md");
        Assert.Contains(resources, r => r.Uri == "skill://git-workflow/assets/logo.png" && r.MimeType == "image/png");
        Assert.Contains(resources, r => r.Uri == RefundsUri);
    }

    [Fact]
    public async Task ReadSkillResourceAsync_ReturnsVerifiedText()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var skill = await client.GetSkillAsync(GitWorkflowUri, TestContext.Current.CancellationToken);

        var result = await client.ReadSkillResourceAsync(skill, GitWorkflowUri, TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal(GitWorkflowMarkdown, text.Text);
        Assert.Equal("text/markdown", text.MimeType);
    }

    [Fact]
    public async Task ReadSkillResourceAsync_ReturnsVerifiedBlobForBinaryFile()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var skill = await client.GetSkillAsync(GitWorkflowUri, TestContext.Current.CancellationToken);

        var result = await client.ReadSkillResourceAsync(skill, "skill://git-workflow/assets/logo.png", TestContext.Current.CancellationToken);

        var blob = Assert.IsType<BlobResourceContents>(Assert.Single(result.Contents));
        Assert.Equal(s_binaryFile, blob.DecodedData.ToArray());
        Assert.Equal("image/png", blob.MimeType);
    }

    [Fact]
    public async Task ReadSkillResourceAsync_RejectsUnlistedFileWithoutContactingServer()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var skill = await client.GetSkillAsync(GitWorkflowUri, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SkillVerificationException>(
            async () => await client.ReadSkillResourceAsync(skill, "skill://git-workflow/references/NEW.md", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadSkillResourceAsync_DetectsContentThatNoLongerMatchesTheHeldEntry()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var skill = await client.GetSkillAsync(GitWorkflowUri, TestContext.Current.CancellationToken);

        // Simulate a stale held entry: the host approved the skill against a different manifest.
        var stale = skill.Resources.Resources!.Select(r => new SkillResource { Uri = r.Uri, Digest = r.Digest, Size = r.Size }).ToList();
        stale[0].Digest = SkillVerifier.ComputeDigest(Encoding.UTF8.GetBytes("previous content"));
        stale[0].Size = Encoding.UTF8.GetByteCount("previous content");
        skill.Resources = SkillResources.FromResources(stale);

        await Assert.ThrowsAsync<SkillVerificationException>(
            async () => await client.ReadSkillResourceAsync(skill, GitWorkflowUri, TestContext.Current.CancellationToken));
    }
}
