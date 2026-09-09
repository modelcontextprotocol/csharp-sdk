#pragma warning disable MCPEXP002 // Raw request handlers are the point of this test.

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// The client extensions validate every entry a server returns against the specification's structural
/// requirements, since hosts must not load invalid entries. These tests stand up a server whose raw
/// <c>skills/list</c> and <c>skills/get</c> handlers return entries the SDK's own server side would never publish.
/// </summary>
public class McpSkillsClientValidationTests : ClientServerTestBase
{
    public McpSkillsClientValidationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            options.Capabilities.Extensions[SkillsProtocol.ExtensionId] = new JsonObject();
            options.RequestHandlers ??= [];
            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = SkillsProtocol.MethodSkillsList,
                Handler = (_, _) => new ValueTask<JsonNode?>(JsonNode.Parse("""
                    { "skills": [ { "uri": "skill://good/SKILL.md", "frontmatter": { "name": "good", "description": "d" },
                                    "resources": [ { "uri": "skill://good/SKILL.md", "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "size": 1 } ] },
                                  { "uri": "skill://bad/SKILL.md", "frontmatter": { "name": "mismatch", "description": "d" },
                                    "resources": [ { "uri": "skill://bad/SKILL.md", "digest": "not-a-digest", "size": 1 } ] } ] }
                    """)),
            });
            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = SkillsProtocol.MethodSkillsGet,
                Handler = (_, _) => new ValueTask<JsonNode?>(JsonNode.Parse("""
                    { "skill": { "uri": "skill://escape/SKILL.md", "frontmatter": { "name": "escape", "description": "d" },
                                 "resources": [ { "uri": "skill://escape/SKILL.md", "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "size": 1 },
                                                { "uri": "skill://escape/../secret.md", "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "size": 1 } ] } }
                    """)),
            });
        });
    }

    [Fact]
    public async Task ListSkillsAsync_RejectsAnInvalidEntry()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<SkillVerificationException>(
            async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("skills/list", exception.Message);
        Assert.Contains("skill://bad/SKILL.md", exception.Message);
    }

    [Fact]
    public async Task GetSkillAsync_RejectsAnEntryWhoseManifestEscapesTheSkill()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<SkillVerificationException>(
            async () => await client.GetSkillAsync("skill://escape/SKILL.md", TestContext.Current.CancellationToken));

        Assert.Contains("skills/get", exception.Message);
        Assert.Contains("'..'", exception.Message);
    }
}
