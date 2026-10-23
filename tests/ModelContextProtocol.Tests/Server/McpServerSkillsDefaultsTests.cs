using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that a server configured with <c>WithSkills</c> and no options still satisfies the 2026-07-28
/// requirement that <c>skills/list</c> and <c>skills/get</c> carry <c>ttlMs</c> and <c>cacheScope</c>, using the same conservative
/// defaults the SDK applies to the built-in list methods.
/// </summary>
public class McpServerSkillsDefaultsTests : ClientServerTestBase
{
    public McpServerSkillsDefaultsTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithSkills(
        [
            McpServerSkill.Create(
                "skill://alpha/SKILL.md",
                new JsonObject { ["name"] = "alpha", ["description"] = "d" },
                [McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: d\n---\n")]),
        ]);
    }

    [Fact]
    public async Task SkillsList_WithDefaultOptions_On2026_07_28_CarriesConservativeCacheHints()
    {
        await using McpClient client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        var result = (await client.SendRequestAsync(
            new JsonRpcRequest { Method = SkillsProtocol.MethodSkillsList },
            TestContext.Current.CancellationToken)).Result!.AsObject();

        Assert.Equal("complete", result["resultType"]?.GetValue<string>());
        Assert.Equal(0, result["ttlMs"]?.GetValue<long>());
        Assert.Equal("private", result["cacheScope"]?.GetValue<string>());

        var typed = await client.ListSkillsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.Zero, typed.TimeToLive);
        Assert.Equal(CacheScope.Private, typed.CacheScope);
    }

    [Theory]
    [InlineData(McpProtocolVersions.July2026ProtocolVersion, true)]
    [InlineData(McpProtocolVersions.November2025ProtocolVersion, false)]
    public async Task SkillsGet_WithDefaultOptions_GatesCacheHintsByProtocolVersion(string protocolVersion, bool includesCacheHints)
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = protocolVersion });
        var result = (await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = SkillsProtocol.MethodSkillsGet,
                Params = new JsonObject { ["uri"] = "skill://alpha/SKILL.md" },
            },
            TestContext.Current.CancellationToken)).Result!.AsObject();
        var typed = await client.GetSkillAsync(
            new GetSkillRequestParams { Uri = "skill://alpha/SKILL.md" }, TestContext.Current.CancellationToken);
        var cacheable = Assert.IsAssignableFrom<ICacheableResult>(typed);

        Assert.Equal("skill://alpha/SKILL.md", result["skill"]!["uri"]!.GetValue<string>());
        Assert.False(result.ContainsKey("nextCursor"));
        if (includesCacheHints)
        {
            Assert.Equal("complete", result["resultType"]?.GetValue<string>());
            Assert.Equal(0, result["ttlMs"]?.GetValue<long>());
            Assert.Equal("private", result["cacheScope"]?.GetValue<string>());
            Assert.Equal(TimeSpan.Zero, cacheable.TimeToLive);
            Assert.Equal(CacheScope.Private, cacheable.CacheScope);
        }
        else
        {
            Assert.False(result.ContainsKey("resultType"));
            Assert.False(result.ContainsKey("ttlMs"));
            Assert.False(result.ContainsKey("cacheScope"));
            Assert.Null(typed.ResultType);
            Assert.Null(cacheable.TimeToLive);
            Assert.Null(cacheable.CacheScope);
        }
    }

    [Fact]
    public async Task ClientExtensions_ThrowWhenServerDoesNotDeclareSkills()
    {
        // A client connected to this server sees the extension; fake its absence to exercise the guard.
        await using McpClient client = await CreateMcpClientForServer();
        client.ServerCapabilities.Extensions!.Remove(SkillsProtocol.ExtensionId);

        Assert.False(client.SupportsSkills());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.GetSkillAsync("skill://alpha/SKILL.md", TestContext.Current.CancellationToken));
    }
}
