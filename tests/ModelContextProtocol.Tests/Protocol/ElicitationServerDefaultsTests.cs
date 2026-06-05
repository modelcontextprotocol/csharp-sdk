using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Tests verifying that the server applies elicitation schema defaults as defense-in-depth,
/// independent of the client. Uses a custom transport to bypass client-side default application.
/// </summary>
public class ElicitationServerDefaultsTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private static ElicitRequestParams.RequestSchema s_schemaWithDefaults => new()
    {
        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
        {
            ["name"] = new ElicitRequestParams.StringSchema { Description = "Name", Default = "John Doe" },
            ["age"] = new ElicitRequestParams.NumberSchema { Type = "integer", Description = "Age", Default = 30 },
            ["score"] = new ElicitRequestParams.NumberSchema { Description = "Score", Default = 95.5 },
            ["active"] = new ElicitRequestParams.BooleanSchema { Description = "Active", Default = true },
            ["status"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
            {
                Description = "Status",
                Enum = ["active", "inactive", "pending"],
                Default = "active",
            },
        },
    };

    [Fact]
    public async Task ServerDefenseInDepth_NullContent_AppliesDefaults()
    {
        // Simulate a client that returns accept with null content (no defaults applied).
        await using var transport = new ElicitationTestTransport(
            new ElicitResult { Action = "accept", Content = null });

        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.InitializeAsync();

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Provide info",
            RequestedSchema = s_schemaWithDefaults,
        }, CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Content);
        Assert.Equal(5, result.Content.Count);
        Assert.Equal("John Doe", result.Content["name"].GetString());
        Assert.Equal(30, result.Content["age"].GetDouble());
        Assert.Equal(95.5, result.Content["score"].GetDouble());
        Assert.True(result.Content["active"].GetBoolean());
        Assert.Equal("active", result.Content["status"].GetString());

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ServerDefenseInDepth_EmptyContent_AppliesDefaults()
    {
        await using var transport = new ElicitationTestTransport(
            new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement>() });

        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.InitializeAsync();

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Provide info",
            RequestedSchema = s_schemaWithDefaults,
        }, CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Content);
        Assert.Equal(5, result.Content.Count);
        Assert.Equal("John Doe", result.Content["name"].GetString());
        Assert.Equal(30, result.Content["age"].GetDouble());

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ServerDefenseInDepth_PartialContent_FillsMissing()
    {
        // Simulate a client returning accept with only some fields, without applying defaults.
        await using var transport = new ElicitationTestTransport(
            new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonElement.Parse("\"Alice\""),
                    ["active"] = JsonElement.Parse("true"),
                },
            });

        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.InitializeAsync();

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Provide info",
            RequestedSchema = s_schemaWithDefaults,
        }, CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Content);
        Assert.Equal(5, result.Content.Count);

        // User-provided values preserved
        Assert.Equal("Alice", result.Content["name"].GetString());
        Assert.True(result.Content["active"].GetBoolean());

        // Missing fields filled with server-side defaults
        Assert.Equal(30, result.Content["age"].GetDouble());
        Assert.Equal(95.5, result.Content["score"].GetDouble());
        Assert.Equal("active", result.Content["status"].GetString());

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ServerDefenseInDepth_AllFieldsProvided_NoChange()
    {
        await using var transport = new ElicitationTestTransport(
            new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonElement.Parse("\"Alice\""),
                    ["age"] = JsonElement.Parse("25"),
                    ["score"] = JsonElement.Parse("88.0"),
                    ["active"] = JsonElement.Parse("false"),
                    ["status"] = JsonElement.Parse("\"inactive\""),
                },
            });

        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.InitializeAsync();

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Provide info",
            RequestedSchema = s_schemaWithDefaults,
        }, CancellationToken.None);

        Assert.NotNull(result.Content);
        Assert.Equal(5, result.Content.Count);
        Assert.Equal("Alice", result.Content["name"].GetString());
        Assert.Equal(25, result.Content["age"].GetDouble());
        Assert.Equal(88.0, result.Content["score"].GetDouble());
        Assert.False(result.Content["active"].GetBoolean());
        Assert.Equal("inactive", result.Content["status"].GetString());

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ServerDefenseInDepth_Decline_NoDefaultsApplied()
    {
        await using var transport = new ElicitationTestTransport(
            new ElicitResult { Action = "decline" });

        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.InitializeAsync();

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Provide info",
            RequestedSchema = s_schemaWithDefaults,
        }, CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Null(result.Content);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ServerDefenseInDepth_Cancel_NoDefaultsApplied()
    {
        await using var transport = new ElicitationTestTransport(
            new ElicitResult { Action = "cancel" });

        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.InitializeAsync();

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Provide info",
            RequestedSchema = s_schemaWithDefaults,
        }, CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Null(result.Content);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ServerDefenseInDepth_SchemaWithNoDefaults_NoChange()
    {
        await using var transport = new ElicitationTestTransport(
            new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement>() });

        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.InitializeAsync();

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Provide info",
            RequestedSchema = new()
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["name"] = new ElicitRequestParams.StringSchema { Description = "Name" },
                    ["age"] = new ElicitRequestParams.NumberSchema { Type = "integer", Description = "Age" },
                },
            },
        }, CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Content);
        Assert.Empty(result.Content);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ServerDefenseInDepth_MultiSelectEnum()
    {
        await using var transport = new ElicitationTestTransport(
            new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement>() });

        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.InitializeAsync();

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Provide info",
            RequestedSchema = new()
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["tags"] = new ElicitRequestParams.UntitledMultiSelectEnumSchema
                    {
                        Description = "Tags",
                        Items = new ElicitRequestParams.UntitledEnumItemsSchema { Enum = ["a", "b", "c"] },
                        Default = ["a", "c"],
                    },
                    ["categories"] = new ElicitRequestParams.TitledMultiSelectEnumSchema
                    {
                        Description = "Categories",
                        Items = new ElicitRequestParams.TitledEnumItemsSchema
                        {
                            AnyOf =
                            [
                                new ElicitRequestParams.EnumSchemaOption { Const = "x", Title = "X" },
                                new ElicitRequestParams.EnumSchemaOption { Const = "y", Title = "Y" },
                            ],
                        },
                        Default = ["y"],
                    },
                },
            },
        }, CancellationToken.None);

        Assert.NotNull(result.Content);
        Assert.Equal(2, result.Content.Count);

        var tags = result.Content["tags"].EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["a", "c"], tags);

        var categories = result.Content["categories"].EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["y"], categories);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ServerDefenseInDepth_TitledSingleSelectEnum()
    {
        await using var transport = new ElicitationTestTransport(
            new ElicitResult { Action = "accept", Content = null });

        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        await using var server = McpServer.Create(transport, options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.InitializeAsync();

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Provide info",
            RequestedSchema = new()
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["priority"] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                    {
                        Description = "Priority",
                        OneOf =
                        [
                            new ElicitRequestParams.EnumSchemaOption { Const = "low", Title = "Low" },
                            new ElicitRequestParams.EnumSchemaOption { Const = "high", Title = "High" },
                        ],
                        Default = "low",
                    },
                },
            },
        }, CancellationToken.None);

        Assert.NotNull(result.Content);
        Assert.Single(result.Content);
        Assert.Equal("low", result.Content["priority"].GetString());

        await transport.DisposeAsync();
        await runTask;
    }

    /// <summary>
    /// Minimal transport that responds to elicitation requests with a pre-configured result,
    /// bypassing any client-side default application.
    /// </summary>
    private sealed class ElicitationTestTransport(ElicitResult elicitResult) : ITransport
    {
        private readonly Channel<JsonRpcMessage> _channel = Channel.CreateUnbounded<JsonRpcMessage>();
        private TaskCompletionSource<bool>? _initTcs;

        public ChannelReader<JsonRpcMessage> MessageReader => _channel.Reader;
        public bool IsConnected { get; private set; } = true;
        public string? SessionId => null;

        public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            if (message is JsonRpcResponse response && _initTcs is { } tcs && response.Id == new RequestId("init-1"))
            {
                tcs.TrySetResult(true);
            }
            else if (message is JsonRpcRequest { Method: RequestMethods.ElicitationCreate } req)
            {
                // Respond with the pre-configured elicitation result via Task.Run
                // to allow the server to finish registering the pending request.
                _ = Task.Run(async () =>
                {
                    await _channel.Writer.WriteAsync(new JsonRpcResponse
                    {
                        Id = req.Id,
                        Result = JsonSerializer.SerializeToNode(elicitResult, McpJsonUtilities.DefaultOptions),
                    }, cancellationToken);
                }, cancellationToken);
            }
        }

        public async Task InitializeAsync()
        {
            _initTcs = new TaskCompletionSource<bool>();
            await _channel.Writer.WriteAsync(new JsonRpcRequest
            {
                Id = new RequestId("init-1"),
                Method = RequestMethods.Initialize,
                Params = JsonSerializer.SerializeToNode(new InitializeRequestParams
                {
                    ProtocolVersion = "2024-11-05",
                    Capabilities = new ClientCapabilities { Elicitation = new() { Form = new() } },
                    ClientInfo = new Implementation { Name = "test-client", Version = "1.0.0" },
                }, McpJsonUtilities.DefaultOptions),
            }, CancellationToken.None);
            await _initTcs.Task.WaitAsync(TestConstants.DefaultTimeout, CancellationToken.None);
        }

        public ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            IsConnected = false;
            return default;
        }
    }
}
