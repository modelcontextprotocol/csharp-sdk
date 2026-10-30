using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpServerSkillsCompositionTests(ITestOutputHelper output) : ClientServerTestBase(output, startServer: false)
{
    private readonly McpServerSkill _registered = McpServerSkill.Create(
        "skill://catalog/alpha/SKILL.md",
        [McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n")],
        new() { Metadata = ["server-local-policy"] });

    [Fact]
    public async Task CustomListReplacesCollection_ButDoesNotRemoveEntriesOrResources()
    {
        McpServerBuilder.WithSkills([_registered], options =>
        {
            options.ListSkillsHandler = (context, _) => new(new ListSkillsResult
            {
                NextCursor = context.Params.Cursor is null ? "application-page" : null,
            });
        });
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var first = await client.ListSkillsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken);
        Assert.Empty(first.Skills);
        Assert.Equal("application-page", first.NextCursor);
        var second = await client.ListSkillsAsync(
            new ListSkillsRequestParams { Cursor = first.NextCursor }, TestContext.Current.CancellationToken);
        Assert.Empty(second.Skills);
        Assert.Null(second.NextCursor);

        var direct = await client.GetSkillAsync(_registered.Id, TestContext.Current.CancellationToken);
        Assert.Equal(_registered.Id, direct.Uri);
        Assert.Single(await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single((await client.ReadSkillResourceAsync(direct, direct.Uri, TestContext.Current.CancellationToken)).Contents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedCallsAddSkills_AndOnlyExplicitlyReconfiguredHandlersChange(bool clearListHandler)
    {
        var beta = McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: beta\ndescription: Beta\n---\n")]);
        McpServerBuilder.WithSkills([_registered], options =>
        {
            options.ListSkillsHandler = (_, _) => new(new ListSkillsResult());
            options.GetSkillHandler = (_, _) => throw new InvalidOperationException("Superseded handler.");
        });
        ServiceCollection.AddMcpServer().WithSkills([beta], options =>
            options.GetSkillHandler = (context, _) => new(new GetSkillResult { Skill = options.SkillCollection[context.Params.Uri].ProtocolSkill }));
        McpServerBuilder.WithSkills(options =>
        {
            if (clearListHandler)
            {
                options.ListSkillsHandler = null;
            }
        });
        StartServer();
        await using var client = await CreateMcpClientForServer();

        Assert.Equal(clearListHandler ? 2 : 0, (await client.ListSkillsAsync(TestContext.Current.CancellationToken)).Count);
        Assert.Equal(_registered.Id, (await client.GetSkillAsync(_registered.Id, TestContext.Current.CancellationToken)).Uri);
        Assert.Equal(beta.Id, (await client.GetSkillAsync(beta.Id, TestContext.Current.CancellationToken)).Uri);
        Assert.Equal(2, (await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task CustomGetReplacesLookup_AndReceivesTheActualPrimitiveWithoutAutomaticFallback()
    {
        int calls = 0;
        McpServerBuilder.WithSkills([_registered], options =>
        {
            options.GetSkillHandler = (context, _) =>
            {
                calls++;
                Assert.Same(_registered, context.MatchedPrimitive);
                Assert.Equal("server-local-policy", Assert.Single(context.MatchedPrimitive!.Metadata));
                throw new McpProtocolException("Denied by application policy.", McpErrorCode.InvalidParams);
            };
        });
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var listed = Assert.Single(await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(_registered.Id, listed.Uri);
        var error = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(_registered.Id, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CollectionMutationsAreLive_AndFileResourcesHaveAnExplicitSeparateLifetime()
    {
        McpSkillsOptions? skillsOptions = null;
        McpServerBuilder.WithSkills([_registered], options => skillsOptions = options);
        StartServer();
        await using var client = await CreateMcpClientForServer();
        var held = Assert.Single(await client.ListSkillsAsync(TestContext.Current.CancellationToken));

        Assert.Same(_registered, skillsOptions!.SkillCollection[_registered.Id]);
        Assert.True(skillsOptions.SkillCollection.Remove(_registered));
        Assert.Empty(await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(_registered.Id, TestContext.Current.CancellationToken));
        Assert.Single((await client.ReadSkillResourceAsync(held, held.Uri, TestContext.Current.CancellationToken)).Contents);

        var added = McpServerSkill.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: beta\ndescription: Beta\n---\n")]);
        foreach (var resource in added.Resources)
        {
            Server.ServerOptions.ResourceCollection!.Add(resource);
        }
        skillsOptions.SkillCollection.Add(added);
        Assert.Same(added, skillsOptions.SkillCollection[added.Id]);
        Assert.Throws<ArgumentException>(() => skillsOptions.SkillCollection.Add(added));
        var retrieved = await client.GetSkillAsync(added.Id, TestContext.Current.CancellationToken);
        Assert.Equal(added.Id, Assert.Single(await client.ListSkillsAsync(TestContext.Current.CancellationToken)).Uri);
        Assert.Single((await client.ReadSkillResourceAsync(retrieved, retrieved.Uri, TestContext.Current.CancellationToken)).Contents);
    }

    [Fact]
    public async Task UnboundedSourceCanListNothingAndGenerateOnlyTheRequestedEntry()
    {
        const string Uri = "skill://generated/skill-1000000000/SKILL.md";
        int generated = 0;
        McpServerBuilder.WithSkills(options =>
        {
            options.ListSkillsHandler = (_, _) => new(new ListSkillsResult());
            options.GetSkillHandler = (context, cancellationToken) =>
            {
                Assert.Null(context.MatchedPrimitive);
                Assert.Same(Server.ServerOptions, context.Server.ServerOptions);
                Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, context.Server.NegotiatedProtocolVersion);
                Assert.True(cancellationToken.CanBeCanceled);
                Assert.Equal(Uri, context.Params.Uri);
                generated++;
                return new(new GetSkillResult
                {
                    Skill = new()
                    {
                        Uri = context.Params.Uri,
                        Frontmatter = new JsonObject { ["name"] = "skill-1000000000", ["description"] = "Generated on demand" },
                        Resources = SkillResources.Dynamic,
                    },
                });
            };
        });
        StartServer();
        await using var client = await CreateMcpClientForServer();

        Assert.Empty(await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, generated);
        var entry = await client.GetSkillAsync(Uri, TestContext.Current.CancellationToken);
        Assert.True(entry.Resources.IsDynamic);
        Assert.Equal(1, generated);
    }

    [Theory]
    [InlineData("2026-07-28")]
    [InlineData("2025-11-25")]
    public async Task HandlerResultsAreNotMutated_AndCacheHintsAreOverriddenAndVersionGated(string version)
    {
        var listResult = new ListSkillsResult
        {
            Skills = [_registered.ProtocolSkill],
            ResultType = "handler-input",
            TimeToLive = TimeSpan.FromMinutes(2),
            CacheScope = CacheScope.Public,
            Meta = new JsonObject { ["example.com/result"] = new JsonObject { ["value"] = "owned" } },
        };
        var getResult = new GetSkillResult
        {
            Skill = _registered.ProtocolSkill,
            ResultType = "handler-input",
            TimeToLive = TimeSpan.FromMinutes(3),
            CacheScope = CacheScope.Public,
        };
        McpServerBuilder.WithSkills([_registered], options =>
        {
            options.TimeToLive = TimeSpan.FromMinutes(10);
            options.CacheScope = CacheScope.Private;
            options.ListSkillsHandler = (_, _) => new(listResult);
            options.GetSkillHandler = (_, _) => new(getResult);
        });
        StartServer();
        var clientOptions = TestMcpClient.CreateOptions();
        clientOptions.ProtocolVersion = version;
        await using var client = await CreateMcpClientForServer(clientOptions);

        foreach (string method in new[] { SkillsProtocol.MethodSkillsList, SkillsProtocol.MethodSkillsGet })
        {
            var result = (await client.SendRequestAsync(new JsonRpcRequest
            {
                Method = method,
                Params = new JsonObject { ["uri"] = _registered.Id },
            }, TestContext.Current.CancellationToken)).Result!.AsObject();
            if (version == McpProtocolVersions.July2026ProtocolVersion)
            {
                Assert.Equal("complete", result["resultType"]!.GetValue<string>());
                Assert.Equal(method == SkillsProtocol.MethodSkillsList ? 120_000 : 180_000, result["ttlMs"]!.GetValue<long>());
                Assert.Equal("public", result["cacheScope"]!.GetValue<string>());
            }
            else
            {
                Assert.False(result.ContainsKey("resultType"));
                Assert.False(result.ContainsKey("ttlMs"));
                Assert.False(result.ContainsKey("cacheScope"));
            }
            Assert.DoesNotContain("server-local-policy", result.ToJsonString());
        }
        Assert.Equal("handler-input", listResult.ResultType);
        Assert.Equal("handler-input", getResult.ResultType);
        Assert.Equal(TimeSpan.FromMinutes(2), listResult.TimeToLive);
        Assert.Equal(TimeSpan.FromMinutes(3), getResult.TimeToLive);
        Assert.Equal("owned", listResult.Meta["example.com/result"]!["value"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("skills/list", "null-result")]
    [InlineData("skills/list", "null-entries")]
    [InlineData("skills/list", "duplicate")]
    [InlineData("skills/list", "frontmatter")]
    [InlineData("skills/list", "manifest")]
    [InlineData("skills/get", "null-result")]
    [InlineData("skills/get", "null-entries")]
    [InlineData("skills/get", "frontmatter")]
    [InlineData("skills/get", "manifest")]
    public async Task InvalidHandlerResultsAreInternalErrors(string method, string failure)
    {
        var entry = _registered.ProtocolSkill;
        if (failure == "frontmatter")
        {
            entry.Frontmatter["description"] = "";
        }
        if (failure == "manifest")
        {
            entry.Resources.Resources![0].Digest = "sha256:invalid";
        }
        McpServerBuilder.WithSkills([_registered], options =>
        {
            options.ListSkillsHandler = (_, _) => new(failure == "null-result" ? null! : new ListSkillsResult
            {
                Skills = failure == "null-entries" ? null! : failure == "duplicate" ? [entry, entry] : [entry],
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
            Method = method,
            Params = new JsonObject { ["uri"] = _registered.Id },
        }, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InternalError, error.ErrorCode);
    }

    [Theory]
    [InlineData("skills/list")]
    [InlineData("skills/get")]
    public async Task ReplacementContentIsNotConstrainedByAnUnusedRegisteredManifest(string method)
    {
        var outer = McpServerSkill.Create("skill://catalog/SKILL.md",
        [
            McpServerSkillFile.FromText("SKILL.md", "---\nname: catalog\ndescription: Outer\n---\n"),
            McpServerSkillFile.FromText("alpha/SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\nchanged content\n"),
        ]).ProtocolSkill;
        McpServerBuilder.WithSkills([_registered], options =>
        {
            options.ListSkillsHandler = (_, _) => new(new ListSkillsResult { Skills = [outer] });
            options.GetSkillHandler = (_, _) => new(new GetSkillResult { Skill = outer });
        });
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var result = await client.SendRequestAsync(new JsonRpcRequest
        {
            Method = method,
            Params = new JsonObject { ["uri"] = outer.Uri },
        }, TestContext.Current.CancellationToken);
        var entry = method == SkillsProtocol.MethodSkillsList ? Assert.Single(result.Result!["skills"]!.AsArray()) : result.Result!["skill"];
        Assert.Equal(outer.Uri, entry!["uri"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReplacingARegisteredEntryCanServeCallerSpecificFrontmatterAndFileContent()
    {
        var replacement = McpServerSkill.Create(_registered.Id,
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: Caller-specific Alpha\n---\nNew instructions\n")]);
        var entryOnly = new McpServerSkill(_registered.ProtocolSkill);
        McpServerBuilder
            .WithSkills([entryOnly], options =>
            {
                options.ListSkillsHandler = (_, _) => new(new ListSkillsResult { Skills = [replacement.ProtocolSkill] });
                options.GetSkillHandler = (context, _) =>
                {
                    Assert.Same(entryOnly, context.MatchedPrimitive);
                    return new(new GetSkillResult { Skill = replacement.ProtocolSkill });
                };
            })
            .WithReadResourceHandler((context, cancellationToken) =>
                replacement.Resources[0].ReadAsync(context, cancellationToken));
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var listed = Assert.Single(await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        var retrieved = await client.GetSkillAsync(_registered.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Caller-specific Alpha", listed.Description);
        Assert.Equal(listed.Description, retrieved.Description);
        Assert.NotEqual(_registered.ProtocolSkill.Resources.Resources![0].Digest, retrieved.Resources.Resources![0].Digest);
        Assert.Equal("Alpha", entryOnly.ProtocolSkill.Description);
        Assert.Single((await client.ReadSkillResourceAsync(retrieved, retrieved.Uri, TestContext.Current.CancellationToken)).Contents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongUriIsRejectedEvenWhenHandlerMutatesTypedParameters(bool mutateParameters)
    {
        var other = McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: beta\ndescription: Beta\n---\n")]);
        McpServerBuilder.WithSkills([_registered], options =>
        {
            options.GetSkillHandler = (context, _) =>
            {
                if (mutateParameters)
                {
                    context.Params.Uri = other.Id;
                }
                return new(new GetSkillResult { Skill = other.ProtocolSkill });
            };
        });
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var error = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(_registered.Id, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InternalError, error.ErrorCode);
        Assert.Contains(_registered.Id, error.Message);
    }

    [Fact]
    public async Task CustomPageCannotContainConflictingSharedFilesEvenWithoutRegisteredPrimitives()
    {
        var outer = McpServerSkill.Create("skill://catalog/SKILL.md",
        [
            McpServerSkillFile.FromText("SKILL.md", "---\nname: catalog\ndescription: Outer\n---\n"),
            McpServerSkillFile.FromText("alpha/SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\nchanged content\n"),
        ]);
        McpServerBuilder.WithSkills(options =>
            options.ListSkillsHandler = (_, _) => new(new ListSkillsResult { Skills = [outer.ProtocolSkill, _registered.ProtocolSkill] }));
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var error = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InternalError, error.ErrorCode);
    }

}
