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

        var entries = new[] { files.Skill.ProtocolSkill!, directory.Skill.ProtocolSkill!, catalog };
        mcpServerBuilder
            .WithSkills(options =>
            {
                options.ListSkillsHandler = (_, _) => new(new ListSkillsResult { Skills = entries });
                options.GetSkillHandler = (context, _) => new(new GetSkillResult
                {
                    Skill = entries.Single(entry => entry.Uri == context.Params.Uri),
                });
            })
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

        var listed = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var retrieved = await client.GetSkillAsync(CatalogUri, cancellationToken: TestContext.Current.CancellationToken);

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
        var entry = await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken);
        var contents = await client.ReadSkillResourceAsync(entry, uri, cancellationToken: TestContext.Current.CancellationToken);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(contents.Contents)).Text;

        Assert.True(JsonNode.DeepEquals(PrevalidatedFrontmatter(name), entry.Frontmatter));
        Assert.Equal(UnsupportedMarkdown(name), text);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), Assert.Single(entry.Resources.Entries!).Size);
        Assert.Equal(SkillVerifier.ComputeDigest(Encoding.UTF8.GetBytes(text)), Assert.Single(entry.Resources.Entries!).Digest);
    }

    [Fact]
    public async Task DigestVerifiedRead_DoesNotEstablishFrontmatterEquality()
    {
        await using var client = await CreateMcpClientForServer();
        var entry = await client.GetSkillAsync(CatalogUri, cancellationToken: TestContext.Current.CancellationToken);
        var result = await client.ReadSkillResourceAsync(entry, CatalogUri, cancellationToken: TestContext.Current.CancellationToken);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;

        Assert.Equal("Advertised metadata", entry.Description);
        Assert.Equal(CatalogMarkdown, text);
        Assert.Equal(1, _catalogReadCount);
    }

    private McpServerSkillPackage CreatePrevalidatedSkill(string name, bool fromDirectory)
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
                var skill = McpServerSkillPackage.CreateFromDirectory(path, new McpServerSkillPackageCreateOptions { Uri = uri, Frontmatter = supplied });
                File.WriteAllText(Path.Combine(path, "SKILL.md"), "changed after creation");
                return skill;
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        var result = McpServerSkillPackage.Create([new McpServerSkillFile { Path = "SKILL.md", Content = bytes }], new McpServerSkillPackageCreateOptions { Uri = uri, Frontmatter = supplied });
        bytes[bytes.Length - 2] = (byte)'X';
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
}
