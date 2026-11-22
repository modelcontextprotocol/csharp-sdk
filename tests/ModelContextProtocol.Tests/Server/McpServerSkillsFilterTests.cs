using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpServerSkillsFilterTests(ITestOutputHelper output) : ClientServerTestBase(output, startServer: false)
{
    private readonly McpServerSkill _registered = new MetadataSkill(CreateSkill("registered"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiltersAreBuiltOnce_AndWrapCompleteOperationsInOrder(bool get)
    {
        var calls = new List<string>();
        int built = 0;
        var application = CreateSkill("application").ProtocolSkill!;
        McpServerBuilder.WithSkills([_registered]).WithSkills(options =>
        {
            options.ListSkillsHandler = (_, _) => new(new ListSkillsResult { Skills = [application] });
            options.GetSkillHandler = (_, _) => throw new InvalidOperationException("Registered gets must not use the fallback.");
            foreach (string name in new[] { "first", "second" })
            {
                options.ListSkillsFilters.Add(Track<ListSkillsRequestParams, ListSkillsResult>(name));
                options.GetSkillFilters.Add(Track<GetSkillRequestParams, GetSkillResult>(name));
            }
        });

        Assert.Equal(0, built);
        StartServer();
        await using var client = await CreateMcpClientForServer();
        if (get)
        {
            Assert.Equal(_registered.Id, (await client.GetSkillAsync(
                _registered.Id, cancellationToken: TestContext.Current.CancellationToken)).Uri);
        }
        else
        {
            var skills = await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal([application.Uri, _registered.Id], skills.Select(skill => skill.Uri));
        }

        Assert.Equal(["first:before", "second:before", "second:after", "first:after"], calls);
        Assert.Equal(4, built);

        McpRequestFilter<TParams, TResult> Track<TParams, TResult>(string name) => next =>
        {
            built++;
            return async (context, cancellationToken) =>
            {
                calls.Add(name + ":before");
                var result = await next(context, cancellationToken);
                calls.Add(name + ":after");
                return result;
            };
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFiltersReceiveMatchedMetadata_AndCanDenyBothDispatchPaths(bool registered)
    {
        McpServerBuilder.WithSkills(registered ? [_registered] : []).WithSkills(options =>
        {
            options.GetSkillHandler = (_, _) => throw new InvalidOperationException("The fallback must not run.");
            options.GetSkillFilters.Add(_ => (context, _) =>
            {
                Assert.Equal(registered ? _registered : null, context.MatchedPrimitive);
                if (registered)
                {
                    Assert.Equal("reader", Assert.Single(_registered.Metadata));
                }
                throw new McpProtocolException("Denied.", McpErrorCode.InvalidParams);
            });
        });

        StartServer();
        await using var client = await CreateMcpClientForServer();
        var error = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.GetSkillAsync(
            "SKILL://REGISTERED/SKILL.md", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
    }

    [Theory]
    [InlineData(false, "valid")]
    [InlineData(true, "valid")]
    [InlineData(false, "null")]
    [InlineData(true, "null")]
    [InlineData(false, "null-entries")]
    [InlineData(true, "null-entries")]
    [InlineData(false, "null-entry")]
    [InlineData(false, "manifest")]
    [InlineData(true, "manifest")]
    public async Task ShortCircuitsReplaceTheCompleteOperation_ButStillValidateResults(bool get, string shape)
    {
        var entry = CreateSkill("registered").ProtocolSkill!;
        if (shape == "manifest") entry.Resources.Entries![0].Digest = "invalid";
        McpServerBuilder.WithSkills([_registered]).WithSkills(options =>
        {
            options.ListSkillsHandler = (_, _) => throw new InvalidOperationException("The handler must not run.");
            options.GetSkillHandler = (_, _) => throw new InvalidOperationException("The handler must not run.");
            options.ListSkillsFilters.Add(_ => (_, _) => new(shape == "null" ? null! : new ListSkillsResult
            {
                Skills = shape switch { "null-entries" => null!, "null-entry" => [null!], _ => [entry] },
            }));

            options.GetSkillFilters.Add(_ => (_, _) => new(shape == "null" ? null! : new GetSkillResult
            {
                Skill = shape == "null-entries" ? null! : entry,
            }));
        });

        StartServer();
        await using var client = await CreateMcpClientForServer();
        if (shape == "valid")
        {
            Assert.NotNull((await Request()).Result);
        }
        else
        {
            var error = await Assert.ThrowsAsync<McpProtocolException>(Request);
            Assert.Equal(McpErrorCode.InternalError, error.ErrorCode);
        }

        Task<JsonRpcResponse> Request() => client.SendRequestAsync(new JsonRpcRequest
        {
            Method = get ? SkillsProtocol.MethodSkillsGet : SkillsProtocol.MethodSkillsList,
            Params = new JsonObject { ["uri"] = _registered.Id },
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListFiltersSeeRegistrations_WithoutMutatingTheirCollection()
    {
        McpServerBuilder.WithSkills([_registered]).WithSkills(options => options.ListSkillsFilters.Add(next => async (context, cancellationToken) =>
        {
            var page = await next(context, cancellationToken);
            Assert.Equal(_registered.Id, Assert.Single(page.Skills).Uri);
            page.Skills.Clear();
            return page;
        }));
        StartServer();
        await using var client = await CreateMcpClientForServer();
        Assert.Empty(await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(_registered.Id, (await client.GetSkillAsync(
            _registered.Id, cancellationToken: TestContext.Current.CancellationToken)).Uri);
    }

    [Fact]
    public async Task GetFilterCannotRebindTheOriginalRequestByReplacingItsParameters()
    {
        var other = CreateSkill("other").ProtocolSkill!;
        McpServerBuilder.WithSkills([_registered]).WithSkills(options => options.GetSkillFilters.Add(_ => (context, _) =>
        {
            context.Params = new GetSkillRequestParams { Uri = other.Uri };
            return new(new GetSkillResult { Skill = other });
        }));
        StartServer();
        await using var client = await CreateMcpClientForServer();
        var error = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.GetSkillAsync(
            _registered.Id, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InternalError, error.ErrorCode);
        Assert.Equal("Request failed (remote): An error occurred.", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilterHintsOverrideOperationDefaults(bool get)
    {
        Result? filterResult = null;
        McpServerBuilder.WithSkills([_registered]).WithSkills(options =>
        {
            options.ListSkillsFilters.Add(next => async (context, cancellationToken) => Hints(await next(context, cancellationToken)));
            options.GetSkillFilters.Add(next => async (context, cancellationToken) => Hints(await next(context, cancellationToken)));
        });

        StartServer();
        var options = TestMcpClient.CreateOptions();
        options.ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion;
        await using var client = await CreateMcpClientForServer(options);
        var result = (await client.SendRequestAsync(new JsonRpcRequest
        {
            Method = get ? SkillsProtocol.MethodSkillsGet : SkillsProtocol.MethodSkillsList,
            Params = new JsonObject { ["uri"] = _registered.Id },
        }, TestContext.Current.CancellationToken)).Result!.AsObject();
        Assert.Equal("complete", result["resultType"]!.GetValue<string>());
        Assert.Equal(60_000, result["ttlMs"]!.GetValue<long>());
        Assert.Equal("public", result["cacheScope"]!.GetValue<string>());

        Assert.Equal("input", filterResult!.ResultType);
        var hints = Assert.IsAssignableFrom<ICacheableResult>(filterResult);
        Assert.Equal(TimeSpan.FromMinutes(1), hints.TimeToLive);
        Assert.Equal(CacheScope.Public, hints.CacheScope);

        T Hints<T>(T result) where T : Result, ICacheableResult
        {
            result.ResultType = "input";
            result.TimeToLive = TimeSpan.FromMinutes(1);
            result.CacheScope = CacheScope.Public;
            filterResult = result;
            return result;
        }
    }

    [Theory]
    [InlineData("skills/list", """{"cursor":42}""")]
    [InlineData("skills/get", """{"uri":42}""")]
    [InlineData("skills/get", """{}""")]
    public async Task InvalidParametersAreRejectedBeforeFilters(string method, string parameters)
    {
        McpServerBuilder.WithSkills(options =>
        {
            options.ListSkillsFilters.Add(_ => (_, _) => throw new InvalidOperationException("Invalid input reached a filter."));
            options.GetSkillFilters.Add(_ => (_, _) => throw new InvalidOperationException("Invalid input reached a filter."));
        });

        StartServer();
        await using var client = await CreateMcpClientForServer();
        var error = await Assert.ThrowsAsync<McpProtocolException>(() => client.SendRequestAsync(new JsonRpcRequest
        {
            Method = method, Params = JsonNode.Parse(parameters),
        }, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
        Assert.Equal("Request failed (remote): Invalid Skills parameters.", error.Message);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.Exception is McpProtocolException { InnerException: System.Text.Json.JsonException });
    }

    private static McpServerSkill CreateSkill(string name) =>
        McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", $"---\nname: {name}\ndescription: {name}\n---\n")]).Skill;

    private sealed class MetadataSkill(McpServerSkill inner) : McpServerSkill(inner.Id)
    {
        public override Skill? ProtocolSkill => inner.ProtocolSkill;
        public override IReadOnlyList<object> Metadata => ["reader"];
        public override ValueTask<GetSkillResult> GetAsync(RequestContext<GetSkillRequestParams> request, CancellationToken cancellationToken = default) =>
            inner.GetAsync(request, cancellationToken);
    }
}
