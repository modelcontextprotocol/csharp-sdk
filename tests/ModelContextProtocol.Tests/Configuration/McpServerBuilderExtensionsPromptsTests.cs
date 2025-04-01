using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Threading.Channels;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsPromptsTests : LoggedTest, IAsyncDisposable
{
    private readonly Pipe _clientToServerPipe = new();
    private readonly Pipe _serverToClientPipe = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly IMcpServerBuilder _builder;
    private readonly CancellationTokenSource _cts;
    private readonly Task _serverTask;

    public McpServerBuilderExtensionsPromptsTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        ServiceCollection sc = new();
        sc.AddSingleton(LoggerFactory);
        _builder = sc
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithListPromptsHandler(async (request, cancellationToken) =>
            {
                var cursor = request.Params?.Cursor;
                switch (cursor)
                {
                    case null:
                        return new()
                        {
                            NextCursor = "abc",
                            Prompts = [new()
                            {
                                Name = "FirstCustomPrompt",
                                Description = "First prompt returned by custom handler",
                            }],
                        };

                    case "abc":
                        return new()
                        {
                            NextCursor = "def",
                            Prompts = [new()
                            {
                                Name = "SecondCustomPrompt",
                                Description = "Second prompt returned by custom handler",
                            }],
                        };

                    case "def":
                        return new()
                        {
                            NextCursor = null,
                            Prompts = [new()
                            {
                                Name = "FinalCustomPrompt",
                                Description = "Final prompt returned by custom handler",
                            }],
                        };

                    default:
                        throw new Exception("Unexpected cursor");
                }
            })
            .WithGetPromptHandler(async (request, cancellationToken) =>
            {
                switch (request.Params?.Name)
                {
                    case "FirstCustomPrompt":
                    case "SecondCustomPrompt":
                    case "FinalCustomPrompt":
                        return new GetPromptResult()
                        {
                            Messages = [new() { Role = Role.User, Content = new() { Text = $"hello from {request.Params.Name}", Type = "text" } }],
                        };

                    default:
                        throw new Exception($"Unknown prompt '{request.Params?.Name}'");
                }
            })
            .WithPrompts<SimplePrompts>();


        // Call WithStdioServerTransport to get the IMcpServer registration, then overwrite default transport with a pipe transport.
        sc.AddSingleton<ITransport>(new StreamServerTransport(_clientToServerPipe.Reader.AsStream(), _serverToClientPipe.Writer.AsStream(), loggerFactory: LoggerFactory));
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
        return await McpClientFactory.CreateAsync(
            new McpServerConfig()
            {
                Id = "TestServer",
                Name = "TestServer",
                TransportType = "ignored",
            },
            createTransportFunc: (_, _) => new StreamClientTransport(
                serverInput: _clientToServerPipe.Writer.AsStream(),
                serverOutput: _serverToClientPipe.Reader.AsStream(),
                LoggerFactory),
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Adds_Prompts_To_Server()
    {
        var serverOptions = _serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var prompts = serverOptions?.Capabilities?.Prompts?.PromptCollection;
        Assert.NotNull(prompts);
        Assert.NotEmpty(prompts);
    }

    [Fact]
    public async Task Can_List_And_Call_Registered_Prompts()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, prompts.Count);

        var prompt = prompts.First(t => t.Name == nameof(SimplePrompts.ReturnsChatMessages));
        Assert.Equal("Returns chat messages", prompt.Description);

        var result = await prompt.GetAsync(new Dictionary<string, object?>() { ["message"] = "hello" }, TestContext.Current.CancellationToken);
        var chatMessages = result.ToChatMessages();

        Assert.NotNull(chatMessages);
        Assert.NotEmpty(chatMessages);
        Assert.Equal(2, chatMessages.Count);
        Assert.Equal("The prompt is: hello", chatMessages[0].Text);
        Assert.Equal("Summarize.", chatMessages[1].Text);

        prompt = prompts.First(t => t.Name == "SecondCustomPrompt");
        Assert.Equal("Second prompt returned by custom handler", prompt.Description);
        result = await prompt.GetAsync(cancellationToken: TestContext.Current.CancellationToken);
        chatMessages = result.ToChatMessages();
        Assert.NotNull(chatMessages);
        Assert.Single(chatMessages);
        Assert.Equal("hello from SecondCustomPrompt", chatMessages[0].Text);
    }

    [Fact]
    public async Task Can_Be_Notified_Of_Prompt_Changes()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, prompts.Count);

        Channel<JsonRpcNotification> listChanged = Channel.CreateUnbounded<JsonRpcNotification>();
        client.AddNotificationHandler("notifications/prompts/list_changed", notification =>
        {
            listChanged.Writer.TryWrite(notification);
            return Task.CompletedTask;
        });

        var notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(notificationRead.IsCompleted);

        var serverOptions = _serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var serverPrompts = serverOptions.Capabilities?.Prompts?.PromptCollection;
        Assert.NotNull(serverPrompts);

        var newPrompt = McpServerPrompt.Create([McpServerPrompt(Name = "NewPrompt")] () => "42");
        serverPrompts.Add(newPrompt);
        await notificationRead;

        prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(7, prompts.Count);
        Assert.Contains(prompts, t => t.Name == "NewPrompt");

        notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(notificationRead.IsCompleted);
        serverPrompts.Remove(newPrompt);
        await notificationRead;

        prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, prompts.Count);
        Assert.DoesNotContain(prompts, t => t.Name == "NewPrompt");
    }

    [Fact]
    public async Task Throws_When_Prompt_Fails()
    {
        IMcpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<McpClientException>(async () => await client.GetPromptAsync(
            nameof(SimplePrompts.ThrowsException),
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_Exception_On_Unknown_Prompt()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var e = await Assert.ThrowsAsync<McpClientException>(async () => await client.GetPromptAsync(
            "NotRegisteredPrompt",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("'NotRegisteredPrompt'", e.Message);
    }

    [Fact]
    public async Task Throws_Exception_Missing_Parameter()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var e = await Assert.ThrowsAsync<McpClientException>(async () => await client.GetPromptAsync(
            nameof(SimplePrompts.ReturnsChatMessages),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Missing required parameter", e.Message);
    }

    [Fact]
    public void WithPrompts_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("promptTypes", () => _builder.WithPrompts((IEnumerable<Type>)null!));

        IMcpServerBuilder nullBuilder = null!;
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithPrompts<object>());
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithPrompts(Array.Empty<Type>()));
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithPromptsFromAssembly());
    }

    [Fact]
    public void Empty_Enumerables_Is_Allowed()
    {
        _builder.WithPrompts(promptTypes: []); // no exception
        _builder.WithPrompts<object>(); // no exception even though no prompts exposed
        _builder.WithPromptsFromAssembly(typeof(AIFunction).Assembly); // no exception even though no prompts exposed
    }

    [Fact]
    public void Register_Prompts_From_Current_Assembly()
    {
        ServiceCollection sc = new();
        sc.AddMcpServer().WithPromptsFromAssembly();
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == nameof(SimplePrompts.ReturnsChatMessages));
    }

    [Fact]
    public void Register_Prompts_From_Multiple_Sources()
    {
        ServiceCollection sc = new();
        sc.AddMcpServer()
            .WithPrompts<SimplePrompts>()
            .WithPrompts<MorePrompts>();
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == nameof(SimplePrompts.ReturnsChatMessages));
        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == nameof(SimplePrompts.ThrowsException));
        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == nameof(SimplePrompts.ReturnsString));
        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == nameof(MorePrompts.AnotherPrompt));
    }

    [McpServerPromptType]
    public sealed class SimplePrompts(ObjectWithId? id = null)
    {
        [McpServerPrompt, Description("Returns chat messages")]
        public static ChatMessage[] ReturnsChatMessages([Description("The first parameter")] string message) =>
            [
                new(ChatRole.User, $"The prompt is: {message}"),
                new(ChatRole.User, "Summarize."),
            ];


        [McpServerPrompt, Description("Returns chat messages")]
        public static ChatMessage[] ThrowsException([Description("The first parameter")] string message) =>
            throw new FormatException("uh oh");

        [McpServerPrompt, Description("Returns chat messages")]
        public string ReturnsString([Description("The first parameter")] string message) =>
            $"The prompt is: {message}. The id is {id}.";
    }

    [McpServerToolType]
    public sealed class MorePrompts
    {
        [McpServerPrompt]
        public static PromptMessage AnotherPrompt() =>
            new PromptMessage
            {
                Role = Role.User,
                Content = new() { Text = "hello", Type = "text" },
            };
    }

    public class ObjectWithId
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
    }
}
