using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Regression tests for issue #1754: application-provided <c>resultType</c>, <c>ttlMs</c>, and
/// <c>cacheScope</c> properties must be omitted from legacy responses and preserved on 2026-07-28
/// responses without mutating the application result.
/// </summary>
/// <remarks>
/// These drive the in-process <see cref="ClientServerTestBase"/> client/server pair and inspect the
/// raw JSON-RPC result wire shape (via <see cref="McpSession.SendRequestAsync(JsonRpcRequest, System.Threading.CancellationToken)"/>)
/// so the assertions are about the actual serialized fields rather than deserialized objects.
/// The cases cover normal, cacheable, and immediate alternate typed results.
/// </remarks>
public class ProtocolVersionResultDecorationTests : ClientServerTestBase
{
    private const string ProtocolWarningMarker = "protocol-incompatible result field";
    private static readonly Implementation s_serverInfo = new() { Name = "result-gating-test", Version = "1" };
    private ListToolsResult _cacheableResult = null!;
    private ListResourcesResult _defaultedCacheableResult = null!;
    private ListPromptsResult _partialCacheableResult = null!;
    private GetPromptResult _normalResult = null!;
    private CallToolResult _immediateAlternateResult = null!;
    private Func<JsonRpcResponse, JsonRpcMessage>? _outgoingResponseTransform;

    public ProtocolVersionResultDecorationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        _cacheableResult = new()
        {
            ResultType = "handler-cacheable",
            TimeToLive = TimeSpan.FromSeconds(1),
            CacheScope = CacheScope.Private,
        };
        _defaultedCacheableResult = new();
        _partialCacheableResult = new() { TimeToLive = TimeSpan.FromMilliseconds(-5) };
        _normalResult = new() { ResultType = "handler-normal", Description = "normal" };
        _immediateAlternateResult = new()
        {
            ResultType = "handler-immediate",
            Content = [new TextContentBlock { Text = "immediate" }],
        };

        services.Configure<McpServerOptions>(options =>
        {
            options.ServerInfo = s_serverInfo;
            options.Handlers.ListToolsHandler = (_, _) => new(_cacheableResult);
            options.Filters.Request.ListToolsFilters.Add(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);
                result.ResultType = "filter-cacheable";
                result.TimeToLive = TimeSpan.FromMilliseconds(1_234);
                result.CacheScope = CacheScope.Public;
                return result;
            });
            options.Handlers.ListResourcesHandler = (_, _) => new(_defaultedCacheableResult);
            options.Handlers.ListPromptsHandler = (_, _) => new(_partialCacheableResult);
            options.Handlers.GetPromptHandler = (_, _) => new(_normalResult);
#pragma warning disable MCPEXP002
            options.Handlers.CallToolWithAlternateHandler = (_, _) =>
                new(new ResultOrAlternate<CallToolResult>(_immediateAlternateResult));
#pragma warning restore MCPEXP002
            options.Filters.Message.OutgoingFilters.Add(next => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcResponse response &&
                    _outgoingResponseTransform is { } transform)
                {
                    context.JsonRpcMessage = transform(response);
                }

