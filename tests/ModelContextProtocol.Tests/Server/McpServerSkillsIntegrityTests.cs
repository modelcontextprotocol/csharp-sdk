using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// End-to-end regression tests for two integrity properties of the Skills extension: the bytes a
/// <see cref="McpServerSkill"/> serves are the bytes its manifest describes, even if the caller mutates its
/// buffer afterwards; and a verified read is bound to the file that was requested, not merely to files of the
/// same skill.
/// </summary>
public class McpServerSkillsIntegrityTests : ClientServerTestBase
{
    private const string SnapshotUri = "skill://snapshot/SKILL.md";
    private const string SwapUri = "skill://swap/SKILL.md";
    private const string SwapOtherUri = "skill://swap/other.md";
    private const string OriginalMarkdown = "---\nname: snapshot\ndescription: d\n---\noriginal\n";

    public McpServerSkillsIntegrityTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // A fresh buffer per fixture instance: ConfigureServices runs once per test, and the mutation below must
        // not leak into the next test's skill.
        byte[] callerOwnedBuffer = Encoding.UTF8.GetBytes(OriginalMarkdown);
        var snapshot = McpServerSkill.Create(
            SnapshotUri,
            new JsonObject { ["name"] = "snapshot", ["description"] = "d" },
            [new McpServerSkillFile { Path = "SKILL.md", Content = callerOwnedBuffer }]);

        // Mutate the caller's buffer after the skill was created. The served content must not change.
        callerOwnedBuffer[callerOwnedBuffer.Length - 2] = (byte)'X';

        // A misbehaving server: the entry for "swap" is correct, but a read of its SKILL.md is answered with the
        // skill's other file, correctly digested and correctly labelled with that other file's URI.
        const string SwapMarkdown = "---\nname: swap\ndescription: d\n---\n";
        const string SwapOther = "other content";
        var swapEntry = new Skill
        {
            Uri = SwapUri,
            Frontmatter = new JsonObject { ["name"] = "swap", ["description"] = "d" },
            Resources = SkillResources.FromResources(
            [
                new SkillResource { Uri = SwapUri, Digest = SkillVerifier.ComputeDigest(Encoding.UTF8.GetBytes(SwapMarkdown)), Size = SwapMarkdown.Length },
                new SkillResource { Uri = SwapOtherUri, Digest = SkillVerifier.ComputeDigest(Encoding.UTF8.GetBytes(SwapOther)), Size = SwapOther.Length },
            ]),
        };

        mcpServerBuilder
            .WithSkills(new InMemoryMcpSkillCatalog([snapshot.ProtocolSkill, swapEntry]))
            .WithResources(snapshot.Resources)
            .WithResources(
            [
                McpServerResource.Create(
                    () => new TextResourceContents { Uri = SwapOtherUri, MimeType = "text/markdown", Text = SwapOther },
                    new McpServerResourceCreateOptions { UriTemplate = SwapUri }),
                McpServerResource.Create(
                    () => new TextResourceContents { Uri = SwapOtherUri, MimeType = "text/markdown", Text = SwapOther },
                    new McpServerResourceCreateOptions { UriTemplate = SwapOtherUri }),
            ]);
    }

    [Fact]
    public async Task ServedContent_IsTheContentTheManifestDescribes_EvenAfterCallerMutatesItsBuffer()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var skill = await client.GetSkillAsync(SnapshotUri, TestContext.Current.CancellationToken);

        var result = await client.ReadSkillResourceAsync(skill, SnapshotUri, TestContext.Current.CancellationToken);

        Assert.Equal(OriginalMarkdown, Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text);
    }

    [Fact]
    public async Task ReadSkillResourceAsync_RejectsAResponseContainingOnlyADifferentFileOfTheSkill()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var skill = await client.GetSkillAsync(SwapUri, TestContext.Current.CancellationToken);

        // Sanity check: the substituted response is for a listed, correctly digested file.
        var raw = await client.ReadResourceAsync(SwapUri, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SwapOtherUri, Assert.Single(raw.Contents).Uri);

        var exception = await Assert.ThrowsAsync<SkillVerificationException>(
            async () => await client.ReadSkillResourceAsync(skill, SwapUri, TestContext.Current.CancellationToken));
        Assert.Contains(SwapUri, exception.Message);

        // Reading the other file directly still verifies.
        await client.ReadSkillResourceAsync(skill, SwapOtherUri, TestContext.Current.CancellationToken);
    }
}
