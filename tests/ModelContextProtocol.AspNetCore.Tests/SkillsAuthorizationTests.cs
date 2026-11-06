using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
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
        await using var client = await TestMcpClient.CreateAsync(
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
            async () => await client.GetSkillAsync(HiddenSkillUri, cancellationToken: TestContext.Current.CancellationToken));
        foreach (var uri in new[] { HiddenSkillUri, "skill://private/secret%2Dpayroll/SKILL.md", HiddenSkillUri + "#x" })
        {
            var error = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await client.ReadResourceAsync(uri, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(McpErrorCode.InvalidRequest, error.ErrorCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedPolicyGuardsCatalogAndResourcePaths(bool allowed)
    {
        await using var app = await StartServer(allowed);
        await using var transport = new HttpClientTransport(
            new() { Endpoint = new Uri("http://localhost:5000") }, HttpClient, LoggerFactory);
        await using var client = await TestMcpClient.CreateAsync(
            transport, cancellationToken: TestContext.Current.CancellationToken);

        var skills = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        if (allowed)
        {
            Assert.Equal(SkillUri, Assert.Single(skills).Uri);
            Assert.Equal(SkillUri, Assert.Single(resources).Uri);
            var skill = await client.GetSkillAsync(SkillUri, cancellationToken: TestContext.Current.CancellationToken);
            var content = await client.ReadSkillResourceAsync(skill, SkillUri, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Single(content.Contents);
        }
        else
        {
            Assert.Empty(skills);
            Assert.Empty(resources);
            var metadataError = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await client.GetSkillAsync(SkillUri, cancellationToken: TestContext.Current.CancellationToken));
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
        await using var client = await TestMcpClient.CreateAsync(
            transport, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken));
        var skill = await client.GetSkillAsync(SkillUri, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SkillUri, skill.Uri);
        Assert.Single((await client.ReadSkillResourceAsync(skill, SkillUri, cancellationToken: TestContext.Current.CancellationToken)).Contents);
    }

    [Fact]
    public async Task UnauthenticatedRoleClaimsDoNotAuthorizeSkillAccess()
    {
        await using var app = await StartServer(allowed: true, authenticated: false);
        await using var transport = new HttpClientTransport(
            new() { Endpoint = new Uri("http://localhost:5000") }, HttpClient, LoggerFactory);
        await using var client = await TestMcpClient.CreateAsync(
            transport, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(SkillUri, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false, "2026-07-28")]
    [InlineData(true, "2026-07-28")]
    [InlineData(false, "2025-11-25")]
    [InlineData(true, "2025-11-25")]
    public async Task ManualSkillsFiltersProtectRegisteredAndCustomEntries_AndFilesAreProtectedSeparately(bool allowed, string version)
    {
        var registered = CreateSkill("report");
        var application = CreateSkill("application");
        var policy = new ReadPolicy();
        var handlers = new AuthorizedHandlers([application.Skill], policy, pageSize: 1);
        Builder.Services.AddSingleton(policy);
        Builder.Services.AddMcpServer().WithHttpTransport(options => options.Stateless = true)
            .WithSkillPackages([registered]).WithSkills(options =>
            {
                options.ListSkillsHandler = handlers.ListAsync;
                options.GetSkillHandler = handlers.GetAsync;
                options.ListSkillsFilters.Add(next => async (context, cancellationToken) =>
                {
                    var result = await next(context, cancellationToken);
                    var requestPolicy = context.Services!.GetRequiredService<ReadPolicy>();
                    result.Skills = result.Skills.Where(skill => requestPolicy.CanRead(context.User, skill.Uri)).ToList();
                    result.TimeToLive = TimeSpan.Zero;
                    result.CacheScope = CacheScope.Private;
                    return result;
                });

                options.GetSkillFilters.Add(next => (context, cancellationToken) =>
                {
                    var uri = context.MatchedPrimitive is McpServerSkill matched ? matched.Id : context.Params.Uri;
                    if (!context.Services!.GetRequiredService<ReadPolicy>().CanRead(context.User, uri))
                    {
                        throw new McpProtocolException("Access forbidden.", McpErrorCode.InvalidParams);
                    }

                    return next(context, cancellationToken);
                });
            })
            .WithResources(application.Resources)
            .WithRequestFilters(filters =>
            {
                filters.AddListResourcesFilter(next => async (context, cancellationToken) =>
                {
                    var result = await next(context, cancellationToken);
                    result.Resources = result.Resources.Where(resource => policy.CanRead(context.User, resource.Uri)).ToList();
                    return result;
                });
                filters.AddReadResourceFilter(next => (context, cancellationToken) =>
                {
                    if (!policy.CanRead(context.User, context.MatchedPrimitive?.Id))
                    {
                        throw new McpProtocolException("Access forbidden.", McpErrorCode.InvalidRequest);
                    }

                    return next(context, cancellationToken);
                });
            });
        await using var app = Builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, allowed ? "reader" : "viewer")], "Test"));
            await next(context);
        });

        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var transport = new HttpClientTransport(
            new() { Endpoint = new Uri("http://localhost:5000/") }, HttpClient, LoggerFactory);
        var clientOptions = TestMcpClient.CreateOptions();
        clientOptions.ProtocolVersion = version;
        await using var client = await TestMcpClient.CreateAsync(
            transport, clientOptions, LoggerFactory, TestContext.Current.CancellationToken);

        var page = await client.ListSkillsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken);
        Assert.Equal(allowed ? [application.Skill.Id, registered.Skill.Id] : [], page.Skills.Select(skill => skill.Uri));
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(allowed ? [application.Skill.Id, registered.Skill.Id] : [],
            resources.Select(resource => resource.Uri).OrderBy(uri => uri, StringComparer.Ordinal));
        Assert.Null(page.NextCursor);
        if (version == McpProtocolVersions.July2026ProtocolVersion)
        {
            Assert.Equal(TimeSpan.Zero, page.TimeToLive);
            Assert.Equal(CacheScope.Private, page.CacheScope);
        }

        foreach (var uri in new[] { "SKILL://PRIVATE/report/SKILL.md", application.Skill.Id })
        {
            if (allowed)
            {
                var skill = await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken);
                Assert.Single((await client.ReadSkillResourceAsync(
                    skill, skill.Uri, cancellationToken: TestContext.Current.CancellationToken)).Contents);
            }
            else
            {
                var error = await Assert.ThrowsAsync<McpProtocolException>(
                    async () => await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken));
                Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
            }
        }

        foreach (var uri in new[]
        {
            registered.Skill.Id,
            "SKILL://PRIVATE/report/SKILL.md",
            "skill://private/%72eport/SKILL.md",
            registered.Skill.Id + "#x",
            application.Skill.Id,
        })
        {
            if (allowed)
            {
                Assert.Single((await client.ReadResourceAsync(uri, cancellationToken: TestContext.Current.CancellationToken)).Contents);
            }
            else
            {
                var error = await Assert.ThrowsAsync<McpProtocolException>(
                    async () => await client.ReadResourceAsync(uri, cancellationToken: TestContext.Current.CancellationToken));
                Assert.Equal(McpErrorCode.InvalidRequest, error.ErrorCode);
            }
        }
    }

    private async Task<WebApplication> StartServer(
        bool allowed, bool hideResourceListing = false, bool authenticated = true, bool paginated = false)
    {
        var packages = paginated
            ? new[] { CreateSkill("secret-payroll"), CreateSkill("zebra"), CreateSkill("zulu") }
            : new[] { CreateSkill("report") };
        var policy = new ReadPolicy();
        var handlers = new AuthorizedHandlers(packages.Select(package => package.Skill).ToArray(), policy, pageSize: 1);
        Builder.Services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithSkills(options =>
            {
                options.ListSkillsHandler = handlers.ListAsync;
                options.GetSkillHandler = handlers.GetAsync;
            })
            .WithResources(packages.SelectMany(package => package.Resources))
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
                    if (!policy.CanRead(context.User, context.MatchedPrimitive?.Id))
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

    private McpServerSkillPackage CreateSkill(string name) => McpServerSkillPackage.Create(
        [McpServerSkillFile.FromText("SKILL.md", $"---\nname: {name}\ndescription: d\n---\n")], new McpServerSkillPackageCreateOptions { Uri = $"skill://private/{name}/SKILL.md" });

    private sealed class ReadPolicy
    {
        public bool CanRead(ClaimsPrincipal? user, string? id) =>
            id?.StartsWith("skill://private/", StringComparison.Ordinal) == true &&
            id != HiddenSkillUri &&
            (user?.Identity?.IsAuthenticated).GetValueOrDefault() &&
            (user?.IsInRole("reader")).GetValueOrDefault();
    }

    private sealed class AuthorizedHandlers(IReadOnlyList<McpServerSkill> skills, ReadPolicy policy, int pageSize)
    {
        public ValueTask<ListSkillsResult> ListAsync(
            RequestContext<ListSkillsRequestParams> context, CancellationToken cancellationToken)
        {
            string? after = context.Params.Cursor is { } cursor ? Encoding.UTF8.GetString(Convert.FromBase64String(cursor)) : null;
            var visible = skills.Where(skill => policy.CanRead(context.User, skill.Id))
                .OrderBy(skill => skill.Id, StringComparer.Ordinal)
                .Where(skill => after is null || string.CompareOrdinal(skill.Id, after) > 0).ToArray();
            var page = visible.Take(pageSize).Select(skill => skill.ProtocolSkill!).ToList();
            return new(new ListSkillsResult
            {
                Skills = page,
                NextCursor = visible.Length > pageSize ?
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(page[page.Count - 1].Uri)) : null,
            });
        }

        public ValueTask<GetSkillResult> GetAsync(
            RequestContext<GetSkillRequestParams> context, CancellationToken cancellationToken) =>
            new(new GetSkillResult
            {
                Skill = skills.SingleOrDefault(skill => skill.Id == context.Params.Uri) is { } skill && policy.CanRead(context.User, skill.Id) ?
                    skill.ProtocolSkill! : throw new McpProtocolException("No skill is served.", McpErrorCode.InvalidParams),
            });
    }
}
