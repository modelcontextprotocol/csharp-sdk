using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpServerSkillsProviderTests(ITestOutputHelper output) : LoggedTest(output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedResultRetainsItsHintsAcrossModernLegacyAndModernRequests(bool get)
    {
        var entry = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n")]).Skill.ProtocolSkill!;
        var listResult = new ListSkillsResult { Skills = [entry], NextCursor = "application-cursor" };
        var getResult = new GetSkillResult { Skill = entry };
        Result source = get ? getResult : listResult;
        source.ResultType = "input";
        source.Meta = new JsonObject { ["example.com/result"] = "shared" };
        var hints = (ICacheableResult)source;
        hints.TimeToLive = TimeSpan.FromMinutes(2);
        hints.CacheScope = CacheScope.Public;

        foreach (string version in new[]
        {
            McpProtocolVersions.July2026ProtocolVersion,
            McpProtocolVersions.November2025ProtocolVersion,
            McpProtocolVersions.July2026ProtocolVersion,
        })
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithSkills(options =>
            {
                options.ListSkillsHandler = (_, _) => new(listResult);
                options.GetSkillHandler = (_, _) => new(getResult);
            });
            await using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
            Pipe input = new(), responses = new();
            await using var server = McpServer.Create(new StreamServerTransport(
                input.Reader.AsStream(), responses.Writer.AsStream()), options, LoggerFactory);
            var running = server.RunAsync(TestContext.Current.CancellationToken);
            try
            {
                var clientOptions = TestMcpClient.CreateOptions();
                clientOptions.ProtocolVersion = version;
                await using var client = await TestMcpClient.CreateAsync(new StreamClientTransport(
                    input.Writer.AsStream(), responses.Reader.AsStream()), clientOptions, LoggerFactory,
                    TestContext.Current.CancellationToken);
                var result = (await client.SendRequestAsync(new JsonRpcRequest
                {
                    Method = get ? SkillsProtocol.MethodSkillsGet : SkillsProtocol.MethodSkillsList,
                    Params = new JsonObject { ["uri"] = entry.Uri },
                }, TestContext.Current.CancellationToken)).Result!.AsObject();

                Assert.Equal("shared", result["_meta"]!["example.com/result"]!.GetValue<string>());
                if (get)
                {
                    Assert.Equal(entry.Uri, result["skill"]!["uri"]!.GetValue<string>());
                }
                else
                {
                    Assert.Equal(entry.Uri, Assert.Single(result["skills"]!.AsArray())!["uri"]!.GetValue<string>());
                    Assert.Equal("application-cursor", result["nextCursor"]!.GetValue<string>());
                }

                if (version == McpProtocolVersions.July2026ProtocolVersion)
                {
                    Assert.Equal("complete", result["resultType"]!.GetValue<string>());
                    Assert.Equal(120_000, result["ttlMs"]!.GetValue<long>());
                    Assert.Equal("public", result["cacheScope"]!.GetValue<string>());
                }
                else
                {
                    Assert.False(result.ContainsKey("resultType") || result.ContainsKey("ttlMs") || result.ContainsKey("cacheScope"));
                }

                Assert.Equal("input", source.ResultType);
                Assert.Equal(TimeSpan.FromMinutes(2), hints.TimeToLive);
                Assert.Equal(CacheScope.Public, hints.CacheScope);
            }
            finally
            {
                await server.DisposeAsync();
                await running.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionsOnlyServerWorksWithoutProvider_ForFixedSkillsAndCustomHandlers(bool custom)
    {
        const string Markdown = "---\nname: alpha\ndescription: Alpha\n---\n";
        var package = McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", Markdown)]);
        var skill = package.Skill;
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        if (custom)
        {
            builder.WithSkills(options =>
            {
                options.ListSkillsHandler = (context, _) =>
                {
                    Assert.Null(context.Services);
                    return new(new ListSkillsResult { Skills = [skill.ProtocolSkill!] });
                };

                options.GetSkillHandler = (context, _) =>
                {
                    Assert.Null(context.Services);
                    return new(new GetSkillResult { Skill = skill.ProtocolSkill! });
                };
            }).WithResources(package.Resources);
        }
        else
        {
            builder.WithSkillPackages([package]);
        }

        McpServerOptions options;
        await using (var provider = services.BuildServiceProvider(validateScopes: true))
        {
            options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        }

        Pipe input = new(), responses = new();
        await using var server = McpServer.Create(new StreamServerTransport(
            input.Reader.AsStream(), responses.Writer.AsStream()), options, LoggerFactory);
        Assert.Null(server.Services);
        var running = server.RunAsync(TestContext.Current.CancellationToken);
        try
        {
            await using var client = await TestMcpClient.CreateAsync(new StreamClientTransport(
                input.Writer.AsStream(), responses.Reader.AsStream()), loggerFactory: LoggerFactory,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(skill.Id, Assert.Single(await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken)).Uri);
            var entry = await client.GetSkillAsync(skill.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(Markdown, Assert.IsType<TextResourceContents>(Assert.Single((await client.ReadSkillResourceAsync(
                entry, entry.Uri, cancellationToken: TestContext.Current.CancellationToken)).Contents)).Text);
        }
        finally
        {
            await server.DisposeAsync();
            await running.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        }
    }
}
