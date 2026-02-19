using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Configuration;

public partial class McpServerBuilderExtensionsToolsTests : ClientServerTestBase
{
    public McpServerBuilderExtensionsToolsTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder
            .WithListToolsHandler(async (request, cancellationToken) =>
            {
                var cursor = request.Params?.Cursor;
                switch (cursor)
                {
                    case null:
                        return new()
                        {
                            NextCursor = "abc",
                            Tools = [new()
                                {
                                    Name = "FirstCustomTool",
                                    Description = "First tool returned by custom handler",
                                    InputSchema = JsonElement.Parse("""
                                        {
                                          "type": "object",
                                          "properties": {},
                                          "required": []
                                        }
                                        """),
                                }],
                        };

                    case "abc":
                        return new()
                        {
                            NextCursor = "def",
                            Tools = [new()
                                {
                                    Name = "SecondCustomTool",
                                    Description = "Second tool returned by custom handler",
                                    InputSchema = JsonElement.Parse("""
                                        {
                                          "type": "object",
                                          "properties": {},
                                          "required": []
                                        }
                                        """),
                                }],
                        };

                    case "def":
                        return new()
                        {
                            NextCursor = null,
                            Tools = [new()
                                {
                                    Name = "FinalCustomTool",
                                    Description = "Third tool returned by custom handler",
                                    InputSchema = JsonElement.Parse("""
                                        {
                                          "type": "object",
                                          "properties": {},
                                          "required": []
                                        }
                                        """),
                                }],
                        };

                    default:
                        throw new McpProtocolException($"Unexpected cursor: '{cursor}'", McpErrorCode.InvalidParams);
                }
            })
            .WithCallToolHandler(async (request, cancellationToken) =>
            {
                switch (request.Params?.Name)
                {
                    case "FirstCustomTool":
                    case "SecondCustomTool":
                    case "FinalCustomTool":
                        return new CallToolResult
                        {
                            Content = [new TextContentBlock { Text = $"{request.Params.Name}Result" }],
                        };

                    default:
                        throw new McpProtocolException($"Unknown tool: '{request.Params?.Name}'", McpErrorCode.InvalidParams);
                }
            })
            .WithTools<EchoTool>(serializerOptions: BuilderToolsJsonContext.Default.Options);

