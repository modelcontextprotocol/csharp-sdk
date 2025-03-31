using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Transport;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsToolsTests : LoggedTest, IAsyncDisposable
{
    private readonly Pipe _clientToServerPipe = new();
    private readonly Pipe _serverToClientPipe = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly IMcpServerBuilder _builder;
    private readonly CancellationTokenSource _cts;
    private readonly Task _serverTask;

    public McpServerBuilderExtensionsToolsTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        ServiceCollection sc = new();
        sc.AddSingleton(LoggerFactory);
        _builder = sc.AddMcpServer().WithStdioServerTransport().WithTools<EchoTool>();
        // Call WithStdioServerTransport to get the IMcpServer registration, then overwrite default transport with a pipe transport.
        sc.AddSingleton<ITransport>(new StdioServerTransport("TestServer", _clientToServerPipe.Reader.AsStream(), _serverToClientPipe.Writer.AsStream(), LoggerFactory));
        sc.AddSingleton(new ObjectWithId());
        _serviceProvider = sc.BuildServiceProvider();

        var server = _serviceProvider.GetRequiredService<IMcpServer>();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _serverTask = server.RunAsync(cancellationToken: _cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        _clientToServerPipe.Writer.Complete();
        _serverToClientPipe.Writer.Complete();

        await _serverTask;

        await _serviceProvider.DisposeAsync();
        _cts.Dispose();
        Dispose();
    }

    private async Task<IMcpClient> CreateMcpClientForServer()
    {
        var serverStdinWriter = new StreamWriter(_clientToServerPipe.Writer.AsStream());
        var serverStdoutReader = new StreamReader(_serverToClientPipe.Reader.AsStream());

        var serverConfig = new McpServerConfig()
        {
            Id = "TestServer",
            Name = "TestServer",
            TransportType = "ignored",
        };

        return await McpClientFactory.CreateAsync(
            serverConfig,
            createTransportFunc: (_, _) => new StreamClientTransport(serverStdinWriter, serverStdoutReader, LoggerFactory),
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Adds_Tools_To_Server()
    {
        var serverOptions = _serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var tools = serverOptions.Capabilities?.Tools?.ToolCollection;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);
    }

    [Fact]
    public async Task Can_List_Registered_Tools()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(13, tools.Count);

        McpClientTool echoTool = tools.First(t => t.Name == "Echo");
        Assert.Equal("Echo", echoTool.Name);
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
        var options = _serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();

        for (int i = 0; i < 2; i++)
        {
            var stdinPipe = new Pipe();
            var stdoutPipe = new Pipe();

            await using var transport = new StdioServerTransport($"TestServer_{i}", stdinPipe.Reader.AsStream(), stdoutPipe.Writer.AsStream());
            await using var server = McpServerFactory.Create(transport, options, loggerFactory, _serviceProvider);
            var serverRunTask = server.RunAsync(TestContext.Current.CancellationToken);

            using var serverStdinWriter = new StreamWriter(stdinPipe.Writer.AsStream());
            using var serverStdoutReader = new StreamReader(stdoutPipe.Reader.AsStream());

            var serverConfig = new McpServerConfig()
            {
                Id = $"TestServer_{i}",
                Name = $"TestServer_{i}",
                TransportType = "ignored",
            };

            await using (var client = await McpClientFactory.CreateAsync(
                serverConfig,
                createTransportFunc: (_, _) => new StreamClientTransport(serverStdinWriter, serverStdoutReader, LoggerFactory),
                loggerFactory: LoggerFactory,
                cancellationToken: TestContext.Current.CancellationToken))
            {
                var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
                Assert.Equal(13, tools.Count);

                McpClientTool echoTool = tools.First(t => t.Name == "Echo");
                Assert.Equal("Echo", echoTool.Name);
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
        IMcpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(13, tools.Count);

        Channel<JsonRpcNotification> listChanged = Channel.CreateUnbounded<JsonRpcNotification>();
        client.AddNotificationHandler(NotificationMethods.ToolListChangedNotification, notification =>
        {
            listChanged.Writer.TryWrite(notification);
            return Task.CompletedTask;
        });

        var notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(notificationRead.IsCompleted);

        var serverOptions = _serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var serverTools = serverOptions.Capabilities?.Tools?.ToolCollection;
        Assert.NotNull(serverTools);

        var newTool = McpServerTool.Create([McpServerTool(Name = "NewTool")] () => "42");
        serverTools.Add(newTool);
        await notificationRead;

        tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(14, tools.Count);
        Assert.Contains(tools, t => t.Name == "NewTool");

        notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(notificationRead.IsCompleted);
        serverTools.Remove(newTool);
        await notificationRead;

        tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(13, tools.Count);
        Assert.DoesNotContain(tools, t => t.Name == "NewTool");
    }

    [Fact]
    public async Task Can_Call_Registered_Tool()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "Echo",
            new Dictionary<string, object?>() { ["message"] = "Peter" }, 
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        Assert.Equal("hello Peter", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Array_Result()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "EchoArray",
            new Dictionary<string, object?>() { ["message"] = "Peter" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        Assert.Equal("hello Peter", result.Content[0].Text);
        Assert.Equal("hello2 Peter", result.Content[1].Text);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Null_Result()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "ReturnNull",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.Empty(result.Content);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Json_Result()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "ReturnJson",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        Assert.Equal("""{"SomeProp":false}""", Regex.Replace(result.Content[0].Text ?? string.Empty, "\\s+", ""));
        Assert.Equal("text", result.Content[0].Type);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Int_Result()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "ReturnInteger",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        Assert.Equal("5", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_And_Pass_ComplexType()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "EchoComplex",
            new Dictionary<string, object?>() { ["complex"] = JsonDocument.Parse("""{"Name": "Peter", "Age": 25}""").RootElement },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        Assert.Equal("Peter", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);
    }

    [Fact]
    public async Task Can_Call_Registered_Tool_With_Instance_Method()
    {
        IMcpClient client = await CreateMcpClientForServer();

        string[][] parts = new string[2][];
        for (int i = 0; i < 2; i++)
        {
            var result = await client.CallToolAsync(
                nameof(EchoTool.GetCtorParameter),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.NotNull(result.Content);
            Assert.NotEmpty(result.Content);

            parts[i] = result.Content[0].Text?.Split(':') ?? [];
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
    public async Task Returns_IsError_Content_When_Tool_Fails()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "ThrowException",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.Contains("Test error", result.Content[0].Text);
    }

    [Fact]
    public async Task Throws_Exception_On_Unknown_Tool()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var e = await Assert.ThrowsAsync<McpClientException>(async () => await client.CallToolAsync(
            "NotRegisteredTool",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("'NotRegisteredTool'", e.Message);
    }

    [Fact]
    public async Task Returns_IsError_Missing_Parameter()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "Echo",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
    }

    [Fact]
    public void WithTools_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("toolTypes", () => _builder.WithTools((IEnumerable<Type>)null!));

        IMcpServerBuilder nullBuilder = null!;
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithTools<object>());
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithTools(Array.Empty<Type>()));
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithToolsFromAssembly());
    }

    [Fact]
    public void Empty_Enumerables_Is_Allowed()
    {
        _builder.WithTools(toolTypes: []); // no exception
        _builder.WithTools<object>(); // no exception even though no tools exposed
        _builder.WithToolsFromAssembly(typeof(AIFunction).Assembly); // no exception even though no tools exposed
    }

    [Fact]
    public void Register_Tools_From_Current_Assembly()
    {
        ServiceCollection sc = new();
        sc.AddMcpServer().WithToolsFromAssembly();
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "Echo");
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
        sc.AddMcpServer().WithTools(typeof(EchoTool));
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = services.GetServices<McpServerTool>().First(t => t.ProtocolTool.Name == "EchoComplex");
        if (parameterInServices)
        {
            Assert.DoesNotContain("\"complex\"", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));
        }
        else
        {
            Assert.Contains("\"complex\"", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithToolsFromAssembly_Parameters_Satisfiable_From_DI(bool parameterInServices)
    {
        ServiceCollection sc = new();
        if (parameterInServices)
        {
            sc.AddSingleton(new ComplexObject());
        }
        sc.AddMcpServer().WithToolsFromAssembly();
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = services.GetServices<McpServerTool>().First(t => t.ProtocolTool.Name == "EchoComplex");
        if (parameterInServices)
        {
            Assert.DoesNotContain("\"complex\"", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));
        }
        else
        {
            Assert.Contains("\"complex\"", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));
        }
    }

    [Fact]
    public async Task Recognizes_Parameter_Types()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(tools);
        Assert.NotEmpty(tools);

        var tool = tools.First(t => t.Name == "TestTool");
        Assert.Equal("TestTool", tool.Name);
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
            .WithTools<EchoTool>()
            .WithTools<AnotherToolType>()
            .WithTools(typeof(ToolTypeWithNoAttribute));
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "double_echo");
        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "DifferentName");
        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "MethodB");
        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "MethodC");
        Assert.Contains(services.GetServices<McpServerTool>(), t => t.ProtocolTool.Name == "MethodD");
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
    public async Task HandlesIProgressParameter()
    {
        ConcurrentQueue<ProgressNotification> notifications = new();

        IMcpClient client = await CreateMcpClientForServer();
        client.AddNotificationHandler(NotificationMethods.ProgressNotification, notification =>
        {
            ProgressNotification pn = JsonSerializer.Deserialize<ProgressNotification>((JsonElement)notification.Params!)!;
            notifications.Enqueue(pn);
            return Task.CompletedTask;
        });

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);

        McpClientTool progressTool = tools.First(t => t.Name == nameof(EchoTool.SendsProgressNotifications));

        var result = await client.SendRequestAsync<CallToolResponse>(new JsonRpcRequest()
        {
            Method = RequestMethods.ToolsCall,
            Params = new CallToolRequestParams()
            {
                Name = progressTool.ProtocolTool.Name,
                Meta = new() { ProgressToken = new("abc123") },
            },
        }, TestContext.Current.CancellationToken);

        Assert.Contains("done", JsonSerializer.Serialize(result));
        SpinWait.SpinUntil(() => notifications.Count == 10, TimeSpan.FromSeconds(10));

        ProgressNotification[] array = notifications.OrderBy(n => n.Progress.Progress).ToArray();
        Assert.Equal(10, array.Length);
        for (int i = 0; i < array.Length; i++)
        {
            Assert.Equal("\"abc123\"", array[i].ProgressToken.ToString());
            Assert.Equal(i, array[i].Progress.Progress);
            Assert.Equal(10, array[i].Progress.Total);
            Assert.Equal($"Progress {i}", array[i].Progress.Message);
        }
    }

    [Fact]
    public async Task CancellationNotificationsPropagateToToolTokens()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);
        McpClientTool cancelableTool = tools.First(t => t.Name == nameof(EchoTool.InfiniteCancelableOperation));

        var requestId = new RequestId(Guid.NewGuid().ToString());
        var invokeTask = client.SendRequestAsync<CallToolResponse>(new JsonRpcRequest()
        {
            Method = RequestMethods.ToolsCall,
            Id = requestId,
            Params = new CallToolRequestParams() { Name = cancelableTool.ProtocolTool.Name },
        }, TestContext.Current.CancellationToken);

        await client.SendNotificationAsync(
            NotificationMethods.CancelledNotification,
            parameters: new CancelledNotification()
            {
                RequestId = requestId,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invokeTask);
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
        public static int ReturnCancellationToken(CancellationToken cancellationToken)
        {
            return cancellationToken.GetHashCode();
        }

        [McpServerTool]
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

    public class ComplexObject
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    public class ObjectWithId
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
    }
}
