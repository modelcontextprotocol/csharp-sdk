using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

public class SkillsRequestOptionsTests : ClientServerTestBase
{
    private const string Markdown = "---\nname: alpha\ndescription: Alpha\n---\n";
    private readonly McpServerSkill _skill = McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", Markdown)]).Skill;
    private readonly List<JsonObject?> _metadata = [];

    public SkillsRequestOptionsTests(ITestOutputHelper output) : base(output, startServer: false)
    {
        McpServerBuilder.WithSkills(options =>
        {
            options.ListSkillsHandler = (context, _) =>
            {
                _metadata.Add(context.Params.Meta);
                return new(new ListSkillsResult
                {
                    Skills = context.Params.Cursor is null ? [_skill.ProtocolSkill!] : [],
                    NextCursor = context.Params.Cursor is null ? "next" : null,
                });
            };

            options.GetSkillHandler = (context, _) =>
            {
                _metadata.Add(context.Params.Meta);
                return new(new GetSkillResult { Skill = _skill.ProtocolSkill! });
            };
        }).WithReadResourceHandler((context, _) =>
        {
            _metadata.Add(context.Params.Meta);
            return new(new ReadResourceResult { Contents = [new TextResourceContents { Uri = _skill.Id, Text = Markdown }] });
        });

        StartServer();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConvenienceRequestsForwardMetadataAndProgress_WithoutMutatingOptions(bool integer)
    {
        var metadata = new JsonObject { ["example.com/request"] = "value", ["progressToken"] = "old" };
        var options = new RequestOptions { Meta = metadata, ProgressToken = integer ? new ProgressToken(42) : new ProgressToken("progress") };
        await using var client = await CreateMcpClientForServer();
        var skills = await client.ListSkillsAsync(options, TestContext.Current.CancellationToken);
        Assert.Single(skills);
        var skill = await client.GetSkillAsync(_skill.Id, options, TestContext.Current.CancellationToken);
        Assert.Single((await client.ReadSkillResourceAsync(skill, skill.Uri, options, TestContext.Current.CancellationToken)).Contents);
        Assert.Equal(4, _metadata.Count);
        Assert.All(_metadata, meta =>
        {
            Assert.Equal("value", meta!["example.com/request"]!.GetValue<string>());
            if (integer) Assert.Equal(42, meta["progressToken"]!.GetValue<int>());
            else Assert.Equal("progress", meta["progressToken"]!.GetValue<string>());
        });

        Assert.Equal("old", metadata["progressToken"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("read")]
    public async Task ConvenienceRequestsPropagateCancellation(string operation)
    {
        await using var client = await CreateMcpClientForServer();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var options = new RequestOptions { Meta = new JsonObject { ["example.com/request"] = "value" } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            switch (operation)
            {
                case "list": await client.ListSkillsAsync(options, cancellation.Token); break;
                case "get": await client.GetSkillAsync(_skill.Id, options, cancellation.Token); break;
                case "read": await client.ReadSkillResourceAsync(_skill.ProtocolSkill!, _skill.Id, options, cancellation.Token); break;
            }
        });

        Assert.Empty(_metadata);
    }
}
