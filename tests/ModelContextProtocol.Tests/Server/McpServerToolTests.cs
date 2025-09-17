using Json.Schema;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit.Sdk;

namespace ModelContextProtocol.Tests.Server;

public partial class McpServerToolTests
{
    private static JsonRpcRequest CreateTestJsonRpcRequest()
    {
        return new JsonRpcRequest
        {
            Id = new RequestId("test-id"),
            Method = "test/method",
            Params = null
        };
    }

    public McpServerToolTests()
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    [Fact]
    public void Create_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("function", () => McpServerTool.Create((AIFunction)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((MethodInfo)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((MethodInfo)null!, typeof(object)));
        Assert.Throws<ArgumentNullException>("createTargetFunc", () => McpServerTool.Create(typeof(McpServerToolTests).GetMethod(nameof(Create_InvalidArgs_Throws))!, null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((Delegate)null!));

        Assert.NotNull(McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.InstanceMethod))!, new DisposableToolType()));
        Assert.NotNull(McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.StaticMethod))!));
        Assert.Throws<ArgumentNullException>("target", () => McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.InstanceMethod))!, target: null!));
    }

    [Fact]
    public async Task SupportsMcpServer()
    {
        Mock<McpServer> mockServer = new();

        McpServerTool tool = McpServerTool.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return "42";
        });

        Assert.DoesNotContain("server", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema, McpJsonUtilities.DefaultOptions));

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task SupportsCtorInjection()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        Mock<McpServer> mockServer = new();
        mockServer.SetupGet(s => s.Services).Returns(services);

        MethodInfo? testMethod = typeof(HasCtorWithSpecialParameters).GetMethod(nameof(HasCtorWithSpecialParameters.TestTool));
        Assert.NotNull(testMethod);
        McpServerTool tool = McpServerTool.Create(testMethod, r =>
        {
            Assert.NotNull(r.Services);
            return ActivatorUtilities.CreateInstance(r.Services, typeof(HasCtorWithSpecialParameters));
        }, new() { Services = services });

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.Single(result.Content);
        Assert.Equal("True True True True", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    private sealed class HasCtorWithSpecialParameters
    {
        private readonly MyService _ms;
        private readonly McpServer _server;
        private readonly RequestContext<CallToolRequestParams> _request;
        private readonly IProgress<ProgressNotificationValue> _progress;

        public HasCtorWithSpecialParameters(MyService ms, McpServer server, RequestContext<CallToolRequestParams> request, IProgress<ProgressNotificationValue> progress)
        {
            Assert.NotNull(ms);
            Assert.NotNull(server);
            Assert.NotNull(request);
            Assert.NotNull(progress);

            _ms = ms;
            _server = server;
            _request = request;
            _progress = progress;
        }

        public string TestTool() => $"{_ms is not null} {_server is not null} {_request is not null} {_progress is not null}";
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task SupportsServiceFromDI(ServiceLifetime injectedArgumentLifetime)
    {
        MyService singletonService = new();

        ServiceCollection sc = new();
        switch (injectedArgumentLifetime)
        {
            case ServiceLifetime.Singleton:
                sc.AddSingleton(singletonService);
                break;

            case ServiceLifetime.Scoped:
                sc.AddScoped(_ => new MyService());
                break;

            case ServiceLifetime.Transient:
                sc.AddTransient(_ => new MyService());
                break;
        }

        sc.AddSingleton(services =>
        {
            return McpServerTool.Create((MyService actualMyService) =>
            {
                Assert.NotNull(actualMyService);
                if (injectedArgumentLifetime == ServiceLifetime.Singleton)
                {
                    Assert.Same(singletonService, actualMyService);
                }

                return "42";
            }, new() { Services = services });
        });

        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = services.GetRequiredService<McpServerTool>();

        Assert.DoesNotContain("actualMyService", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema, McpJsonUtilities.DefaultOptions));

        Mock<McpServer> mockServer = new();

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken));

        mockServer.SetupGet(s => s.Services).Returns(services);

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Services = services },
            TestContext.Current.CancellationToken);
        Assert.Equal("42", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task SupportsOptionalServiceFromDI()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = McpServerTool.Create((MyService? actualMyService = null) =>
        {
            Assert.Null(actualMyService);
            return "42";
        }, new() { Services = services });

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task SupportsDisposingInstantiatedDisposableTargets()
    {
        McpServerToolCreateOptions options = new() { SerializerOptions = JsonContext2.Default.Options };
        McpServerTool tool1 = McpServerTool.Create(
            typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.InstanceMethod))!,
            _ => new DisposableToolType(),
            options);

        var result = await tool1.InvokeAsync(
            new RequestContext<CallToolRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.Equal("""{"disposals":1}""", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task SupportsAsyncDisposingInstantiatedAsyncDisposableTargets()
    {
        McpServerToolCreateOptions options = new() { SerializerOptions = JsonContext2.Default.Options };
        McpServerTool tool1 = McpServerTool.Create(
            typeof(AsyncDisposableToolType).GetMethod(nameof(AsyncDisposableToolType.InstanceMethod))!,
            _ => new AsyncDisposableToolType(),
            options);

        var result = await tool1.InvokeAsync(
            new RequestContext<CallToolRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.Equal("""{"asyncDisposals":1}""", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task SupportsAsyncDisposingInstantiatedAsyncDisposableAndDisposableTargets()
    {
        ServiceCollection sc = new();
        sc.AddSingleton<MyService>();
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerToolCreateOptions options = new() { SerializerOptions = JsonContext2.Default.Options };
        McpServerTool tool1 = McpServerTool.Create(
            typeof(AsyncDisposableAndDisposableToolType).GetMethod(nameof(AsyncDisposableAndDisposableToolType.InstanceMethod))!,
            static r => ActivatorUtilities.CreateInstance(r.Services!, typeof(AsyncDisposableAndDisposableToolType)),
            options);

        var result = await tool1.InvokeAsync(
            new RequestContext<CallToolRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Services = services },
            TestContext.Current.CancellationToken);
        Assert.Equal("""{"asyncDisposals":1,"disposals":0}""", (result.Content[0] as TextContentBlock)?.Text);
    }


    [Fact]
    public async Task CanReturnCollectionOfAIContent()
    {
        Mock<McpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<AIContent> {
                new TextContent("text"),
                new DataContent("data:image/png;base64,1234"),
                new DataContent("data:audio/wav;base64,1234")
            };
        }, new() { SerializerOptions = JsonContext2.Default.Options });

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Content.Count);

        Assert.Equal("text", (result.Content[0] as TextContentBlock)?.Text);

        Assert.Equal("1234", (result.Content[1] as ImageContentBlock)?.Data);
        Assert.Equal("image/png", (result.Content[1] as ImageContentBlock)?.MimeType);

        Assert.Equal("1234", (result.Content[2] as AudioContentBlock)?.Data);
        Assert.Equal("audio/wav", (result.Content[2] as AudioContentBlock)?.MimeType);
    }

    [Theory]
    [InlineData("text", "text")]
    [InlineData("data:image/png;base64,1234", "image")]
    [InlineData("data:audio/wav;base64,1234", "audio")]
    public async Task CanReturnSingleAIContent(string data, string type)
    {
        Mock<McpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return type switch
            {
                "text" => (AIContent)new TextContent(data),
                "image" => new DataContent(data),
                "audio" => new DataContent(data),
                _ => throw new ArgumentException("Invalid type")
            };
        });

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Content);
        Assert.Equal(type, result.Content[0].Type);

        if (result.Content[0] is TextContentBlock tc)
        {
            Assert.Equal(data, tc.Text);
        }
        else if (result.Content[0] is ImageContentBlock ic)
        {
            Assert.Equal(data.Split(',').Last(), ic.Data);
            Assert.Equal("image/png", ic.MimeType);
        }
        else if (result.Content[0] is AudioContentBlock ac)
        {
            Assert.Equal(data.Split(',').Last(), ac.Data);
            Assert.Equal("audio/wav", ac.MimeType);
        }
        else
        {
            throw new XunitException($"Unexpected content type: {result.Content[0].GetType()}");
        }
    }

    [Fact]
    public async Task CanReturnNullAIContent()
    {
        Mock<McpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return (string?)null;
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.Empty(result.Content);
    }

    [Fact]
    public async Task CanReturnString()
    {
        Mock<McpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return "42";
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.Single(result.Content);
        Assert.Equal("42", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CanReturnCollectionOfStrings()
    {
        Mock<McpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<string> { "42", "43" };
        }, new() { SerializerOptions = JsonContext2.Default.Options });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.Single(result.Content);
        Assert.Equal("""["42","43"]""", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CanReturnMcpContent()
    {
        Mock<McpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new TextContentBlock { Text = "42" };
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.Single(result.Content);
        Assert.Equal("42", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        Assert.Equal("text", result.Content[0].Type);
    }

    [Fact]
    public async Task CanReturnCollectionOfMcpContent()
    {
        Mock<McpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return (IList<ContentBlock>)
            [
                new TextContentBlock { Text = "42" },
                new ImageContentBlock { Data = "1234", MimeType = "image/png" }
            ];
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Content.Count);
        Assert.Equal("42", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        Assert.Equal("1234", Assert.IsType<ImageContentBlock>(result.Content[1]).Data);
        Assert.Equal("image/png", Assert.IsType<ImageContentBlock>(result.Content[1]).MimeType);
    }

    [Fact]
    public async Task CanReturnCallToolResult()
    {
        CallToolResult response = new()
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = "text" }, new ImageContentBlock { Data = "1234", MimeType = "image/png" } }
        };

        Mock<McpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return response;
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()),
            TestContext.Current.CancellationToken);

        Assert.Same(response, result);

        Assert.Equal(2, result.Content.Count);
        Assert.Equal("text", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        Assert.Equal("1234", Assert.IsType<ImageContentBlock>(result.Content[1]).Data);
    }

    [Fact]
    public async Task SupportsSchemaCreateOptions()
    {
        AIJsonSchemaCreateOptions schemaCreateOptions = new()
        {
            TransformSchemaNode = (context, node) =>
            {
                node["text"] = "1234";
                return node;
            },
        };

        McpServerTool tool = McpServerTool.Create((int num, string str) =>
        {
            return "42";
        }, new() { SchemaCreateOptions = schemaCreateOptions });

        Assert.All(
            tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject(),
            x => Assert.True(x.Value.TryGetProperty("text", out JsonElement value) && value.ToString() == "1234")
        );
    }

    [Theory]
    [MemberData(nameof(StructuredOutput_ReturnsExpectedSchema_Inputs))]
    public async Task StructuredOutput_Enabled_ReturnsExpectedSchema<T>(T value)
    {
        JsonSerializerOptions options = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        McpServerTool tool = McpServerTool.Create(() => value, new() { Name = "tool", UseStructuredContent = true, SerializerOptions = options });
        var mockServer = new Mock<McpServer>();
        var request = new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest())
        {
            Params = new CallToolRequestParams { Name = "tool" },
        };

        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(tool.ProtocolTool.OutputSchema);
        Assert.Equal("object", tool.ProtocolTool.OutputSchema.Value.GetProperty("type").GetString());
        Assert.NotNull(result.StructuredContent);
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema.Value, result.StructuredContent);
    }

    [Fact]
    public async Task StructuredOutput_Enabled_VoidReturningTools_ReturnsExpectedSchema()
    {
        McpServerTool tool = McpServerTool.Create(() => { });
        var mockServer = new Mock<McpServer>();
        var request = new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest())
        {
            Params = new CallToolRequestParams { Name = "tool" },
        };

        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(tool.ProtocolTool.OutputSchema);
        Assert.Null(result.StructuredContent);

        tool = McpServerTool.Create(() => Task.CompletedTask);
        request = new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest())
        {
            Params = new CallToolRequestParams { Name = "tool" },
        };

        result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(tool.ProtocolTool.OutputSchema);
        Assert.Null(result.StructuredContent);

        tool = McpServerTool.Create(() => default(ValueTask));
        request = new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest())
        {
            Params = new CallToolRequestParams { Name = "tool" },
        };

        result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(tool.ProtocolTool.OutputSchema);
        Assert.Null(result.StructuredContent);
    }

    [Theory]
    [MemberData(nameof(StructuredOutput_ReturnsExpectedSchema_Inputs))]
    public async Task StructuredOutput_Disabled_ReturnsExpectedSchema<T>(T value)
    {
        JsonSerializerOptions options = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        McpServerTool tool = McpServerTool.Create(() => value, new() { UseStructuredContent = false, SerializerOptions = options });
        var mockServer = new Mock<McpServer>();
        var request = new RequestContext<CallToolRequestParams>(mockServer.Object, CreateTestJsonRpcRequest())
        {
            Params = new CallToolRequestParams { Name = "tool" },
        };

        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(tool.ProtocolTool.OutputSchema);
        Assert.Null(result.StructuredContent);
    }

    [Theory]
    [InlineData(JsonNumberHandling.Strict)]
    [InlineData(JsonNumberHandling.AllowReadingFromString)]
    public async Task ToolWithNullableParameters_ReturnsExpectedSchema(JsonNumberHandling nunmberHandling)
    {
        JsonSerializerOptions options = new(JsonContext2.Default.Options) { NumberHandling = nunmberHandling };
        McpServerTool tool = McpServerTool.Create((int? x = 42, DateTimeOffset? y = null) => { }, new() { SerializerOptions = options });

        JsonElement expectedSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "x": { "type": ["integer", "null"], "default": 42 },
                    "y": { "type": ["string", "null"], "format": "date-time", "default": null }
                }
            }
            """).RootElement;

        Assert.True(JsonElement.DeepEquals(expectedSchema, tool.ProtocolTool.InputSchema));
    }

    public static IEnumerable<object[]> StructuredOutput_ReturnsExpectedSchema_Inputs()
    {
        yield return new object[] { "string" };
        yield return new object[] { 42 };
        yield return new object[] { 3.14 };
        yield return new object[] { true };
        yield return new object[] { new() };
        yield return new object[] { new List<string> { "item1", "item2" } };
        yield return new object[] { new Dictionary<string, int> { ["key1"] = 1, ["key2"] = 2 } };
        yield return new object[] { new Person("John", 27) };
    }

    private sealed class MyService;

    private class DisposableToolType : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose()
        {
            Disposals++;
        }

        public object InstanceMethod()
        {
            if (Disposals != 0)
            {
                throw new InvalidOperationException("Dispose was called");
            }

            return this;
        }

        public static object StaticMethod()
        {
            return "42";
        }
    }

    private class AsyncDisposableToolType : IAsyncDisposable
    {
        public int AsyncDisposals { get; private set; }

        public ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            return default;
        }

        public object InstanceMethod()
        {
            if (AsyncDisposals != 0)
            {
                throw new InvalidOperationException("DisposeAsync was called");
            }

            return this;
        }
    }

    private class AsyncDisposableAndDisposableToolType : IAsyncDisposable, IDisposable
    {
        public AsyncDisposableAndDisposableToolType(MyService service)
        {
            Assert.NotNull(service);
        }

        [JsonPropertyOrder(0)]
        public int AsyncDisposals { get; private set; }

        [JsonPropertyOrder(1)]
        public int Disposals { get; private set; }

        public void Dispose()
        {
            Disposals++;
        }

        public ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            return default;
        }

        public object InstanceMethod()
        {
            if (Disposals != 0)
            {
                throw new InvalidOperationException("Dispose was called");
            }

            if (AsyncDisposals != 0)
            {
                throw new InvalidOperationException("DisposeAsync was called");
            }

            return this;
        }
    }

    private static void AssertMatchesJsonSchema(JsonElement schemaDoc, JsonNode? value)
    {
        JsonSchema schema = JsonSerializer.Deserialize(schemaDoc, JsonContext2.Default.JsonSchema)!;
        EvaluationOptions options = new() { OutputFormat = OutputFormat.List };
        EvaluationResults results = schema.Evaluate(value, options);
        if (!results.IsValid)
        {
            IEnumerable<string> errors = results.Details
                .Where(d => d.HasErrors)
                .SelectMany(d => d.Errors!.Select(error => $"Path:${d.InstanceLocation} {error.Key}:{error.Value}"));

            throw new XunitException($"""
                Instance JSON document does not match the specified schema.
                Schema:
                {JsonSerializer.Serialize(schema)}
                Instance:
                {value?.ToJsonString() ?? "null"}
                Errors:
                {string.Join(Environment.NewLine, errors)}
                """);
        }
    }

    record Person(string Name, int Age);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DisposableToolType))]
    [JsonSerializable(typeof(AsyncDisposableToolType))]
    [JsonSerializable(typeof(AsyncDisposableAndDisposableToolType))]
    [JsonSerializable(typeof(JsonSchema))]
    [JsonSerializable(typeof(List<AIContent>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(int?))]
    [JsonSerializable(typeof(DateTimeOffset?))]
    partial class JsonContext2 : JsonSerializerContext;
}