        services.AddSingleton(new ObjectWithId());
    }

    [Fact]
    public void Adds_Tools_To_Server()
    {
        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var tools = serverOptions.ToolCollection;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);
    }

    [Fact]
    public async Task Can_List_Registered_Tools()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(19, tools.Count);

        McpClientTool echoTool = tools.First(t => t.Name == "echo");
        Assert.Equal("Echoes the input back to the client.", echoTool.Description);
        Assert.Equal("object", echoTool.JsonSchema.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, echoTool.JsonSchema.GetProperty("properties").GetProperty("message").ValueKind);
        Assert.Equal("the echoes message", echoTool.JsonSchema.GetProperty("properties").GetProperty("message").GetProperty("description").GetString());
        Assert.Equal(1, echoTool.JsonSchema.GetProperty("required").GetArrayLength());

        McpClientTool doubleEchoTool = tools.First(t => t.Name == "double_echo");
        Assert.Equal("double_echo", doubleEchoTool.Name);
        Assert.Equal("Echoes the input back to the client.", doubleEchoTool.Description);
    }

    [Fact]
    public async Task Can_Create_Multiple_Servers_From_Options_And_List_Registered_Tools()
    {
        var options = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var loggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();

        for (int i = 0; i < 2; i++)
        {
            var stdinPipe = new Pipe();
            var stdoutPipe = new Pipe();

            await using var transport = new StreamServerTransport(stdinPipe.Reader.AsStream(), stdoutPipe.Writer.AsStream());
            await using var server = McpServer.Create(transport, options, loggerFactory, ServiceProvider);
            var serverRunTask = server.RunAsync(TestContext.Current.CancellationToken);

            await using (var client = await McpClient.CreateAsync(
                 new StreamClientTransport(
                    serverInput: stdinPipe.Writer.AsStream(),
                    serverOutput: stdoutPipe.Reader.AsStream(),
                    LoggerFactory),
                loggerFactory: LoggerFactory,
                cancellationToken: TestContext.Current.CancellationToken))
            {
                var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
                Assert.Equal(19, tools.Count);

                McpClientTool echoTool = tools.First(t => t.Name == "echo");
                Assert.Equal("Echoes the input back to the client.", echoTool.Description);
                Assert.Equal("object", echoTool.JsonSchema.GetProperty("type").GetString());
                Assert.Equal(JsonValueKind.Object, echoTool.JsonSchema.GetProperty("properties").GetProperty("message").ValueKind);
                Assert.Equal("the echoes message", echoTool.JsonSchema.GetProperty("properties").GetProperty("message").GetProperty("description").GetString());
                Assert.Equal(1, echoTool.JsonSchema.GetProperty("required").GetArrayLength());

                McpClientTool doubleEchoTool = tools.First(t => t.Name == "double_echo");
                Assert.Equal("double_echo", doubleEchoTool.Name);
                Assert.Equal("Echoes the input back to the client.", doubleEchoTool.Description);
            }

            stdinPipe.Writer.Complete();
            await serverRunTask;
            stdoutPipe.Writer.Complete();
        }
    }

    [Fact]
    public async Task Can_Be_Notified_Of_Tool_Changes()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(19, tools.Count);

        Channel<JsonRpcNotification> listChanged = Channel.CreateUnbounded<JsonRpcNotification>();
        var notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(notificationRead.IsCompleted);

        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var serverTools = serverOptions.ToolCollection;
        Assert.NotNull(serverTools);

        var newTool = McpServerTool.Create([McpServerTool(Name = "NewTool")] () => "42");
        await using (client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification, (notification, cancellationToken) =>
            {
                listChanged.Writer.TryWrite(notification);
                return default;
            }))
        {
            serverTools.Add(newTool);
            await notificationRead;

            tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(20, tools.Count);
            Assert.Contains(tools, t => t.Name == "NewTool");

            notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
            Assert.False(notificationRead.IsCompleted);
            serverTools.Remove(newTool);
            await notificationRead;
        }

        tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(19, tools.Count);
        Assert.DoesNotContain(tools, t => t.Name == "NewTool");
    }

    [Fact]
    public async Task Can_Call_Registered_Tool()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object?>() { ["message"] = "Peter" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        var tc = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("hello Peter", tc.Text);
        Assert.Equal("text", tc.Type);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Array_Result()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "echo_array",
            new Dictionary<string, object?>() { ["message"] = "Peter" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.Equal("""["hello Peter","hello2 Peter"]""", (result.Content[0] as TextContentBlock)?.Text);

        result = await client.CallToolAsync(
            "SecondCustomTool",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.Equal("SecondCustomToolResult", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Null_Result()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "return_null",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.Empty(result.Content);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Json_Result()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "return_json",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        Assert.Equal("""{"SomeProp":false}""", Regex.Replace((result.Content[0] as TextContentBlock)?.Text ?? string.Empty, "\\s+", ""));
        Assert.Equal("text", (result.Content[0] as TextContentBlock)?.Type);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Int_Result()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "return_integer",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        Assert.Equal("5", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_And_Pass_ComplexType()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "echo_complex",
            new Dictionary<string, object?>() { ["complex"] = JsonDocument.Parse("""{"Name": "Peter", "Age": 25}""").RootElement },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        Assert.Equal("Peter", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Instance_Method()
    {
        await using McpClient client = await CreateMcpClientForServer();

        string[][] parts = new string[2][];
        for (int i = 0; i < 2; i++)
        {
            var result = await client.CallToolAsync(
                "get_ctor_parameter",
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.NotNull(result.Content);
            Assert.NotEmpty(result.Content);

            parts[i] = (result.Content[0] as TextContentBlock)?.Text?.Split(':') ?? [];
            Assert.Equal(2, parts[i].Length);
        }

        string random1 = parts[0][0];
        string random2 = parts[1][0];
        Assert.NotEqual(random1, random2);

        string id1 = parts[0][1];
        string id2 = parts[1][1];
        Assert.Equal(id1, id2);
    }

    [Fact]
    public async Task Returns_IsError_Content_And_Logs_Error_When_Tool_Fails()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "throw_exception",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.Contains("An error occurred", (result.Content[0] as TextContentBlock)?.Text);

        var errorLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.LogLevel == LogLevel.Error);
        Assert.Equal($"\"throw_exception\" threw an unhandled exception.", errorLog.Message);
        Assert.IsType<InvalidOperationException>(errorLog.Exception);
        Assert.Equal("Test error", errorLog.Exception.Message);
    }

    [Fact]
    public async Task Logs_Tool_Name_On_Successful_Call()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is not true);
        Assert.Equal("hello test", (result.Content[0] as TextContentBlock)?.Text);

        var infoLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "\"echo\" completed. IsError = False.");
        Assert.Equal(LogLevel.Information, infoLog.LogLevel);
    }

    [Fact]
    public async Task Logs_Tool_Name_With_IsError_When_Tool_Returns_Error()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "return_is_error",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("Tool returned an error", (result.Content[0] as TextContentBlock)?.Text);

        var infoLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "\"return_is_error\" completed. IsError = True.");
        Assert.Equal(LogLevel.Information, infoLog.LogLevel);
    }

    [Fact]
    public async Task Logs_Tool_Error_When_Tool_Throws_OperationCanceledException()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "throw_operation_canceled_exception",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.Contains("An error occurred", (result.Content[0] as TextContentBlock)?.Text);

        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Error &&
            m.Message == "\"throw_operation_canceled_exception\" threw an unhandled exception." &&
            m.Exception is OperationCanceledException);
    }

    [Fact]
    public async Task Logs_Tool_Error_When_Tool_Throws_McpProtocolException()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.CallToolAsync(
            "throw_mcp_protocol_exception",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Error &&
            m.Message == "\"throw_mcp_protocol_exception\" threw an unhandled exception." &&
            m.Exception is McpProtocolException);

        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning &&
            m.Message.Contains("request handler failed"));
    }

    [Fact]
    public async Task Throws_Exception_On_Unknown_Tool()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var e = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.CallToolAsync(
            "NotRegisteredTool",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("'NotRegisteredTool'", e.Message);
    }

    [Fact]
    public async Task Returns_IsError_Missing_Parameter()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "echo",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
    }

    [Fact]
    public void WithTools_InvalidArgs_Throws()
    {
        IMcpServerBuilder builder = new ServiceCollection().AddMcpServer();

        Assert.Throws<ArgumentNullException>("tools", () => builder.WithTools((IEnumerable<McpServerTool>)null!));
        Assert.Throws<ArgumentNullException>("toolTypes", () => builder.WithTools((IEnumerable<Type>)null!));
        Assert.Throws<ArgumentNullException>("target", () => builder.WithTools<object>(target: null!));

        IMcpServerBuilder nullBuilder = null!;
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithTools<object>());
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithTools(new object()));
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithTools(Array.Empty<Type>()));
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithToolsFromAssembly());
    }

    [Fact]
    public void Empty_Enumerables_Is_Allowed()
    {
        IMcpServerBuilder builder = new ServiceCollection().AddMcpServer();

        builder.WithTools(tools: []); // no exception
        builder.WithTools(toolTypes: []); // no exception
        builder.WithTools<object>(); // no exception even though no tools exposed
        builder.WithToolsFromAssembly(typeof(AIFunction).Assembly); // no exception even though no tools exposed
    }

    [Fact]
    public void Register_Tools_From_Current_Assembly()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        ServiceCollection sc = new();
        sc.AddMcpServer().WithToolsFromAssembly();
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "echo");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithTools_Parameters_Satisfiable_From_DI(bool parameterInServices)
    {
        ServiceCollection sc = new();
        if (parameterInServices)
        {
            sc.AddSingleton(new ComplexObject());
        }
        sc.AddMcpServer().WithTools([typeof(EchoTool)], BuilderToolsJsonContext.Default.Options);
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = services.GetServices<McpServerTool>().First(t => t.ProtocolTool.Name == "echo_complex");
        if (parameterInServices)
        {
            Assert.DoesNotContain("\"complex\"", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema, AIJsonUtilities.DefaultOptions));
        }
        else
        {
            Assert.Contains("\"complex\"", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema, AIJsonUtilities.DefaultOptions));
        }
    }


    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    [InlineData(null)]
    public void WithToolsFromAssembly_Parameters_Satisfiable_From_DI(ServiceLifetime? lifetime)
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        ServiceCollection sc = new();
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                sc.AddSingleton(new ComplexObject());
                break;

            case ServiceLifetime.Scoped:
                sc.AddScoped(_ => new ComplexObject());
                break;

            case ServiceLifetime.Transient:
                sc.AddTransient(_ => new ComplexObject());
                break;
        }

        sc.AddMcpServer().WithToolsFromAssembly();
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = services.GetServices<McpServerTool>().First(t => t.ProtocolTool.Name == "echo_complex");
        if (lifetime is not null)
        {
            Assert.DoesNotContain("\"complex\"", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema, AIJsonUtilities.DefaultOptions));
        }
        else
        {
            Assert.Contains("\"complex\"", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema, AIJsonUtilities.DefaultOptions));
        }
    }

    [Fact]
    public async Task WithTools_TargetInstance_UsesTarget()
    {
        ServiceCollection sc = new();

        var target = new EchoTool(new ObjectWithId());
        sc.AddMcpServer().WithTools(target, BuilderToolsJsonContext.Default.Options);

        McpServerTool tool = sc.BuildServiceProvider().GetServices<McpServerTool>().First(t => t.ProtocolTool.Name == "get_ctor_parameter");
        var result = await tool.InvokeAsync(new RequestContext<CallToolRequestParams>(new Mock<McpServer>().Object, new JsonRpcRequest { Method = "test", Id = new RequestId("1") }), TestContext.Current.CancellationToken);

        Assert.Equal(target.GetCtorParameter(), (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task WithTools_TargetInstance_UsesEnumerableImplementation()
    {
        ServiceCollection sc = new();

        sc.AddMcpServer().WithTools(new MyToolProvider());

        var tools = sc.BuildServiceProvider().GetServices<McpServerTool>().ToArray();
        Assert.Equal(2, tools.Length);
        Assert.Contains(tools, t => t.ProtocolTool.Name == "Returns42");
        Assert.Contains(tools, t => t.ProtocolTool.Name == "Returns43");
    }

    private sealed class MyToolProvider : IEnumerable<McpServerTool>
    {
        public IEnumerator<McpServerTool> GetEnumerator()
        {
            yield return McpServerTool.Create(() => "42", new() { Name = "Returns42" });
            yield return McpServerTool.Create(() => "43", new() { Name = "Returns43" });
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task Recognizes_Parameter_Types()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(tools);
        Assert.NotEmpty(tools);

        var tool = tools.First(t => t.Name == "test_tool");
        Assert.Empty(tool.Description!);
        Assert.Equal("object", tool.JsonSchema.GetProperty("type").GetString());

        Assert.Contains("integer", tool.JsonSchema.GetProperty("properties").GetProperty("number").GetProperty("type").GetString());
        Assert.Contains("number", tool.JsonSchema.GetProperty("properties").GetProperty("otherNumber").GetProperty("type").GetString());
        Assert.Contains("boolean", tool.JsonSchema.GetProperty("properties").GetProperty("someCheck").GetProperty("type").GetString());
        Assert.Contains("string", tool.JsonSchema.GetProperty("properties").GetProperty("someDate").GetProperty("type").GetString());
        Assert.Contains("string", tool.JsonSchema.GetProperty("properties").GetProperty("someOtherDate").GetProperty("type").GetString());
        Assert.Contains("array", tool.JsonSchema.GetProperty("properties").GetProperty("data").GetProperty("type").GetString());
        Assert.Contains("object", tool.JsonSchema.GetProperty("properties").GetProperty("complexObject").GetProperty("type").GetString());
    }

    [Fact]
    public void Register_Tools_From_Multiple_Sources()
    {
        ServiceCollection sc = new();
        sc.AddMcpServer()
            .WithTools<EchoTool>(serializerOptions: BuilderToolsJsonContext.Default.Options)
            .WithTools<AnotherToolType>(serializerOptions: BuilderToolsJsonContext.Default.Options)
            .WithTools([typeof(ToolTypeWithNoAttribute)], BuilderToolsJsonContext.Default.Options)
            .WithTools([McpServerTool.Create(() => "42", new() { Name = "Returns42" })]);
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "double_echo");
        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "DifferentName");
        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "method_b");
        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "method_c");
        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "method_d");
        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "Returns42");
    }

    [Fact]
    public void Create_ExtractsToolAnnotations_AllSet()
    {
        var tool = McpServerTool.Create(EchoTool.ReturnInteger);
        Assert.NotNull(tool);
        Assert.NotNull(tool.ProtocolTool);

        var annotations = tool.ProtocolTool.Annotations;
        Assert.NotNull(annotations);
        Assert.Equal("Return An Integer", annotations.Title);
        Assert.Equal("Return An Integer", tool.ProtocolTool.Title);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
        Assert.True(annotations.ReadOnlyHint);
    }

    [Fact]
    public void Create_ExtractsToolAnnotations_SomeSet()
    {
        var tool = McpServerTool.Create(EchoTool.ReturnJson);
        Assert.NotNull(tool);
        Assert.NotNull(tool.ProtocolTool);

        var annotations = tool.ProtocolTool.Annotations;
        Assert.NotNull(annotations);
        Assert.Null(annotations.Title);
        Assert.Null(annotations.DestructiveHint);
        Assert.False(annotations.IdempotentHint);
        Assert.Null(annotations.OpenWorldHint);
        Assert.Null(annotations.ReadOnlyHint);
    }

    [Fact]
    public async Task AttributeProperties_Propagated()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);

        McpClientTool tool = tools.First(t => t.Name == "echo_complex");

        Assert.Equal("This is a title", tool.Title);
        Assert.Equal("This is a title", tool.ProtocolTool.Title);
        Assert.Equal("This is a title", tool.ProtocolTool.Annotations?.Title);

        Assert.NotNull(tool.ProtocolTool.Icons);
        Assert.NotEmpty(tool.ProtocolTool.Icons);
        var icon = Assert.Single(tool.ProtocolTool.Icons);
        Assert.Equal("https://example.com/tool-icon.svg", icon.Source);
        Assert.Null(icon.Theme);
    }

    [Fact]
    public async Task HandlesIProgressParameter()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);

        McpClientTool progressTool = tools.First(t => t.Name == "sends_progress_notifications");

        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int remainingNotifications = 10;

        ConcurrentQueue<ProgressNotificationParams> notifications = new();
        await using (client.RegisterNotificationHandler(NotificationMethods.ProgressNotification, (notification, cancellationToken) =>
        {
            if (JsonSerializer.Deserialize<ProgressNotificationParams>(notification.Params, McpJsonUtilities.DefaultOptions) is { } pn &&
                pn.ProgressToken == new ProgressToken("abc123"))
            {
                notifications.Enqueue(pn);
                if (Interlocked.Decrement(ref remainingNotifications) == 0)
                {
                    tcs.SetResult(true);
                }
            }

            return default;
        }))
        {
            var result = await client.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = progressTool.ProtocolTool.Name,
                    // Set the progress token in Meta
                    Meta = new JsonObject
                    {
                        ["progressToken"] = "abc123",
                    }
                },
                cancellationToken: TestContext.Current.CancellationToken);

            await tcs.Task;
            Assert.Contains("done", JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions));
        }

        ProgressNotificationParams[] array = notifications.OrderBy(n => n.Progress.Progress).ToArray();
        Assert.Equal(10, array.Length);
        for (int i = 0; i < array.Length; i++)
        {
            Assert.Equal("abc123", array[i].ProgressToken.ToString());
            Assert.Equal(i, array[i].Progress.Progress);
            Assert.Equal(10, array[i].Progress.Total);
            Assert.Equal($"Progress {i}", array[i].Progress.Message);
        }
    }

    [Fact]
    public async Task CancellationNotificationsPropagateToToolTokens()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);
        McpClientTool cancelableTool = tools.First(t => t.Name == "infinite_cancelable_operation");

        var requestId = new RequestId(Guid.NewGuid().ToString());
        var invokeTask = client.SendRequestAsync<CallToolRequestParams, CallToolResult>(
            RequestMethods.ToolsCall,
            new CallToolRequestParams { Name = cancelableTool.ProtocolTool.Name },
            requestId: requestId,
            cancellationToken: TestContext.Current.CancellationToken);

        await client.SendNotificationAsync(
            NotificationMethods.CancelledNotification,
            parameters: new CancelledNotificationParams
            {
                RequestId = requestId,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await invokeTask);
    }

    [McpServerToolType]
    public sealed class EchoTool(ObjectWithId objectFromDI)
    {
        private readonly string _randomValue = Guid.NewGuid().ToString("N");

        [McpServerTool, Description("Echoes the input back to the client.")]
        public static string Echo([Description("the echoes message")] string message)
        {
            return "hello " + message;
        }

        [McpServerTool(Name = "double_echo"), Description("Echoes the input back to the client.")]
        public static string Echo2(string message)
        {
            return "hello hello" + message;
        }

        [McpServerTool]
        public static string TestTool(int number, double otherNumber, bool someCheck, DateTime someDate, DateTimeOffset someOtherDate, string[] data, ComplexObject complexObject)
        {
            return "hello hello";
        }

        [McpServerTool]
        public static string[] EchoArray(string message)
        {
            return ["hello " + message, "hello2 " + message];
        }

        [McpServerTool]
        public static string? ReturnNull()
        {
            return null;
        }

        [McpServerTool(Idempotent = false)]
        public static JsonElement ReturnJson()
        {
            return JsonDocument.Parse("{\"SomeProp\": false}").RootElement;
        }

        [McpServerTool(Title = "Return An Integer", Destructive = false, Idempotent = true, OpenWorld = false, ReadOnly = true)]
        public static int ReturnInteger()
        {
            return 5;
        }

        [McpServerTool]
        public static string ThrowException()
        {
            throw new InvalidOperationException("Test error");
        }

        [McpServerTool]
        public static string ThrowOperationCanceledException()
        {
            throw new OperationCanceledException("Tool was canceled");
        }

        [McpServerTool]
        public static string ThrowMcpProtocolException()
        {
            throw new McpProtocolException("Tool protocol error", McpErrorCode.InvalidParams);
        }

        [McpServerTool]
        public static CallToolResult ReturnIsError()
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = "Tool returned an error" }],
            };
        }

        [McpServerTool]
        public static int ReturnCancellationToken(CancellationToken cancellationToken)
        {
            return cancellationToken.GetHashCode();
        }

        [McpServerTool(Title = "This is a title", IconSource = "https://example.com/tool-icon.svg")]
        public static string EchoComplex(ComplexObject complex)
        {
            return complex.Name!;
        }

        [McpServerTool]
        public static async Task<string> InfiniteCancelableOperation(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (Exception)
            {
                return "canceled";
            }

            return "unreachable";
        }

        [McpServerTool]
        public string GetCtorParameter() => $"{_randomValue}:{objectFromDI.Id}";

        [McpServerTool]
        public string SendsProgressNotifications(IProgress<ProgressNotificationValue> progress)
        {
            for (int i = 0; i < 10; i++)
            {
                progress.Report(new() { Progress = i, Total = 10, Message = $"Progress {i}" });
            }

            return "done";
        }
    }

    [McpServerToolType]
    internal class AnotherToolType
    {
        [McpServerTool(Name = "DifferentName")]
        private static string MethodA(int a) => a.ToString();

        [McpServerTool]
        internal static string MethodB(string b) => b.ToString();

        [McpServerTool]
        protected static string MethodC(long c) => c.ToString();
    }

    internal class ToolTypeWithNoAttribute
    {
        [McpServerTool]
        public static string MethodD(string d) => d.ToString();
    }

    public class ObjectWithId
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
    }

    public class ComplexObject
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(ComplexObject))]
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(CallToolResult))]
    partial class BuilderToolsJsonContext : JsonSerializerContext;
}