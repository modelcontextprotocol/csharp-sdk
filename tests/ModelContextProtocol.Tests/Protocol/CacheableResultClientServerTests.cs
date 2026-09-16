using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// End-to-end tests verifying that SEP-2549 caching hints set by a server on cacheable results
/// are observed by a connected client.
/// </summary>
public class CacheableResultClientServerTests(ITestOutputHelper testOutputHelper)
    : ClientServerTestBase(testOutputHelper)
{
    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder
            .WithListToolsHandler((_, _) => new ValueTask<ListToolsResult>(new ListToolsResult
            {
                Tools = [new Tool { Name = "echo" }],
                TimeToLive = TimeSpan.FromMinutes(5),
                CacheScope = CacheScope.Public,
            }))
            .WithListPromptsHandler((_, _) => new ValueTask<ListPromptsResult>(new ListPromptsResult
            {
                Prompts = [new Prompt { Name = "greet" }],
            }))
            .WithListResourcesHandler((_, _) => new ValueTask<ListResourcesResult>(new ListResourcesResult
            {
                Resources = [new Resource { Uri = "test://resource", Name = "resource" }],
            }))
            .WithReadResourceHandler((request, _) => new ValueTask<ReadResourceResult>(new ReadResourceResult
            {
                Contents = [new TextResourceContents { Uri = request.Params!.Uri!, Text = "hi" }],
                TimeToLive = TimeSpan.FromSeconds(30),
                CacheScope = CacheScope.Private,
            }));
    }

    [Fact]
    public async Task ListTools_PropagatesCachingHints_ToClient()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.ListToolsAsync(
            new ListToolsRequestParams(),
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMinutes(5), result.TimeToLive);
        Assert.Equal(CacheScope.Public, result.CacheScope);
    }

    [Fact]
    public async Task ReadResource_PropagatesCachingHints_ToClient()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.ReadResourceAsync(
            "test://resource",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(30), result.TimeToLive);
        Assert.Equal(CacheScope.Private, result.CacheScope);
    }

    [Fact]
    public async Task ListPrompts_WhenHandlerOmitsHints_ServerInjectsConservativeDefaults()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.ListPromptsAsync(
            new ListPromptsRequestParams(),
            TestContext.Current.CancellationToken);

        // SEP-2549: the handler left the hints unset, so the server fills in conservative defaults
        // (immediately stale, not shareable) rather than omitting the now-required fields.
        Assert.Equal(TimeSpan.Zero, result.TimeToLive);
        Assert.Equal(CacheScope.Private, result.CacheScope);
    }

    [Fact]
    public async Task ListResources_WhenHandlerOmitsHints_ServerInjectsConservativeDefaults()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.ListResourcesAsync(
            new ListResourcesRequestParams(),
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.Zero, result.TimeToLive);
        Assert.Equal(CacheScope.Private, result.CacheScope);
    }
}