                await next(context, cancellationToken);
            });
        });
    }

    [Theory]
    [InlineData(McpProtocolVersions.November2024ProtocolVersion)]
    [InlineData(McpProtocolVersions.March2025ProtocolVersion)]
    [InlineData(McpProtocolVersions.June2025ProtocolVersion)]
    [InlineData(McpProtocolVersions.November2025ProtocolVersion)]
    public async Task ToolsList_OnLegacySession_OmitsResultTypeAndCacheHints(string protocolVersion)
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = protocolVersion });

        Assert.Equal(protocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsList },
            TestContext.Current.CancellationToken);

        AssertExact(new JsonObject { ["tools"] = new JsonArray() }, response.Result);
        AssertProtocolWarning("resultType, ttlMs, cacheScope");
    }

    [Fact]
    public async Task ToolsCall_On2025_11_25Session_OmitsResultType()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = System.Text.Json.JsonSerializer.SerializeToNode(
                    new CallToolRequestParams { Name = "echo" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        AssertExact(JsonNode.Parse("""{"content":[{"type":"text","text":"immediate"}]}"""), response.Result);
        AssertProtocolWarning("resultType");
    }

    [Fact]
    public async Task PromptsGet_OnLegacySession_OmitsResultTypeAddedByOutgoingFilter()
    {
        _outgoingResponseTransform = response =>
        {
            if (response.Result is JsonObject result && result.ContainsKey("messages"))
            {
                result["resultType"] = "filter-normal";
            }

            return response;
        };

        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.PromptsGet,
                Params = JsonSerializer.SerializeToNode(
                    new GetPromptRequestParams { Name = "shared" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        AssertExact(JsonNode.Parse("""{"description":"normal","messages":[]}"""), response.Result);
        AssertProtocolWarning("resultType");
    }

    [Fact]
    public async Task ToolsCall_OnLegacySession_OmitsResultTypeAddedByOutgoingFilter()
    {
        _outgoingResponseTransform = response =>
        {
            if (response.Result is JsonObject result && result.ContainsKey("content"))
            {
                result["resultType"] = "filter-immediate";
            }

            return response;
        };

        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = JsonSerializer.SerializeToNode(
                    new CallToolRequestParams { Name = "echo" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        AssertExact(JsonNode.Parse("""{"content":[{"type":"text","text":"immediate"}]}"""), response.Result);
        AssertProtocolWarning("resultType");
    }

    [Fact]
    public async Task ToolsList_OnLegacySession_NormalizesFilterReplacementWithoutMutation()
    {
        var sharedFilterResult = JsonNode.Parse("""
            {
              "resultType": "filter-cacheable",
              "tools": [],
              "ttlMs": 2468,
              "cacheScope": "public"
            }
            """)!.AsObject();
        _outgoingResponseTransform = response =>
            response.Result is JsonObject result && result.ContainsKey("tools") ?
                new JsonRpcResponse
                {
                    Id = response.Id,
                    Result = sharedFilterResult,
                    Context = response.Context,
                } :
                response;

        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsList },
            TestContext.Current.CancellationToken);

        AssertExact(new JsonObject { ["tools"] = new JsonArray() }, response.Result);
        AssertExact(JsonNode.Parse("""
            {
              "resultType": "filter-cacheable",
              "tools": [],
              "ttlMs": 2468,
              "cacheScope": "public"
            }
            """), sharedFilterResult);
        AssertProtocolWarning("resultType, ttlMs, cacheScope");
    }

    [Fact]
    public async Task ToolsList_OnLegacySession_NormalizesEveryOutgoingFilterEmission()
    {
        await using var transport = new TestServerTransport();
        var options = new McpServerOptions
        {
            ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
            ServerInfo = s_serverInfo,
        };
        options.Handlers.ListToolsHandler = (_, _) => new(new ListToolsResult());
        options.Filters.Message.OutgoingFilters.Add(next => async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcResponse { Result: JsonObject result } &&
                result.ContainsKey("tools"))
            {
                result["resultType"] = "first";
                await next(context, cancellationToken);

                result["resultType"] = "second";
                await next(context, cancellationToken);
                return;
            }

            await next(context, cancellationToken);
        });

        await using var server = McpServer.Create(transport, options, LoggerFactory);
        Task runTask = server.RunAsync(TestContext.Current.CancellationToken);

        var initializeId = new RequestId("initialize");
        var listId = new RequestId("list");
        var initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var responses = new List<JsonRpcResponse>();
        transport.OnMessageSent = message =>
        {
            if (message is JsonRpcResponse response && response.Id == initializeId)
            {
                initialized.TrySetResult(true);
            }
            else if (message is JsonRpcResponse listResponse && listResponse.Id == listId)
            {
                responses.Add(listResponse);
            }
        };

        await transport.SendClientMessageAsync(new JsonRpcRequest
        {
            Id = initializeId,
            Method = RequestMethods.Initialize,
            Params = JsonSerializer.SerializeToNode(new InitializeRequestParams
            {
                ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
                Capabilities = new ClientCapabilities(),
                ClientInfo = new Implementation { Name = "result-gating-test", Version = "1" },
            }, McpJsonUtilities.DefaultOptions),
        }, TestContext.Current.CancellationToken);
        await initialized.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnMessageSent = message =>
        {
            if (message is JsonRpcResponse response && response.Id == listId)
            {
                responses.Add(response);
                if (responses.Count == 2)
                {
                    completed.TrySetResult(true);
                }
            }
        };

        await transport.SendClientMessageAsync(
            new JsonRpcNotification { Method = NotificationMethods.InitializedNotification },
            TestContext.Current.CancellationToken);
        await transport.SendClientMessageAsync(new JsonRpcRequest
        {
            Id = listId,
            Method = RequestMethods.ToolsList,
        }, TestContext.Current.CancellationToken);
        await completed.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.All(responses, response =>
            AssertExact(new JsonObject { ["tools"] = new JsonArray() }, response.Result));
        Assert.NotSame(responses[0], responses[1]);
        Assert.Single(MockLoggerProvider.LogMessages, message =>
            message.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning &&
            message.Message.Contains(ProtocolWarningMarker, StringComparison.Ordinal) &&
            message.Message.Contains(RequestMethods.ToolsList, StringComparison.Ordinal));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ToolsList_On2026_07_28Session_IncludesResultTypeAndCacheHints()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsList },
            TestContext.Current.CancellationToken);

        AssertExact(ModernResult(new JsonObject
        {
            ["resultType"] = "filter-cacheable",
            ["tools"] = new JsonArray(),
            ["ttlMs"] = 1_234,
            ["cacheScope"] = "public",
        }), response.Result);
        AssertNoProtocolWarning();
    }

    [Fact]
    public async Task ToolsCall_On2026_07_28Session_IncludesResultType()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = System.Text.Json.JsonSerializer.SerializeToNode(
                    new CallToolRequestParams { Name = "echo" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        AssertExact(ModernResult(JsonNode.Parse("""{"resultType":"handler-immediate","content":[{"type":"text","text":"immediate"}]}""")!.AsObject()), response.Result);
        AssertNoProtocolWarning();
    }

    [Fact]
    public async Task ResourcesList_On2026_07_28Session_AddsDefaultsWithoutMutatingResult()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ResourcesList },
            TestContext.Current.CancellationToken);

        AssertExact(ModernResult(new JsonObject
        {
            ["resultType"] = "complete",
            ["resources"] = new JsonArray(),
            ["ttlMs"] = 0,
            ["cacheScope"] = "private",
        }), response.Result);
        Assert.Null(_defaultedCacheableResult.ResultType);
        Assert.Null(_defaultedCacheableResult.TimeToLive);
        Assert.Null(_defaultedCacheableResult.CacheScope);
        AssertNoProtocolWarning();
    }

    [Fact]
    public async Task ToolsList_OnModernSession_RestoresFieldsRemovedByOutgoingFilter()
    {
        _outgoingResponseTransform = response =>
        {
            if (response.Result is JsonObject result && result.ContainsKey("tools"))
            {
                result.Remove("resultType");
                result.Remove("ttlMs");
                result.Remove("cacheScope");
            }

            return response;
        };

        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsList },
            TestContext.Current.CancellationToken);

        AssertExact(ModernResult(new JsonObject
        {
            ["resultType"] = "complete",
            ["tools"] = new JsonArray(),
            ["ttlMs"] = 0,
            ["cacheScope"] = "private",
        }), response.Result);
        AssertNoProtocolWarning();
    }

    [Fact]
    public async Task ResourcesList_On2025_11_25Session_WithNoApplicationFields_DoesNotWarn()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ResourcesList },
            TestContext.Current.CancellationToken);

        AssertExact(new JsonObject { ["resources"] = new JsonArray() }, response.Result);
        AssertNoProtocolWarning();
    }

    [Fact]
    public async Task PromptsList_OnLegacySession_OmitsOnlyExplicitCacheHint()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.PromptsList },
            TestContext.Current.CancellationToken);

        AssertExact(new JsonObject { ["prompts"] = new JsonArray() }, response.Result);
        AssertProtocolWarning("ttlMs");
        Assert.Null(_partialCacheableResult.ResultType);
        Assert.Equal(TimeSpan.FromMilliseconds(-5), _partialCacheableResult.TimeToLive);
        Assert.Null(_partialCacheableResult.CacheScope);
    }

    [Fact]
    public async Task PromptsList_OnModernSession_PreservesNegativeTtlAndDefaultsMissingFields()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.PromptsList },
            TestContext.Current.CancellationToken);

        AssertExact(ModernResult(new JsonObject
        {
            ["resultType"] = "complete",
            ["prompts"] = new JsonArray(),
            ["ttlMs"] = -5,
            ["cacheScope"] = "private",
        }), response.Result);
        AssertNoProtocolWarning();
        Assert.Null(_partialCacheableResult.ResultType);
        Assert.Equal(TimeSpan.FromMilliseconds(-5), _partialCacheableResult.TimeToLive);
        Assert.Null(_partialCacheableResult.CacheScope);
    }

    [Fact]
    public async Task LegacyResults_WarnOncePerMethod()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        for (int i = 0; i < 2; i++)
        {
            var response = await client.SendRequestAsync(
                new JsonRpcRequest { Method = RequestMethods.ToolsList },
                TestContext.Current.CancellationToken);

            AssertExact(new JsonObject { ["tools"] = new JsonArray() }, response.Result);
        }

        var promptsResponse = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.PromptsList },
            TestContext.Current.CancellationToken);
        AssertExact(new JsonObject { ["prompts"] = new JsonArray() }, promptsResponse.Result);

        Assert.Equal(2, MockLoggerProvider.LogMessages.Count(message =>
            message.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning &&
            message.Message.Contains(ProtocolWarningMarker, StringComparison.Ordinal)));
        Assert.Single(MockLoggerProvider.LogMessages, message =>
            message.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning &&
            message.Message.Contains(ProtocolWarningMarker, StringComparison.Ordinal) &&
            message.Message.Contains(RequestMethods.ToolsList, StringComparison.Ordinal));
        Assert.Single(MockLoggerProvider.LogMessages, message =>
            message.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning &&
            message.Message.Contains(ProtocolWarningMarker, StringComparison.Ordinal) &&
            message.Message.Contains(RequestMethods.PromptsList, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(McpProtocolVersions.November2025ProtocolVersion, false)]
    [InlineData(McpProtocolVersions.July2026ProtocolVersion, true)]
    public async Task PromptsGet_EmitsExactNormalResultShape(string protocolVersion, bool modern)
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = protocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.PromptsGet,
                Params = JsonSerializer.SerializeToNode(
                    new GetPromptRequestParams { Name = "shared" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        var expected = JsonNode.Parse(modern
            ? """{"resultType":"handler-normal","description":"normal","messages":[]}"""
            : """{"description":"normal","messages":[]}""")!.AsObject();
        AssertExact(modern ? ModernResult(expected) : expected, response.Result);
        if (modern)
        {
            AssertNoProtocolWarning();
        }
        else
        {
            AssertProtocolWarning("resultType");
        }
    }

    private static JsonObject ModernResult(JsonObject result)
    {
        result["_meta"] = new JsonObject
        {
            [MetaKeys.ServerInfo] = JsonSerializer.SerializeToNode(s_serverInfo, McpJsonUtilities.DefaultOptions),
        };
        return result;
    }

    private static void AssertExact(JsonNode? expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected: {expected}\nActual: {actual}");

    private void AssertProtocolWarning(string fields) =>
        Assert.Contains(MockLoggerProvider.LogMessages, message =>
            message.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning &&
            message.Message.Contains(ProtocolWarningMarker, StringComparison.Ordinal) &&
            message.Message.Contains($"'{fields}'", StringComparison.Ordinal));

    private void AssertNoProtocolWarning() =>
        Assert.DoesNotContain(MockLoggerProvider.LogMessages, message =>
            message.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning &&
            message.Message.Contains(ProtocolWarningMarker, StringComparison.Ordinal));
}
