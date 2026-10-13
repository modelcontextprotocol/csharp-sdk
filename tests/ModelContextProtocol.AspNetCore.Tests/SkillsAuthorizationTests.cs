using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Security.Claims;
using System.Text;

namespace ModelContextProtocol.AspNetCore.Tests;

public class SkillsAuthorizationTests(ITestOutputHelper output) : KestrelInMemoryTest(output)
{
    private const string SkillUri = "skill://private/report/SKILL.md";
    private const string HiddenSkillUri = "skill://private/secret-payroll/SKILL.md";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PaginationDoesNotDiscloseDeniedEntriesThroughCursors(bool allowed)
    {
        await using var app = await StartServer(allowed, paginated: true);
        await using var transport = new HttpClientTransport(
            new() { Endpoint = new Uri("http://localhost:5000") }, HttpClient, LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport, cancellationToken: TestContext.Current.CancellationToken);
        var seen = new List<string>();
        string? cursor = null;
        int pages = 0;
        do
        {
            var page = await client.ListSkillsAsync(
                new ListSkillsRequestParams { Cursor = cursor }, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(page.Skills, skill => skill.Uri == HiddenSkillUri);
            seen.AddRange(page.Skills.Select(skill => skill.Uri));
            cursor = page.NextCursor;
            if (cursor is not null)
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                Assert.DoesNotContain("secret-payroll", decoded);
                Assert.Equal(Assert.Single(page.Skills).Uri, decoded);
            }
            Assert.True(++pages <= 3, "Authorized pagination did not terminate.");
        }
        while (cursor is not null);

        Assert.Equal(allowed ? 2 : 1, pages);
        Assert.Equal(allowed ? new[] { "skill://private/zebra/SKILL.md", "skill://private/zulu/SKILL.md" } : [], seen);
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(seen, resources.Select(resource => resource.Uri).OrderBy(uri => uri, StringComparer.Ordinal));
        await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(HiddenSkillUri, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.ReadResourceAsync(HiddenSkillUri, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedPolicyGuardsCatalogAndResourcePaths(bool allowed)
    {
        await using var app = await StartServer(allowed);
        await using var transport = new HttpClientTransport(
            new() { Endpoint = new Uri("http://localhost:5000") }, HttpClient, LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport, cancellationToken: TestContext.Current.CancellationToken);

        var skills = await client.ListSkillsAsync(TestContext.Current.CancellationToken);
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        if (allowed)
        {
            Assert.Equal(SkillUri, Assert.Single(skills).Uri);
            Assert.Equal(SkillUri, Assert.Single(resources).Uri);
            var skill = await client.GetSkillAsync(SkillUri, TestContext.Current.CancellationToken);
            var content = await client.ReadSkillResourceAsync(skill, SkillUri, TestContext.Current.CancellationToken);
            Assert.Single(content.Contents);
        }
        else
        {
            Assert.Empty(skills);
            Assert.Empty(resources);
            var metadataError = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await client.GetSkillAsync(SkillUri, TestContext.Current.CancellationToken));
            Assert.Equal(McpErrorCode.InvalidParams, metadataError.ErrorCode);
            var resourceError = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await client.ReadResourceAsync(SkillUri, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(McpErrorCode.InvalidRequest, resourceError.ErrorCode);
        }
    }

    [Fact]
    public async Task ResourceListingOmissionDoesNotOverrideAnAuthorizedDirectRead()
    {
        await using var app = await StartServer(allowed: true, hideResourceListing: true);
        await using var transport = new HttpClientTransport(
            new() { Endpoint = new Uri("http://localhost:5000") }, HttpClient, LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken));
        var skill = await client.GetSkillAsync(SkillUri, TestContext.Current.CancellationToken);
        Assert.Equal(SkillUri, skill.Uri);
        Assert.Single((await client.ReadSkillResourceAsync(skill, SkillUri, TestContext.Current.CancellationToken)).Contents);
    }

    [Fact]
    public async Task UnauthenticatedRoleClaimsDoNotAuthorizeSkillAccess()
    {
        await using var app = await StartServer(allowed: true, authenticated: false);
        await using var transport = new HttpClientTransport(
            new() { Endpoint = new Uri("http://localhost:5000") }, HttpClient, LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(SkillUri, TestContext.Current.CancellationToken));
    }

    private async Task<WebApplication> StartServer(
        bool allowed, bool hideResourceListing = false, bool authenticated = true, bool paginated = false)
    {
        var skills = paginated
            ? new[] { CreateSkill("secret-payroll"), CreateSkill("zebra"), CreateSkill("zulu") }
            : new[] { CreateSkill("report") };
        var policy = new ReadPolicy();
        var catalog = new AuthorizedCatalog(skills.Select(skill => skill.ProtocolSkill).ToArray(), policy, pageSize: 1);
        Builder.Services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithSkills(catalog)
            .WithResources(skills.SelectMany(skill => skill.Resources))
            .WithRequestFilters(filters =>
            {
                filters.AddListResourcesFilter(next => async (context, cancellationToken) =>
                {
                    var result = await next(context, cancellationToken);
                    result.Resources = result.Resources
                        .Where(resource => !hideResourceListing && policy.CanRead(context.User, resource.Uri))
                        .ToList();
                    return result;
                });
                filters.AddReadResourceFilter(next => (context, cancellationToken) =>
                {
                    if (!policy.CanRead(context.User, context.Params.Uri))
                    {
                        throw new McpProtocolException("Access forbidden.", McpErrorCode.InvalidRequest);
                    }
                    return next(context, cancellationToken);
                });
            });

        var app = Builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, allowed ? "reader" : "viewer")], authenticated ? "Test" : null));
            await next(context);
        });
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private McpServerSkill CreateSkill(string name) => McpServerSkill.Create(
        $"skill://private/{name}/SKILL.md",
        [McpServerSkillFile.FromText("SKILL.md", $"---\nname: {name}\ndescription: d\n---\n")]);

    private sealed class ReadPolicy
    {
        public bool CanRead(ClaimsPrincipal? user, string uri) =>
            uri.StartsWith("skill://private/", StringComparison.Ordinal) &&
            uri != HiddenSkillUri &&
            (user?.Identity?.IsAuthenticated).GetValueOrDefault() &&
            (user?.IsInRole("reader")).GetValueOrDefault();
    }

    private sealed class AuthorizedCatalog(IReadOnlyList<Skill> skills, ReadPolicy policy, int pageSize) : IMcpSkillCatalog
    {
        public ValueTask<McpSkillPage> ListAsync(
            string? cursor, McpSkillRequestContext context, CancellationToken cancellationToken) =>
            ForUser(context.User).ListAsync(cursor, context, cancellationToken);

        public ValueTask<Skill?> GetAsync(
            string uri, McpSkillRequestContext context, CancellationToken cancellationToken) =>
            ForUser(context.User).GetAsync(uri, context, cancellationToken);

        private InMemoryMcpSkillCatalog ForUser(ClaimsPrincipal? user) =>
            new(skills.Where(skill => policy.CanRead(user, skill.Uri)), pageSize);
    }
}
