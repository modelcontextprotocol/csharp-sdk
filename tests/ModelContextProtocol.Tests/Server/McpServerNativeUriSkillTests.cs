using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpServerNativeUriSkillTests(ITestOutputHelper output) : ClientServerTestBase(output)
{
    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithSkills(
            new[]
            {
                (Uri: "repo:/skills/alpha/SKILL.md", Name: "alpha"),
                (Uri: "repo:skills/alpha/SKILL.md", Name: "alpha"),
                (Uri: "repo:alpha/SKILL.md", Name: "alpha"),
                (Uri: "urn:alpha/SKILL.md", Name: "alpha"),
                (Uri: "repo:/skills/caf%C3%A9/SKILL.md", Name: "café"),
                (Uri: "repo:skills/caf%C3%A9/SKILL.md", Name: "café"),
            }.Select(entry => McpServerSkill.Create(
                entry.Uri, [McpServerSkillFile.FromText("SKILL.md", $"---\nname: {entry.Name}\ndescription: Native resource\n---\n")])));
    }

    [Theory]
    [InlineData("repo:/skills/alpha/SKILL.md")]
    [InlineData("repo:skills/alpha/SKILL.md")]
    [InlineData("repo:alpha/SKILL.md")]
    [InlineData("urn:alpha/SKILL.md")]
    [InlineData("repo:/skills/caf%C3%A9/SKILL.md")]
    [InlineData("repo:skills/caf%C3%A9/SKILL.md")]
    public async Task NativeResourceUrisListGetAndVerifyWithoutRewritingTheirIdentity(string uri)
    {
        await using var client = await CreateMcpClientForServer();
        var skills = await client.ListSkillsAsync(TestContext.Current.CancellationToken);
        var fetched = await client.GetSkillAsync(uri, TestContext.Current.CancellationToken);
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(6, skills.Count);
        Assert.Equal(uri, fetched.Uri);
        Assert.Contains(resources, resource => resource.Uri == uri);
        var read = await client.ReadSkillResourceAsync(fetched, uri, TestContext.Current.CancellationToken);
        Assert.Equal(uri, Assert.Single(read.Contents).Uri);
        Assert.IsType<TextResourceContents>(read.Contents[0]);
    }
}
