using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Tests verifying that the client applies elicitation schema defaults independently of the server.
/// Uses a custom transport that acts as a server, sending elicitation requests and capturing
/// the raw response the client sends back, before any server-side default application.
/// </summary>
public class ElicitationClientDefaultsTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private static readonly ElicitRequestParams s_elicitParamsWithDefaults = new()
    {
        Message = "Provide info",
        RequestedSchema = new()
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
        },
    };

    [Fact]
    public async Task ClientAppliesDefaults_NullContent()
    {
        // Client handler returns accept with null content.
        // The client should fill in all defaults before sending the response.
        var ct = TestContext.Current.CancellationToken;
        await using ClientElicitationTestTransport transport = new(s_elicitParamsWithDefaults);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            Handlers = new()
            {
                ElicitationHandler = (_, _) =>
                    new(new ElicitResult { Action = "accept", Content = null }),
            },
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        var rawResult = await transport.SendElicitationAndGetRawResponseAsync(ct);

        Assert.Equal("accept", rawResult.Action);
        Assert.NotNull(rawResult.Content);
        Assert.Equal(5, rawResult.Content.Count);
        Assert.Equal("John Doe", rawResult.Content["name"].GetString());
        Assert.Equal(30, rawResult.Content["age"].GetDouble());
        Assert.Equal(95.5, rawResult.Content["score"].GetDouble());
        Assert.True(rawResult.Content["active"].GetBoolean());
        Assert.Equal("active", rawResult.Content["status"].GetString());
    }

    [Fact]
    public async Task ClientAppliesDefaults_EmptyContent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using ClientElicitationTestTransport transport = new(s_elicitParamsWithDefaults);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            Handlers = new()
            {
                ElicitationHandler = (_, _) =>
                    new(new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement>() }),
            },
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        var rawResult = await transport.SendElicitationAndGetRawResponseAsync(ct);

        Assert.Equal("accept", rawResult.Action);
        Assert.NotNull(rawResult.Content);
        Assert.Equal(5, rawResult.Content.Count);
        Assert.Equal("John Doe", rawResult.Content["name"].GetString());
        Assert.Equal(30, rawResult.Content["age"].GetDouble());
    }

    [Fact]
    public async Task ClientAppliesDefaults_PartialContent()
    {
        // Client handler returns accept with only some fields.
        // The client should fill in missing defaults before sending the response.
        var ct = TestContext.Current.CancellationToken;
        await using ClientElicitationTestTransport transport = new(s_elicitParamsWithDefaults);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            Handlers = new()
            {
                ElicitationHandler = (_, _) =>
                    new(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["name"] = JsonElement.Parse("\"Alice\""),
                            ["active"] = JsonElement.Parse("false"),
                        },
                    }),
            },
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        var rawResult = await transport.SendElicitationAndGetRawResponseAsync(ct);

        Assert.Equal("accept", rawResult.Action);
        Assert.NotNull(rawResult.Content);
        Assert.Equal(5, rawResult.Content.Count);

        // User-provided values preserved
        Assert.Equal("Alice", rawResult.Content["name"].GetString());
        Assert.False(rawResult.Content["active"].GetBoolean());

        // Missing fields filled with client-side defaults
        Assert.Equal(30, rawResult.Content["age"].GetDouble());
        Assert.Equal(95.5, rawResult.Content["score"].GetDouble());
        Assert.Equal("active", rawResult.Content["status"].GetString());
    }

    [Fact]
    public async Task ClientAppliesDefaults_AllFieldsProvided_NoChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await using ClientElicitationTestTransport transport = new(s_elicitParamsWithDefaults);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            Handlers = new()
            {
                ElicitationHandler = (_, _) =>
                    new(new ElicitResult
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
                    }),
            },
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        var rawResult = await transport.SendElicitationAndGetRawResponseAsync(ct);

        Assert.NotNull(rawResult.Content);
        Assert.Equal(5, rawResult.Content.Count);
        Assert.Equal("Alice", rawResult.Content["name"].GetString());
        Assert.Equal(25, rawResult.Content["age"].GetDouble());
        Assert.Equal(88.0, rawResult.Content["score"].GetDouble());
        Assert.False(rawResult.Content["active"].GetBoolean());
        Assert.Equal("inactive", rawResult.Content["status"].GetString());
    }

    [Fact]
    public async Task ClientAppliesDefaults_Decline_NoDefaultsApplied()
    {
        var ct = TestContext.Current.CancellationToken;
        await using ClientElicitationTestTransport transport = new(s_elicitParamsWithDefaults);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            Handlers = new()
            {
                ElicitationHandler = (_, _) =>
                    new(new ElicitResult { Action = "decline" }),
            },
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        var rawResult = await transport.SendElicitationAndGetRawResponseAsync(ct);

        Assert.Equal("decline", rawResult.Action);
        Assert.Null(rawResult.Content);
    }

    [Fact]
    public async Task ClientAppliesDefaults_Cancel_NoDefaultsApplied()
    {
        var ct = TestContext.Current.CancellationToken;
        await using ClientElicitationTestTransport transport = new(s_elicitParamsWithDefaults);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            Handlers = new()
            {
                ElicitationHandler = (_, _) => new(new ElicitResult { Action = "cancel" }),
            },
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        var rawResult = await transport.SendElicitationAndGetRawResponseAsync(ct);

        Assert.Equal("cancel", rawResult.Action);
        Assert.Null(rawResult.Content);
    }

    [Fact]
    public async Task ClientAppliesDefaults_SchemaWithNoDefaults_NoChange()
    {
        ElicitRequestParams paramsNoDefaults = new()
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
        };

        var ct = TestContext.Current.CancellationToken;
        await using ClientElicitationTestTransport transport = new(paramsNoDefaults);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            Handlers = new()
            {
                ElicitationHandler = (_, _) =>
                    new(new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement>() }),
            },
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        var rawResult = await transport.SendElicitationAndGetRawResponseAsync(ct);

        Assert.True(rawResult.IsAccepted);
        Assert.NotNull(rawResult.Content);
        Assert.Empty(rawResult.Content);
    }

    [Fact]
    public async Task ClientAppliesDefaults_MultiSelectEnum()
    {
        ElicitRequestParams paramsMultiSelect = new()
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
        };

        var ct = TestContext.Current.CancellationToken;
        await using ClientElicitationTestTransport transport = new(paramsMultiSelect);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            Handlers = new()
            {
                ElicitationHandler = (_, _) =>
                    new(new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement>() }),
            },
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        var rawResult = await transport.SendElicitationAndGetRawResponseAsync(ct);

        Assert.NotNull(rawResult.Content);
        Assert.Equal(2, rawResult.Content.Count);

        var tags = rawResult.Content["tags"].EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["a", "c"], tags);

        var categories = rawResult.Content["categories"].EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["y"], categories);
    }

    [Fact]
    public async Task ClientAppliesDefaults_TitledSingleSelectEnum()
    {
        ElicitRequestParams paramsTitledEnum = new()
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
        };

        var ct = TestContext.Current.CancellationToken;
        await using ClientElicitationTestTransport transport = new(paramsTitledEnum);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            Handlers = new()
            {
                ElicitationHandler = (_, _) =>
                    new(new ElicitResult { Action = "accept", Content = null }),
            },
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        var rawResult = await transport.SendElicitationAndGetRawResponseAsync(ct);

        Assert.NotNull(rawResult.Content);
        Assert.Single(rawResult.Content);
        Assert.Equal("low", rawResult.Content["priority"].GetString());
    }

    /// <summary>
    /// Minimal transport that acts as a server, sending elicitation requests to the client
    /// and capturing the raw response, bypassing any server-side default application.
    /// </summary>
    private sealed class ClientElicitationTestTransport(ElicitRequestParams elicitParams) : IClientTransport
    {
        private readonly Channel<JsonRpcMessage> _incomingToClient = Channel.CreateUnbounded<JsonRpcMessage>();
        private readonly TaskCompletionSource<ElicitResult> _elicitResultTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _nextRequestId;

        public string Name => "test-elicitation-transport";

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ITransport transport = new TransportChannel(_incomingToClient, this);
            return Task.FromResult(transport);
        }

        public async Task<ElicitResult> SendElicitationAndGetRawResponseAsync(CancellationToken cancellationToken)
        {
            var requestId = new RequestId(Interlocked.Increment(ref _nextRequestId).ToString());
            await _incomingToClient.Writer.WriteAsync(new JsonRpcRequest
            {
                Id = requestId,
                Method = RequestMethods.ElicitationCreate,
                Params = JsonSerializer.SerializeToNode(elicitParams, McpJsonUtilities.DefaultOptions),
            }, cancellationToken);

            return await _elicitResultTcs.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
        }

        private void HandleOutgoingMessage(JsonRpcMessage message)
        {
            if (message is JsonRpcRequest { Method: RequestMethods.Initialize } initReq)
            {
                // Respond to initialize
                _ = Task.Run(async () =>
                {
                    await _incomingToClient.Writer.WriteAsync(new JsonRpcResponse
                    {
                        Id = initReq.Id,
                        Result = JsonSerializer.SerializeToNode(new InitializeResult
                        {
                            ProtocolVersion = "2025-03-26",
                            Capabilities = new ServerCapabilities(),
                            ServerInfo = new Implementation { Name = "test-server", Version = "1.0.0" },
                        }, McpJsonUtilities.DefaultOptions),
                    }, CancellationToken.None);
                });
            }
            else if (message is JsonRpcResponse response)
            {
                // Capture the raw elicitation response from the client
                if (response.Result is { } resultNode &&
                    JsonSerializer.Deserialize<ElicitResult>(resultNode, McpJsonUtilities.DefaultOptions) is { } result)
                {
                    _elicitResultTcs.TrySetResult(result);
                }
            }
        }

        public ValueTask DisposeAsync() => default;

        private sealed class TransportChannel(
            Channel<JsonRpcMessage> incoming,
            ClientElicitationTestTransport parent) : ITransport
        {
            public ChannelReader<JsonRpcMessage> MessageReader => incoming.Reader;
            public bool IsConnected { get; private set; } = true;
            public string? SessionId => null;

            public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
            {
                parent.HandleOutgoingMessage(message);
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                incoming.Writer.TryComplete();
                IsConnected = false;
                return default;
            }
        }
    }
}
