using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpServerSkillsProviderTests(ITestOutputHelper output) : LoggedTest(output)
{
    private const string SkillUri = "skill://alpha/SKILL.md";
    private const string Markdown = "---\nname: alpha\ndescription: Alpha\n---\n# Instructions\n";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-skills-provider-" + Guid.NewGuid().ToString("N"));

    public override void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
        base.Dispose();
    }

    [Theory]
    [InlineData("catalog", true)]
    [InlineData("catalog", false)]
    [InlineData("skills", true)]
    [InlineData("skills", false)]
    [InlineData("directory", true)]
    [InlineData("directory", false)]
    public async Task SuppliedAndStaticCatalogs_WorkWithoutProvider(string registration, bool scopeRequests)
    {
        var skill = McpServerSkill.Create(SkillUri, [McpServerSkillFile.FromText("SKILL.md", Markdown)]);
        await using var catalog = new ProviderFreeCatalog(new InMemoryMcpSkillCatalog([skill.ProtocolSkill]));
        var services = new ServiceCollection();
        var builder = services.AddMcpServer(options => options.ScopeRequests = scopeRequests);
        switch (registration)
        {
            case "catalog":
                builder.WithSkills(catalog).WithResources(skill.Resources);
                break;
            case "skills":
                builder.WithSkills([skill]);
                break;
            case "directory":
                var directory = Path.Combine(_root, "alpha");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "SKILL.md"), Markdown);
                builder.WithSkillsFromDirectory(_root);
                break;
        }
        var options = await CreateOptionsAsync(services);
        Assert.Equal(0, catalog.DisposeCount);

        await RunServerAsync(options, async transport =>
        {
            var list = Assert.IsType<JsonRpcResponse>(await SendAsync(transport, SkillsProtocol.MethodSkillsList));
            Assert.Equal(SkillUri, Assert.Single(list.Result!["skills"]!.AsArray())!["uri"]!.GetValue<string>());

            var get = Assert.IsType<JsonRpcResponse>(await SendAsync(
                transport, SkillsProtocol.MethodSkillsGet, new JsonObject { ["uri"] = SkillUri }));
            Assert.Equal(SkillUri, get.Result!["skill"]!["uri"]!.GetValue<string>());

            var read = Assert.IsType<JsonRpcResponse>(await SendAsync(
                transport, RequestMethods.ResourcesRead, new JsonObject { ["uri"] = SkillUri }));
            var contents = Assert.Single(read.Result!["contents"]!.AsArray());
            Assert.Equal(Markdown, contents!["text"]!.GetValue<string>());
        });

        Assert.Equal(0, catalog.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeAndFactoryCatalogs_StillRequireProvider(bool factory)
    {
        var skill = McpServerSkill.Create(SkillUri, [McpServerSkillFile.FromText("SKILL.md", Markdown)]);
        var services = new ServiceCollection();
        services.AddSingleton(new InMemoryMcpSkillCatalog([skill.ProtocolSkill]));
        var builder = services.AddMcpServer();
        int factoryCalls = 0;
        if (factory)
        {
            builder.WithSkills(provider =>
            {
                factoryCalls++;
                return new ProviderFreeCatalog(provider.GetRequiredService<InMemoryMcpSkillCatalog>());
            });
        }
        else
        {
            builder.WithSkills<ProviderFreeCatalog>();
        }
        var options = await CreateOptionsAsync(services);

        await RunServerAsync(options, async transport =>
        {
            foreach (var method in new[] { SkillsProtocol.MethodSkillsList, SkillsProtocol.MethodSkillsGet })
            {
                var error = Assert.IsType<JsonRpcError>(await SendAsync(
                    transport, method, new JsonObject { ["uri"] = SkillUri }));
                Assert.Equal((int)McpErrorCode.InternalError, error.Error.Code);
            }
        });

        Assert.Equal(0, factoryCalls);
    }

    private static async Task<McpServerOptions> CreateOptionsAsync(ServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
    }

    private async Task RunServerAsync(McpServerOptions options, Func<TestServerTransport, Task> action)
    {
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        Assert.Null(server.Services);
        var running = server.RunAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.IsType<JsonRpcResponse>(await SendAsync(transport, RequestMethods.Initialize,
                JsonSerializer.SerializeToNode(new InitializeRequestParams
                {
                    ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
                    Capabilities = new(),
                    ClientInfo = new() { Name = "provider-free-client", Version = "1" },
                }, McpJsonUtilities.DefaultOptions)));
            await action(transport);
        }
        finally
        {
            await transport.DisposeAsync();
            await running.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<JsonRpcMessage> SendAsync(TestServerTransport transport, string method, JsonNode? parameters = null)
    {
        var id = new RequestId(method);
        var response = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnMessageSent = message =>
        {
            if (message is JsonRpcResponse result && result.Id == id ||
                message is JsonRpcError error && error.Id == id)
            {
                response.TrySetResult(message);
            }
        };
        await transport.SendClientMessageAsync(
            new JsonRpcRequest { Id = id, Method = method, Params = parameters }, TestContext.Current.CancellationToken);
        return await response.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    private sealed class ProviderFreeCatalog(InMemoryMcpSkillCatalog catalog) : IMcpSkillCatalog, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask<McpSkillPage> ListAsync(RequestContext<ListSkillsRequestParams> context, CancellationToken cancellationToken)
        {
            Assert.Null(context.Services);
            Assert.Null(context.Server.Services);
            return catalog.ListAsync(context, cancellationToken);
        }

        public ValueTask<Skill?> GetAsync(RequestContext<GetSkillRequestParams> context, CancellationToken cancellationToken)
        {
            Assert.Null(context.Services);
            Assert.Null(context.Server.Services);
            return catalog.GetAsync(context, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return default;
        }
    }
}
