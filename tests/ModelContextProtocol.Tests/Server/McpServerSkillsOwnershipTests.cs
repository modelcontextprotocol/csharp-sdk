using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpServerSkillsOwnershipTests : ClientServerTestBase
{
    private const string CatalogUri = "skill://catalog/SKILL.md";
    private const string CatalogMarkdown = "---\nname: catalog\ndescription: From the file\n---\n# Instructions\n";
    private int _catalogReadCount;

    public McpServerSkillsOwnershipTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        var files = CreatePrevalidatedSkill("provided-files", fromDirectory: false);
        var directory = CreatePrevalidatedSkill("provided-directory", fromDirectory: true);
        var catalog = new Skill
        {
            Uri = CatalogUri,
            Frontmatter = new JsonObject { ["name"] = "catalog", ["description"] = "Advertised metadata" },
            Resources = SkillResources.FromResources(
            [
                new SkillResource
                {
                    Uri = CatalogUri,
                    Digest = SkillVerifier.ComputeDigest(Encoding.UTF8.GetBytes(CatalogMarkdown)),
                    Size = Encoding.UTF8.GetByteCount(CatalogMarkdown),
                },
            ]),
        };

        mcpServerBuilder
            .WithSkills(new InMemoryMcpSkillCatalog([files.ProtocolSkill, directory.ProtocolSkill, catalog]))
            .WithResources(files.Resources)
            .WithResources(directory.Resources)
            .WithResources(
            [
                McpServerResource.Create(() =>
                {
                    Interlocked.Increment(ref _catalogReadCount);
                    return CatalogMarkdown;
                }, new McpServerResourceCreateOptions { UriTemplate = CatalogUri, MimeType = "text/markdown" }),
            ]);
    }

    [Fact]
    public async Task CatalogListAndGet_TransportStructuredMetadataWithoutReadingFiles()
    {
        await using var client = await CreateMcpClientForServer();

        var listed = await client.ListSkillsAsync(TestContext.Current.CancellationToken);
        var retrieved = await client.GetSkillAsync(CatalogUri, TestContext.Current.CancellationToken);

        Assert.Equal("Advertised metadata", Assert.Single(listed, skill => skill.Uri == CatalogUri).Description);
        Assert.Equal("Advertised metadata", retrieved.Description);
        Assert.Equal(0, _catalogReadCount);
    }

    [Theory]
    [InlineData("provided-files")]
    [InlineData("provided-directory")]
    public async Task ExplicitAuthoring_PreservesPrevalidatedUnsupportedMetadataAndBytes(string name)
    {
        await using var client = await CreateMcpClientForServer();
        var uri = $"skill://{name}/SKILL.md";
        var entry = await client.GetSkillAsync(uri, TestContext.Current.CancellationToken);
        var contents = await client.ReadSkillResourceAsync(entry, uri, TestContext.Current.CancellationToken);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(contents.Contents)).Text;

        Assert.True(JsonNode.DeepEquals(PrevalidatedFrontmatter(name), entry.Frontmatter));
        Assert.Equal(UnsupportedMarkdown(name), text);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), Assert.Single(entry.Resources.Resources!).Size);
        Assert.Equal(SkillVerifier.ComputeDigest(Encoding.UTF8.GetBytes(text)), Assert.Single(entry.Resources.Resources!).Digest);
    }

    [Fact]
    public async Task DigestVerifiedRead_DoesNotEstablishFrontmatterEquality()
    {
        await using var client = await CreateMcpClientForServer();
        var entry = await client.GetSkillAsync(CatalogUri, TestContext.Current.CancellationToken);
        var result = await client.ReadSkillResourceAsync(entry, CatalogUri, TestContext.Current.CancellationToken);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;

        Assert.Equal(CatalogMarkdown, text);
        Assert.Equal(1, _catalogReadCount);
        Assert.Throws<SkillVerificationException>(() => VerifyHostFrontmatter(entry, text));
    }

    [Fact]
    public async Task HostRejectsUnsupportedFetchedYaml_InsteadOfTrustingServerJson()
    {
        await using var client = await CreateMcpClientForServer();
        var entry = await client.GetSkillAsync("skill://provided-files/SKILL.md", TestContext.Current.CancellationToken);
        var result = await client.ReadSkillResourceAsync(entry, entry.Uri, TestContext.Current.CancellationToken);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;

        Assert.True(JsonNode.DeepEquals(PrevalidatedFrontmatter("provided-files"), entry.Frontmatter));
        var exception = Assert.Throws<SkillVerificationException>(() => VerifyHostFrontmatter(entry, text));
        Assert.IsAssignableFrom<FormatException>(exception.InnerException);
    }

    private McpServerSkill CreatePrevalidatedSkill(string name, bool fromDirectory)
    {
        var markdown = UnsupportedMarkdown(name);
        var bytes = Encoding.UTF8.GetBytes(markdown);
        var supplied = PrevalidatedFrontmatter(name);
        var uri = $"skill://{name}/SKILL.md";
        if (fromDirectory)
        {
            var path = Path.Combine(Path.GetTempPath(), "mcp-skill-prevalidated-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                File.WriteAllBytes(Path.Combine(path, "SKILL.md"), bytes);
                var skill = McpServerSkill.CreateFromDirectory(uri, supplied, path);
                File.WriteAllText(Path.Combine(path, "SKILL.md"), "changed after creation");
                supplied["description"] = "changed after creation";
                return skill;
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        var result = McpServerSkill.Create(uri, supplied, [new McpServerSkillFile { Path = "SKILL.md", Content = bytes }]);
        bytes[bytes.Length - 2] = (byte)'X';
        supplied["description"] = "changed after creation";
        return result;
    }

    private string UnsupportedMarkdown(string name) =>
        $"---\nname: &name {name}\ndescription: Provided\nmetadata:\n  owner: &owner team\n  reviewer: *owner\n---\n# Instructions\n";

    private JsonObject PrevalidatedFrontmatter(string name) => new()
    {
        ["name"] = name,
        ["description"] = "Provided",
        ["metadata"] = new JsonObject { ["owner"] = "team", ["reviewer"] = "team" },
    };

    private void VerifyHostFrontmatter(Skill entry, string markdown)
    {
        try
        {
            var parsed = SkillFrontmatter.Parse(markdown);
            if (!JsonNode.DeepEquals(parsed, entry.Frontmatter))
            {
                throw new SkillVerificationException("The fetched frontmatter differs from the held entry.");
            }
        }
        catch (FormatException exception)
        {
            throw new SkillVerificationException("The fetched frontmatter could not be verified.", exception);
        }
    }
}
