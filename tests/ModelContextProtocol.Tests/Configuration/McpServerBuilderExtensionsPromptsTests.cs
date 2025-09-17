using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Collections;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Configuration;

public partial class McpServerBuilderExtensionsPromptsTests : ClientServerTestBase
{
    public McpServerBuilderExtensionsPromptsTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder
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
                                throw new McpException($"Unexpected cursor: '{cursor}'", McpErrorCode.InvalidParams);
                        }
                    })
        .WithGetPromptHandler(async (request, cancellationToken) =>
        {
            switch (request.Params?.Name)
            {
                case "FirstCustomPrompt":
                case "SecondCustomPrompt":
                case "FinalCustomPrompt":
                    return new GetPromptResult
                    {
                        Messages = [new() { Role = Role.User, Content = new TextContentBlock { Text = $"hello from {request.Params.Name}" } }],
                    };

                default:
                    throw new McpException($"Unknown prompt '{request.Params?.Name}'", McpErrorCode.InvalidParams);
            }
        })
        .WithPrompts<SimplePrompts>();

        services.AddSingleton(new ObjectWithId());
    }

    [Fact]
    public void Adds_Prompts_To_Server()
    {
        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var prompts = serverOptions?.Capabilities?.Prompts?.PromptCollection;
        Assert.NotNull(prompts);
        Assert.NotEmpty(prompts);
    }

    [Fact]
    public async Task Can_List_And_Call_Registered_Prompts()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, prompts.Count);

        var prompt = prompts.First(t => t.Name == "returns_chat_messages");
        Assert.Equal("Returns chat messages", prompt.Description);

        var result = await prompt.GetAsync(new Dictionary<string, object?>() { ["message"] = "hello" }, cancellationToken: TestContext.Current.CancellationToken);
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
        await using McpClient client = await CreateMcpClientForServer();

        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, prompts.Count);

        Channel<JsonRpcNotification> listChanged = Channel.CreateUnbounded<JsonRpcNotification>();
        var notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(notificationRead.IsCompleted);

        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var serverPrompts = serverOptions.Capabilities?.Prompts?.PromptCollection;
        Assert.NotNull(serverPrompts);

        var newPrompt = McpServerPrompt.Create([McpServerPrompt(Name = "NewPrompt")] () => "42");
        await using (client.RegisterNotificationHandler("notifications/prompts/list_changed", (notification, cancellationToken) =>
            {
                listChanged.Writer.TryWrite(notification);
                return default;
            }))
        {
            serverPrompts.Add(newPrompt);
            await notificationRead;

            prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(7, prompts.Count);
            Assert.Contains(prompts, t => t.Name == "NewPrompt");

            notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
            Assert.False(notificationRead.IsCompleted);
            serverPrompts.Remove(newPrompt);
            await notificationRead;
        }

        prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, prompts.Count);
        Assert.DoesNotContain(prompts, t => t.Name == "NewPrompt");
    }

    [Fact]
    public async Task TitleAttributeProperty_PropagatedToTitle()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(prompts);
        Assert.NotEmpty(prompts);

        McpClientPrompt prompt = prompts.First(t => t.Name == "returns_string");

        Assert.Equal("This is a title", prompt.Title);
    }

    [Fact]
    public async Task Throws_When_Prompt_Fails()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<McpException>(async () => await client.GetPromptAsync(
            nameof(SimplePrompts.ThrowsException),
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_Exception_On_Unknown_Prompt()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var e = await Assert.ThrowsAsync<McpException>(async () => await client.GetPromptAsync(
            "NotRegisteredPrompt",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("'NotRegisteredPrompt'", e.Message);
    }

    [Fact]
    public async Task Throws_Exception_Missing_Parameter()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var e = await Assert.ThrowsAsync<McpException>(async () => await client.GetPromptAsync(
            "returns_chat_messages",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InternalError, e.ErrorCode);
    }

    [Fact]
    public void WithPrompts_InvalidArgs_Throws()
    {
        IMcpServerBuilder builder = new ServiceCollection().AddMcpServer();

        Assert.Throws<ArgumentNullException>("prompts", () => builder.WithPrompts((IEnumerable<McpServerPrompt>)null!));
        Assert.Throws<ArgumentNullException>("promptTypes", () => builder.WithPrompts((IEnumerable<Type>)null!));
        Assert.Throws<ArgumentNullException>("target", () => builder.WithPrompts<object>(target: null!));

        IMcpServerBuilder nullBuilder = null!;
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithPrompts<object>());
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithPrompts(new object()));
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithPrompts(Array.Empty<Type>()));
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithPromptsFromAssembly());
    }

    [Fact]
    public async Task WithPrompts_TargetInstance_UsesTarget()
    {
        ServiceCollection sc = new();

        var target = new SimplePrompts(new ObjectWithId() { Id = "42" });
        sc.AddMcpServer().WithPrompts(target);

        McpServerPrompt prompt = sc.BuildServiceProvider().GetServices<McpServerPrompt>().First(t => t.ProtocolPrompt.Name == "returns_string");
        var result = await prompt.GetAsync(new RequestContext<GetPromptRequestParams>(new Mock<McpServer>().Object, new JsonRpcRequest { Method = "test", Id = new RequestId("1") })
        {
            Params = new GetPromptRequestParams
            {
                Name = "returns_string",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["message"] = JsonSerializer.SerializeToElement("hello", AIJsonUtilities.DefaultOptions),
                }
            }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(target.ReturnsString("hello"), (result.Messages[0].Content as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task WithPrompts_TargetInstance_UsesEnumerableImplementation()
    {
        ServiceCollection sc = new();

        sc.AddMcpServer().WithPrompts(new MyPromptProvider());

        var prompts = sc.BuildServiceProvider().GetServices<McpServerPrompt>().ToArray();
        Assert.Equal(2, prompts.Length);
        Assert.Contains(prompts, t => t.ProtocolPrompt.Name == "Returns42");
        Assert.Contains(prompts, t => t.ProtocolPrompt.Name == "Returns43");
    }

    private sealed class MyPromptProvider : IEnumerable<McpServerPrompt>
    {
        public IEnumerator<McpServerPrompt> GetEnumerator()
        {
            yield return McpServerPrompt.Create(() => "42", new() { Name = "Returns42" });
            yield return McpServerPrompt.Create(() => "43", new() { Name = "Returns43" });
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Empty_Enumerables_Is_Allowed()
    {
        IMcpServerBuilder builder = new ServiceCollection().AddMcpServer();

        builder.WithPrompts(prompts: []); // no exception
        builder.WithPrompts(promptTypes: []); // no exception
        builder.WithPrompts<object>(); // no exception even though no prompts exposed
        builder.WithPromptsFromAssembly(typeof(AIFunction).Assembly); // no exception even though no prompts exposed
    }

    [Fact]
    public void Register_Prompts_From_Current_Assembly()
    {
        ServiceCollection sc = new();
        sc.AddMcpServer().WithPromptsFromAssembly();
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == "returns_chat_messages");
    }

    [Fact]
    public void Register_Prompts_From_Multiple_Sources()
    {
        ServiceCollection sc = new();
        sc.AddMcpServer()
            .WithPrompts<SimplePrompts>()
            .WithPrompts<MorePrompts>(JsonContext4.Default.Options)
            .WithPrompts([McpServerPrompt.Create(() => "42", new() { Name = "Returns42" })]);
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == "returns_chat_messages");
        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == "throws_exception");
        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == "returns_string");
        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == "another_prompt");
        Assert.Contains(services.GetServices<McpServerPrompt>(), t => t.ProtocolPrompt.Name == "Returns42");
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

        [McpServerPrompt(Title = "This is a title"), Description("Returns chat messages")]
        public string ReturnsString([Description("The first parameter")] string message) =>
            $"The prompt is: {message}. The id is {id}.";
    }

    [McpServerToolType]
    public sealed class MorePrompts
    {
        [McpServerPrompt]
        public static PromptMessage AnotherPrompt(ObjectWithId id) =>
            new()
            {
                Role = Role.User,
                Content = new TextContentBlock { Text = "hello" },
            };
    }

    public class ObjectWithId
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
    }

    [JsonSerializable(typeof(ObjectWithId))]
    [JsonSerializable(typeof(PromptMessage))]
    partial class JsonContext4 : JsonSerializerContext;
}
