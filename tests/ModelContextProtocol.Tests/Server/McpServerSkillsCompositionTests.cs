using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpServerSkillsCompositionTests(ITestOutputHelper output) : ClientServerTestBase(output, startServer: false)
{
    private readonly McpServerSkill _registered = CreateSkill("alpha");

    private static McpServerSkill CreateSkill(string name) =>
        McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", $"---\nname: {name}\ndescription: {name}\n---\n")]).Skill;

    [Fact]
    public async Task RegisteredEntriesAreListedInInsertionOrder()
    {
        var skills = new[] { CreateSkill("gamma"), CreateSkill("alpha"), CreateSkill("beta") };
        McpServerBuilder.WithSkills(skills);
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var page = await client.ListSkillsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken);

        Assert.Equal(skills.Select(skill => skill.Id), page.Skills.Select(skill => skill.Uri));
        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    public async Task InitialCustomPageIsAugmented_AndContinuationPagesDoNotRepeatRegisteredEntries(string cursor)
    {
        var application = CreateSkill("application").ProtocolSkill!;
        var page = new ListSkillsResult { Skills = [application], NextCursor = cursor };
        McpServerBuilder.WithSkills([_registered]).WithSkills(options =>
            options.ListSkillsHandler = (context, _) => new(context.Params.Cursor is null ? page : new ListSkillsResult()));
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var first = await client.ListSkillsAsync(new ListSkillsRequestParams(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal([application.Uri, _registered.Id], first.Skills.Select(skill => skill.Uri));
        Assert.Equal(cursor, first.NextCursor);
        var next = await client.ListSkillsAsync(new ListSkillsRequestParams { Cursor = cursor }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(next.Skills);
        Assert.Null(next.NextCursor);
        Assert.Equal(2, page.Skills.Count);
    }

    [Fact]
    public async Task ComposedPageCanDescribeDifferentSnapshotsOfSharedFiles()
    {
        var inner = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: inner\ndescription: Inner\n---\n")],
            new McpServerSkillPackageCreateOptions
            {
                Uri = "skill://catalog/inner/SKILL.md",
            }).Skill;
        var outer = McpServerSkillPackage.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", "---\nname: catalog\ndescription: Catalog\n---\n"),
            McpServerSkillFile.FromText("inner/SKILL.md", "---\nname: inner\ndescription: Changed\n---\n"),
        ], new McpServerSkillPackageCreateOptions
        {
            Uri = "skill://catalog/SKILL.md",
        }).Skill;
        McpServerBuilder.WithSkills([inner]).WithSkills(options =>
        {
            options.ListSkillsFilters.Add(next => async (context, cancellationToken) =>
            {
                var page = await next(context, cancellationToken);
                page.Skills.Add(outer.ProtocolSkill!);
                return page;
            });
        });

        StartServer();
        await using var client = await CreateMcpClientForServer();
        var page = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var returnedOuter = Assert.Single(page, entry => entry.Uri == outer.Id);
        var returnedInner = Assert.Single(page, entry => entry.Uri == inner.Id);
        Assert.Equal("Catalog", returnedOuter.Description);
        Assert.Equal("Inner", returnedInner.Description);
        Assert.Equal(2, page.Count);
        Assert.NotEqual(Assert.Single(returnedInner.Resources.Entries!).Digest,
            Assert.Single(returnedOuter.Resources.Entries!, file => file.Uri == inner.Id).Digest);
    }

    [Fact]
    public async Task RegisteredGetWins_AndCustomFallbackServesUnlistedEntries()
    {
        var hidden = CreateSkill("hidden").ProtocolSkill!;
        int calls = 0;
        McpServerBuilder.WithSkills([_registered]).WithSkills(options =>
        {
            options.GetSkillHandler = (context, _) =>
            {
                calls++;
                Assert.Null(context.MatchedPrimitive);
                if (!new Uri(context.Params.Uri).Equals(new Uri(hidden.Uri)))
                {
                    throw new McpProtocolException("Denied.", McpErrorCode.InvalidParams);
                }

                return new(new GetSkillResult { Skill = hidden });
            };
        });

        StartServer();
        await using var client = await CreateMcpClientForServer();
        Assert.Equal(_registered.Id, (await client.GetSkillAsync(_registered.Id, cancellationToken: TestContext.Current.CancellationToken)).Uri);
        Assert.Equal(0, calls);
        Assert.Single(await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(hidden.Uri, (await client.GetSkillAsync("SKILL://HIDDEN/SKILL.md", cancellationToken: TestContext.Current.CancellationToken)).Uri);
        var error = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync("skill://denied/SKILL.md", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongFallbackUriIsRejectedEvenWhenHandlerMutatesParameters(bool mutate)
    {
        var other = CreateSkill("other");
        McpServerBuilder.WithSkills(options => options.GetSkillHandler = (context, _) =>
        {
            if (mutate)
            {
                context.Params.Uri = other.Id;
            }

            return new(new GetSkillResult { Skill = other.ProtocolSkill! });
        });

        StartServer();
        await using var client = await CreateMcpClientForServer();
        var error = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(_registered.Id, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InternalError, error.ErrorCode);
        Assert.Equal("Request failed (remote): An error occurred.", error.Message);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.Exception is InvalidOperationException local && local.Message.Contains(_registered.Id));
    }

    [Theory]
    [InlineData("skills/list", "null-result")]
    [InlineData("skills/list", "null-entries")]
    [InlineData("skills/list", "frontmatter")]
    [InlineData("skills/list", "manifest")]
    [InlineData("skills/get", "null-result")]
    [InlineData("skills/get", "null-entries")]
    [InlineData("skills/get", "frontmatter")]
    [InlineData("skills/get", "manifest")]
    public async Task InvalidCustomResultsAreInternalErrors(string method, string failure)
    {
        var entry = _registered.ProtocolSkill!;
        if (failure == "frontmatter") entry.Frontmatter["description"] = "";
        if (failure == "manifest") entry.Resources.Entries![0].Digest = "sha256:invalid";
        McpServerBuilder.WithSkills(options =>
        {
            options.ListSkillsHandler = (_, _) => new(failure == "null-result" ? null! : new ListSkillsResult
            {
                Skills = failure == "null-entries" ? null! : [entry],
            });

            options.GetSkillHandler = (_, _) => new(failure == "null-result" ? null! : new GetSkillResult
            {
                Skill = failure == "null-entries" ? null! : entry,
            });
        });

        StartServer();
        await using var client = await CreateMcpClientForServer();
        var error = await Assert.ThrowsAsync<McpProtocolException>(() => client.SendRequestAsync(new JsonRpcRequest
        {
            Method = method, Params = new JsonObject { ["uri"] = _registered.Id },
        }, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InternalError, error.ErrorCode);
        Assert.Equal("Request failed (remote): An error occurred.", error.Message);
        if (failure is "frontmatter" or "manifest")
        {
            Assert.Contains(MockLoggerProvider.LogMessages, log =>
                log.Exception is ArgumentException local && local.Message.Contains(entry.Uri));
        }
    }

    [Fact]
    public async Task RegisteredGetUsesTheActualContext_WithoutInvokingGetForListing()
    {
        var entry = _registered.ProtocolSkill!;
        entry.Frontmatter["description"] = "get";
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "reader")], "test"));
        var item = new object();
        RequestContext<GetSkillRequestParams>? filtered = null;
        CancellationToken filteredToken = default;
        RequestService? dependency = null;
        ServiceCollection.AddScoped<RequestService>();
        var primitive = new RequestSkill(entry.Uri, null, async (context, cancellationToken) =>
        {
            Assert.Same(filtered, context);
            Assert.Equal(filteredToken, cancellationToken);
            Assert.True(cancellationToken.CanBeCanceled);
            Assert.Same(user, context.User);
            Assert.Same(item, context.Items["request-item"]);
            Assert.Same(dependency, context.Services!.GetRequiredService<RequestService>());
            Assert.NotSame(Server.Services, context.Services);
            Assert.Equal(SkillsProtocol.MethodSkillsGet, context.JsonRpcRequest.Method);
            Assert.Equal("SKILL://ALPHA/SKILL.md", context.Params.Uri);
            Assert.Equal("get", context.Params.Meta!["example.com/request"]!.GetValue<string>());
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return new GetSkillResult { Skill = entry };
        });

        McpServerBuilder.WithMessageFilters(filters => filters.AddIncomingFilter(next => (context, cancellationToken) =>
        {
            context.User = user;
            context.Items["request-item"] = item;
            return next(context, cancellationToken);
        })).WithSkills([primitive]).WithSkills(options =>
        {
            options.GetSkillHandler = (_, _) => throw new InvalidOperationException("The fallback must not run.");
            options.GetSkillFilters.Add(next => (context, cancellationToken) =>
            {
                Assert.Same(primitive, context.MatchedPrimitive);
                Assert.Equal("request-skill", Assert.Single(primitive.Metadata));
                filtered = context;
                filteredToken = cancellationToken;
                dependency = context.Services!.GetRequiredService<RequestService>();
                return next(context, cancellationToken);
            });
        });

        StartServer();
        Assert.Equal(0, primitive.GetCalls);
        await using var client = await CreateMcpClientForServer();
        Assert.Empty(await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, primitive.GetCalls);
        var result = await client.GetSkillAsync(new GetSkillRequestParams
        {
            Uri = "SKILL://ALPHA/SKILL.md", Meta = new JsonObject { ["example.com/request"] = "get" },
        }, TestContext.Current.CancellationToken);
        Assert.Equal("get", result.Skill.Description);
        Assert.Equal(1, primitive.GetCalls);
    }

    [Fact]
    public async Task RegisteredListingAndGetCanHaveDifferentEntries()
    {
        var listing = _registered.ProtocolSkill!;
        var fetched = CreateSkill("alpha").ProtocolSkill!;
        fetched.Frontmatter["description"] = "request-time entry";
        var primitive = new RequestSkill(listing.Uri, listing, (_, _) => new(new GetSkillResult { Skill = fetched }));
        McpServerBuilder.WithSkills([primitive]);
        StartServer();
        await using var client = await CreateMcpClientForServer();
        Assert.Equal("alpha", Assert.Single(await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken)).Description);
        Assert.Equal(0, primitive.GetCalls);
        Assert.Equal("request-time entry", (await client.GetSkillAsync(
            primitive.Id, cancellationToken: TestContext.Current.CancellationToken)).Description);
        Assert.Equal(1, primitive.GetCalls);
    }

    [Fact]
    public async Task RegisteredGetCanSupplyEnvelopeHints()
    {
        var source = new GetSkillResult
        {
            Skill = _registered.ProtocolSkill!,
            ResultType = "input",
            Meta = new JsonObject { ["example.com/result"] = "get" },
            TimeToLive = TimeSpan.FromMinutes(2),
            CacheScope = CacheScope.Public,
        };

        var primitive = new RequestSkill(_registered.Id, null, (_, _) => new(source));
        McpServerBuilder.WithSkills([primitive]);

        StartServer();
        var options = TestMcpClient.CreateOptions();
        options.ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion;
        await using var client = await CreateMcpClientForServer(options);
        var result = (await client.SendRequestAsync(new JsonRpcRequest
        {
            Method = SkillsProtocol.MethodSkillsGet, Params = new JsonObject { ["uri"] = primitive.Id },
        }, TestContext.Current.CancellationToken)).Result!.AsObject();
        Assert.Equal("get", result["_meta"]!["example.com/result"]!.GetValue<string>());
        Assert.Equal("complete", result["resultType"]!.GetValue<string>());
        Assert.Equal(120_000, result["ttlMs"]!.GetValue<long>());
        Assert.Equal("public", result["cacheScope"]!.GetValue<string>());
        Assert.Equal("input", source.ResultType);
        Assert.Equal(TimeSpan.FromMinutes(2), source.TimeToLive);
        Assert.Equal(CacheScope.Public, source.CacheScope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupRejectsInvalidOrDifferentlyIdentifiedListingEntries_WithoutInvokingGet(bool differentUri)
    {
        var entry = differentUri ? CreateSkill("other").ProtocolSkill! : _registered.ProtocolSkill!;
        if (!differentUri) entry.Frontmatter["description"] = "";
        var primitive = new RequestSkill(_registered.Id, entry, (_, _) => throw new InvalidOperationException("Get must not run."));
        McpServerBuilder.WithSkills([primitive]);
        Assert.Throws<ArgumentException>(() => StartServer());
        Assert.Equal(0, primitive.GetCalls);
    }

    [Theory]
    [InlineData("skills/list", "frontmatter")]
    [InlineData("skills/list", "uri")]
    [InlineData("skills/get", "frontmatter")]
    [InlineData("skills/get", "uri")]
    [InlineData("skills/get", "null")]
    public async Task RuntimeRegisteredResultsStillEnforceStructureAndIdentity(string method, string failure)
    {
        GetSkillResult? result = null;
        var primitive = new RequestSkill(_registered.Id, _registered.ProtocolSkill, (_, _) => new(result!));
        int fallbacks = 0;
        McpServerBuilder.WithSkills([primitive]).WithSkills(options => options.GetSkillHandler = (_, _) =>
        {
            fallbacks++;
            throw new InvalidOperationException("The fallback must not run.");
        });

        StartServer();
        var entry = failure == "uri" ? CreateSkill("other").ProtocolSkill! : _registered.ProtocolSkill!;
        if (failure == "frontmatter") entry.Frontmatter["description"] = "";
        primitive.ListingEntry = entry;
        if (failure != "null") result = new GetSkillResult { Skill = entry };
        await using var client = await CreateMcpClientForServer();
        var error = await Assert.ThrowsAsync<McpProtocolException>(() => client.SendRequestAsync(new JsonRpcRequest
        {
            Method = method, Params = new JsonObject { ["uri"] = primitive.Id },
        }, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InternalError, error.ErrorCode);
        Assert.Equal(method == SkillsProtocol.MethodSkillsGet ? 1 : 0, primitive.GetCalls);
        Assert.Equal(0, fallbacks);
    }

    private sealed class RequestService;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResourceOverridesUseFirstWins_AndVerifiedReadsStillRejectMismatchedContent(bool applicationFirst)
    {
        var package = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: alpha\n---\n")]);
        var application = McpServerResource.Create(() => "application content", new() { UriTemplate = package.Skill.Id });
        if (applicationFirst) McpServerBuilder.WithResources([application]);
        McpServerBuilder.WithSkillPackages([package]);
        if (!applicationFirst) McpServerBuilder.WithResources([application]);
        StartServer();
        await using var client = await CreateMcpClientForServer();
        var entry = await client.GetSkillAsync(package.Skill.Id, cancellationToken: TestContext.Current.CancellationToken);
        if (applicationFirst)
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await client.ReadSkillResourceAsync(
                entry, entry.Uri, cancellationToken: TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Single((await client.ReadSkillResourceAsync(entry, entry.Uri, cancellationToken: TestContext.Current.CancellationToken)).Contents);
        }
    }

    private sealed class RequestSkill(
        string uri, Skill? listingEntry, McpRequestHandler<GetSkillRequestParams, GetSkillResult> handler)
        : McpServerSkill(uri)
    {
        public Skill? ListingEntry { get; set; } = listingEntry;
        public int GetCalls { get; private set; }
        public override IReadOnlyList<object> Metadata => ["request-skill"];
        public override Skill? ProtocolSkill => ListingEntry;

        public override ValueTask<GetSkillResult> GetAsync(
            RequestContext<GetSkillRequestParams> request, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return handler(request, cancellationToken);
        }
    }

    [Fact]
    public async Task RuntimeEntriesAndFilesHaveSeparateLifetimes()
    {
        var package = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: alpha\n---\n")]);
        McpSkillsOptions? options = null;
        McpServerBuilder.WithSkills([_registered]).WithResources(package.Resources).WithSkills(configured => options = configured);
        StartServer();
        await using var client = await CreateMcpClientForServer();
        var held = await client.GetSkillAsync(_registered.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(options!.SkillCollection.Remove(_registered));
        Assert.Empty(await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single((await client.ReadSkillResourceAsync(held, held.Uri, cancellationToken: TestContext.Current.CancellationToken)).Contents);
        var added = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: beta\ndescription: beta\n---\n")]);
        foreach (var resource in added.Resources) Server.ServerOptions.ResourceCollection!.Add(resource);
        options.SkillCollection.Add(added.Skill);
        Assert.Equal(added.Skill.Id, (await client.GetSkillAsync(added.Skill.Id, cancellationToken: TestContext.Current.CancellationToken)).Uri);
        Server.ServerOptions.ResourceCollection!.Clear();
        Assert.Equal(added.Skill.Id, Assert.Single(await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken)).Uri);
        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ReadResourceAsync(
            added.Skill.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CustomListHintsCoverTheCompleteAugmentedPage()
    {
        var page = new ListSkillsResult { TimeToLive = TimeSpan.FromMinutes(2), CacheScope = CacheScope.Public };
        McpServerBuilder.WithSkills([_registered]).WithSkills(options => options.ListSkillsHandler = (_, _) => new(page));

        StartServer();
        var clientOptions = TestMcpClient.CreateOptions();
        clientOptions.ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion;
        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.ListSkillsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken);
        Assert.Equal(_registered.Id, Assert.Single(result.Skills).Uri);
        Assert.Equal(TimeSpan.FromMinutes(2), result.TimeToLive);
        Assert.Equal(CacheScope.Public, result.CacheScope);
    }
}
