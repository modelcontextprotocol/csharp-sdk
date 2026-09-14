using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// End-to-end tests for the SEP-2640 skills methods over the client-server transport: capability
/// declaration, <c>skills/list</c> enumeration and pagination, <c>skills/get</c> retrieval including
/// skills absent from the listing, and the error contract for unknown URIs.
/// </summary>
public class McpServerSkillsTests : ClientServerTestBase
{
    private const string ListedSkillUri = "skill://git-workflow/SKILL.md";
    private const string NestedSkillUri = "skill://acme/billing/refunds/SKILL.md";

    public McpServerSkillsTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        var catalog = new InMemoryMcpSkillCatalog(
        [
            CreateSkill(ListedSkillUri, "git-workflow", SkillResources.FromResources(
            [
                new SkillResource
                {
                    Uri = ListedSkillUri,
                    Digest = "sha256:" + new string('a', 64),
                    Size = 2314,
                },
            ])),
            CreateSkill(NestedSkillUri, "refunds", SkillResources.Dynamic),
        ]);

        mcpServerBuilder.WithSkills(catalog, options =>
        {
            options.TimeToLive = TimeSpan.FromMinutes(5);
            options.CacheScope = CacheScope.Public;
        });
    }

    private static SkillEntry CreateSkill(string uri, string name, SkillResources resources) => new()
    {
        Uri = uri,
        Frontmatter = new JsonObject
        {
            ["name"] = name,
            ["description"] = $"The {name} skill",
        },
        Resources = resources,
    };

    private async Task<JsonNode?> SendAsync(McpClient client, string method, JsonObject? parameters)
    {
        var request = new JsonRpcRequest
        {
            Method = method,
            Params = parameters,
        };

        var response = await client.SendRequestAsync(request, TestContext.Current.CancellationToken);
        return response.Result;
    }

    [Fact]
    public async Task Server_DeclaresSkillsExtensionCapability()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var extensions = client.ServerCapabilities.Extensions;

        Assert.NotNull(extensions);
        Assert.True(extensions.ContainsKey(SkillsProtocol.ExtensionId));
    }

    [Fact]
    public async Task Server_DoesNotDeclareDirectoryRead_WhenNotSupported()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var settings = JsonSerializer.SerializeToNode(
            client.ServerCapabilities.Extensions![SkillsProtocol.ExtensionId])?.AsObject();

        Assert.NotNull(settings);
        Assert.False(settings.ContainsKey(SkillsProtocol.DirectoryReadSetting));
    }

    [Fact]
    public async Task SkillsList_ReturnsEveryPublishedSkill()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await SendAsync(client, SkillsProtocol.MethodSkillsList, []);
        var listing = result.Deserialize(McpSkillsJsonContext.Default.ListSkillsResult);

        Assert.NotNull(listing);
        Assert.Equal(2, listing.Skills.Count);
        Assert.Contains(listing.Skills, skill => skill.Uri == ListedSkillUri);
        Assert.Contains(listing.Skills, skill => skill.Uri == NestedSkillUri);
    }

    [Fact]
    public async Task SkillsList_MarksResultComplete()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await SendAsync(client, SkillsProtocol.MethodSkillsList, []);

        Assert.Equal("complete", result?["resultType"]?.GetValue<string>());
    }

    [Fact]
    public async Task SkillsList_SerializesEnumeratedManifestAsArray()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await SendAsync(client, SkillsProtocol.MethodSkillsList, []);

        var entry = result!["skills"]!.AsArray()
            .Single(skill => skill!["uri"]!.GetValue<string>() == ListedSkillUri)!;
        var resources = entry["resources"]!.AsArray();

        Assert.Single(resources);
        Assert.Equal(2314, resources[0]!["size"]!.GetValue<long>());
        Assert.StartsWith("sha256:", resources[0]!["digest"]!.GetValue<string>());
    }

    [Fact]
    public async Task SkillsList_SerializesDynamicManifestAsTheDynamicString()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await SendAsync(client, SkillsProtocol.MethodSkillsList, []);

        var entry = result!["skills"]!.AsArray()
            .Single(skill => skill!["uri"]!.GetValue<string>() == NestedSkillUri)!;

        Assert.Equal("dynamic", entry["resources"]!.GetValue<string>());
    }

    [Fact]
    public async Task SkillsList_PreservesFrontmatterVerbatim()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await SendAsync(client, SkillsProtocol.MethodSkillsList, []);

        var entry = result!["skills"]!.AsArray()
            .Single(skill => skill!["uri"]!.GetValue<string>() == ListedSkillUri)!;
        var frontmatter = entry["frontmatter"]!.AsObject();

        Assert.Equal("git-workflow", frontmatter["name"]!.GetValue<string>());
        Assert.Equal("The git-workflow skill", frontmatter["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task SkillsGet_ReturnsTheRequestedEntry()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await SendAsync(
            client,
            SkillsProtocol.MethodSkillsGet,
            new JsonObject { ["uri"] = ListedSkillUri });
        var skill = result.Deserialize(McpSkillsJsonContext.Default.GetSkillResult);

        Assert.NotNull(skill);
        Assert.Equal(ListedSkillUri, skill.Skill.Uri);
        Assert.False(skill.Skill.Resources.IsDynamic);
    }

    [Fact]
    public async Task SkillsGet_CarriesNoPaginationCursor()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await SendAsync(
            client,
            SkillsProtocol.MethodSkillsGet,
            new JsonObject { ["uri"] = ListedSkillUri });

        Assert.False(result!.AsObject().ContainsKey("nextCursor"));
    }

    [Fact]
    public async Task SkillsGet_WithUnknownUri_ReturnsInvalidParams()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await SendAsync(
                client,
                SkillsProtocol.MethodSkillsGet,
                new JsonObject { ["uri"] = "skill://missing/SKILL.md" }));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public async Task SkillsGet_WithMissingUri_ReturnsInvalidParams()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await SendAsync(client, SkillsProtocol.MethodSkillsGet, []));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }
}
