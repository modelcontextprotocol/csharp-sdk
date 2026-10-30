using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using SkillsServer;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Samples;

public class DatabaseStyleSkillsHandlersTests : ClientServerTestBase
{
    private ClaimsPrincipal _caller = CreateCaller("acme");
    private readonly ExampleSkillStore _store = new(
    [
        ExampleSkillRow.Create("acme", "aa-denied", requiredRole: "administrator"),
        ExampleSkillRow.Create("other", "ab-other"),
        ExampleSkillRow.Create("acme", "alpha"),
        ExampleSkillRow.Create("acme", "beta"),
        ExampleSkillRow.Create("acme", "hidden", listed: false),
    ]);

    public DatabaseStyleSkillsHandlersTests(ITestOutputHelper output) : base(output, startServer: false)
    {
        ServiceCollection.AddSingleton(_store);
        McpServerBuilder.WithRepositoryHandlers()
            .WithMessageFilters(filters => filters.AddIncomingFilter(next => (context, cancellationToken) =>
            {
                context.User = _caller;
                return next(context, cancellationToken);
            }));
        StartServer();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TenantAndPermissionFilteringHappensBeforePaging_AndCursorsDoNotNameDeniedRows(bool resources)
    {
        await using var client = await CreateMcpClientForServer();
        var seen = new List<string>();
        string? cursor = null;
        int pages = 0;
        do
        {
            string[] uris;
            string? nextCursor;
            if (resources)
            {
                var result = await client.ListResourcesAsync(new ListResourcesRequestParams { Cursor = cursor }, TestContext.Current.CancellationToken);
                uris = result.Resources.Select(resource => resource.Uri).ToArray();
                nextCursor = result.NextCursor;
            }
            else
            {
                var result = await client.ListSkillsAsync(new ListSkillsRequestParams { Cursor = cursor }, TestContext.Current.CancellationToken);
                uris = result.Skills.Select(skill => skill.Uri).ToArray();
                nextCursor = result.NextCursor;
            }
            seen.Add(Assert.Single(uris));
            if (nextCursor is not null)
            {
                Assert.Equal(uris[0], Encoding.UTF8.GetString(Convert.FromBase64String(nextCursor)));
            }
            cursor = nextCursor;
            Assert.True(++pages <= 2);
        }
        while (cursor is not null);
        Assert.Equal(2, pages);
        Assert.Equal(["skill://store/acme/alpha/SKILL.md", "skill://store/acme/beta/SKILL.md"], seen);
    }

    [Fact]
    public async Task PermittedUnlistedSkillAndFileAreDirectlyRetrievable()
    {
        const string Uri = "skill://store/acme/hidden/SKILL.md";
        await using var client = await CreateMcpClientForServer();
        Assert.DoesNotContain(await client.ListSkillsAsync(TestContext.Current.CancellationToken), entry => entry.Uri == Uri);
        Assert.DoesNotContain(await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken), entry => entry.Uri == Uri);

        var entry = await client.GetSkillAsync(Uri, TestContext.Current.CancellationToken);
        var read = await client.ReadSkillResourceAsync(entry, Uri, TestContext.Current.CancellationToken);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents));
        Assert.Equal(_store.Rows.Single(row => row.Skill.Id == Uri).Markdown, text.Text);
    }

    [Theory]
    [InlineData("skill://store/acme/aa-denied/SKILL.md")]
    [InlineData("skill://store/other/ab-other/SKILL.md")]
    public async Task DirectMetadataAndFileAccessEnforceTheSameTenantAndPermissionChecks(string uri)
    {
        await using var client = await CreateMcpClientForServer();

        var metadataError = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(uri, TestContext.Current.CancellationToken));
        var fileError = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.ReadResourceAsync(uri, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InvalidParams, metadataError.ErrorCode);
        Assert.Equal(McpErrorCode.InvalidParams, fileError.ErrorCode);
    }

    [Fact]
    public async Task CallerChangesAreReflectedByBothHandlersWithoutChangingRegistrations()
    {
        await using var client = await CreateMcpClientForServer();
        Assert.Equal(2, (await client.ListSkillsAsync(TestContext.Current.CancellationToken)).Count);
        _caller = CreateCaller("other");

        var skill = Assert.Single(await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        var resource = Assert.Single(await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("skill://store/other/ab-other/SKILL.md", skill.Uri);
        Assert.Equal(skill.Uri, resource.Uri);
        Assert.Single((await client.ReadSkillResourceAsync(skill, skill.Uri, TestContext.Current.CancellationToken)).Contents);
    }

    [Theory]
    [InlineData("skills/list", false)]
    [InlineData("skills/list", true)]
    [InlineData("skills/get", false)]
    [InlineData("skills/get", true)]
    [InlineData("resources/list", false)]
    [InlineData("resources/list", true)]
    [InlineData("resources/read", false)]
    [InlineData("resources/read", true)]
    public async Task EveryOperationRequiresAnAuthenticatedTenant(string method, bool missingTenant)
    {
        _caller = missingTenant ?
            new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "reader")], "Example")) :
            new(new ClaimsIdentity([new Claim("tenant", "acme"), new Claim(ClaimTypes.Role, "reader")]));
        await using var client = await CreateMcpClientForServer();

        var error = await Assert.ThrowsAsync<McpProtocolException>(() => client.SendRequestAsync(new JsonRpcRequest
        {
            Method = method,
            Params = new JsonObject { ["uri"] = "skill://store/acme/hidden/SKILL.md" },
        }, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InvalidRequest, error.ErrorCode);
    }

    [Theory]
    [InlineData("skills/list")]
    [InlineData("skills/get")]
    [InlineData("resources/list")]
    [InlineData("resources/read")]
    public async Task TypedCallbacksUseTheSuppliedRequestScopeAndPropagateCancellation(string method)
    {
        var skills = ServiceProvider.GetRequiredService<IOptions<McpSkillsOptions>>().Value;
        var resources = Server.ServerOptions.Handlers;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var scoped = scope.ServiceProvider.GetRequiredService<ExampleSkillRepository>();
        Assert.Same(scoped, scope.ServiceProvider.GetRequiredService<ExampleSkillRepository>());
        await using var otherScope = ServiceProvider.CreateAsyncScope();
        Assert.NotSame(scoped, otherScope.ServiceProvider.GetRequiredService<ExampleSkillRepository>());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        const string Uri = "skill://store/acme/hidden/SKILL.md";
        var request = new JsonRpcRequest { Method = method };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            switch (method)
            {
                case "skills/list":
                    await skills.ListSkillsHandler!(new(Server, request, new()) { Services = scope.ServiceProvider, User = _caller }, cancellation.Token);
                    break;
                case "skills/get":
                    await skills.GetSkillHandler!(new(Server, request, new() { Uri = Uri }) { Services = scope.ServiceProvider, User = _caller }, cancellation.Token);
                    break;
                case "resources/list":
                    await resources.ListResourcesHandler!(new(Server, request, new()) { Services = scope.ServiceProvider, User = _caller }, cancellation.Token);
                    break;
                case "resources/read":
                    await resources.ReadResourceHandler!(new(Server, request, new() { Uri = Uri }) { Services = scope.ServiceProvider, User = _caller }, cancellation.Token);
                    break;
            }
        });
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Theory]
    [InlineData("skills/list")]
    [InlineData("resources/list")]
    public async Task RepositoryCursorErrorsAreReportedAsInvalidParams(string method)
    {
        await using var client = await CreateMcpClientForServer();
        var error = await Assert.ThrowsAsync<McpProtocolException>(() => client.SendRequestAsync(new JsonRpcRequest
        {
            Method = method,
            Params = new JsonObject { ["cursor"] = "not-base64!" },
        }, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
    }

    private static ClaimsPrincipal CreateCaller(string tenant) =>
        new(new ClaimsIdentity([new Claim("tenant", tenant), new Claim(ClaimTypes.Role, "reader")], "Example"));
}
