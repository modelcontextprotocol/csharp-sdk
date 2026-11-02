using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpServerNestedUnicodeSkillTests(ITestOutputHelper output) : ClientServerTestBase(output)
{
    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        var files = new List<McpServerSkillFile>
        {
            McpServerSkillFile.FromText("SKILL.md", "---\nname: outer\ndescription: Parent skill\n---\n"),
        };
        var skills = new List<McpServerSkill>();
        foreach (var name in new[] { "café", "数据分析", "\U00010428" })
        {
            var markdown = $"---\nname: {name}\ndescription: Nested skill\n---\n";
            files.Add(McpServerSkillFile.FromText($"{name}/SKILL.md", markdown));
            skills.Add(McpServerSkill.Create(
                $"skill://outer/{Uri.EscapeDataString(name)}/SKILL.md",
                [McpServerSkillFile.FromText("SKILL.md", markdown)]));
        }
        skills.Add(McpServerSkill.Create("skill://outer/SKILL.md", files));
        mcpServerBuilder.WithSkills(skills);
    }

    [Theory]
    [InlineData("café")]
    [InlineData("数据分析")]
    [InlineData("\U00010428")]
    public async Task NestedUnicodeSkillsListGetAndShareVerifiedResources(string name)
    {
        await using var client = await CreateMcpClientForServer();
        var skills = await client.ListSkillsAsync(TestContext.Current.CancellationToken);
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var uri = $"skill://outer/{Uri.EscapeDataString(name)}/SKILL.md";

        Assert.Equal(4, skills.Count);
        Assert.Equal(4, resources.Count);
        Assert.Contains(skills, skill => skill.Uri == uri);
        Assert.Contains(resources, resource => resource.Uri == uri);

        var inner = await client.GetSkillAsync(uri, TestContext.Current.CancellationToken);
        var outer = await client.GetSkillAsync("skill://outer/SKILL.md", TestContext.Current.CancellationToken);
        Assert.Equal(name, inner.Name);
        Assert.Contains(outer.Resources.Resources!, resource => resource.Uri == uri);

        var innerRead = await client.ReadSkillResourceAsync(inner, uri, TestContext.Current.CancellationToken);
        var outerRead = await client.ReadSkillResourceAsync(outer, uri, TestContext.Current.CancellationToken);
        var innerText = Assert.IsType<TextResourceContents>(Assert.Single(innerRead.Contents));
        var outerText = Assert.IsType<TextResourceContents>(Assert.Single(outerRead.Contents));
        Assert.Equal(uri, innerText.Uri);
        Assert.Equal(innerText.Text, outerText.Text);
        Assert.Equal(name, SkillFrontmatter.Parse(innerText.Text!)["name"]!.GetValue<string>());
    }
}
