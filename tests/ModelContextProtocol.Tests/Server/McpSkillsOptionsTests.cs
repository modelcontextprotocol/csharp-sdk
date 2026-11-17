using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Skills;

namespace ModelContextProtocol.Tests.Server;

public class McpSkillsOptionsTests
{
    [Fact]
    public async Task ConfigurationIsDeferredServiceAwareAndOrdered_AndPreservesTheSuppliedCollection()
    {
        var services = new ServiceCollection();
        var order = new List<string>();
        var collection = new McpServerSkillCollection();
        var skill = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n")]).Skill;
        services.AddSingleton(skill);
        services.Configure<McpSkillsOptions>(options =>
        {
            order.Add("configure");
            options.SkillCollection = collection;
        });

        services.AddMcpServer().WithSkills();
        services.AddOptions<McpSkillsOptions>().Configure<McpServerSkill>((options, registered) =>
        {
            order.Add("service");
            Assert.Same(skill, registered);
        }).Validate(options => options.SkillCollection.Count == 1, "The registered skill is required.");
        services.PostConfigure<McpSkillsOptions>(options => order.Add("post"));
        Assert.Empty(order);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.Empty(order);
        var options = provider.GetRequiredService<IOptions<McpSkillsOptions>>().Value;
        Assert.Equal(["configure", "service", "post"], order);
        Assert.Same(collection, options.SkillCollection);
        Assert.Same(skill, Assert.Single(collection));
        _ = provider.GetRequiredService<IOptions<McpSkillsOptions>>().Value;
        Assert.Equal(3, order.Count);
    }
}
