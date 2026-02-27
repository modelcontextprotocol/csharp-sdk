using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

public class McpClientPromptTests : ClientServerTestBase
{
    public McpClientPromptTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithPrompts<GreetingPrompts>();
    }

    [McpServerPromptType]
    private sealed class GreetingPrompts
    {
        [McpServerPrompt, Description("Generates a greeting prompt")]
        public static ChatMessage Greeting([Description("The name to greet")] string name) =>
            new(ChatRole.User, $"Hello, {name}!");

        [McpServerPrompt, Description("Echoes back the metadata it receives")]
        public static ChatMessage MetadataEcho(RequestContext<GetPromptRequestParams> context) =>
            new(ChatRole.User, context.Params?.Meta?.ToJsonString() ?? "{}");
    }

    [Fact]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var originalPrompt = prompts.First(p => p.Name == "greeting");
        var promptDefinition = originalPrompt.ProtocolPrompt;

        var newPrompt = new McpClientPrompt(client, promptDefinition);

        Assert.NotNull(newPrompt);
        Assert.Equal("greeting", newPrompt.Name);
        Assert.Equal("Generates a greeting prompt", newPrompt.Description);
        Assert.Same(promptDefinition, newPrompt.ProtocolPrompt);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        var promptDefinition = new Prompt
        {
            Name = "test",
            Description = "Test prompt"
        };

        Assert.Throws<ArgumentNullException>("client", () => new McpClientPrompt(null!, promptDefinition));
    }

    [Fact]
    public async Task Constructor_WithNullPrompt_ThrowsArgumentNullException()
    {
        await using McpClient client = await CreateMcpClientForServer();

        Assert.Throws<ArgumentNullException>("prompt", () => new McpClientPrompt(client, null!));
    }

    [Fact]
    public async Task ReusePromptDefinition_AcrossDifferentClients_InvokesSuccessfully()
    {
        Prompt promptDefinition;
        {
            await using McpClient client1 = await CreateMcpClientForServer();
            var prompts = await client1.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
            var greetingPrompt = prompts.First(p => p.Name == "greeting");
            promptDefinition = greetingPrompt.ProtocolPrompt;
        }

        await using McpClient client2 = await CreateMcpClientForServer();

        var reusedPrompt = new McpClientPrompt(client2, promptDefinition);

        var result = await reusedPrompt.GetAsync(
            new Dictionary<string, object?> { ["name"] = "World" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Messages);
        var message = result.Messages.First();
        Assert.NotNull(message.Content);
        var textContent = message.Content as TextContentBlock;
        Assert.NotNull(textContent);
        Assert.Equal("Hello, World!", textContent.Text);
    }

    [Fact]
    public async Task ReusePromptDefinition_PreservesPromptMetadata()
    {
        await using McpClient client = await CreateMcpClientForServer();
        
        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var originalPrompt = prompts.First(p => p.Name == "greeting");
        var promptDefinition = originalPrompt.ProtocolPrompt;

        var reusedPrompt = new McpClientPrompt(client, promptDefinition);

        Assert.Equal(originalPrompt.Name, reusedPrompt.Name);
        Assert.Equal(originalPrompt.Description, reusedPrompt.Description);
        Assert.Equal(originalPrompt.ProtocolPrompt.Name, reusedPrompt.ProtocolPrompt.Name);
        Assert.Equal(originalPrompt.ProtocolPrompt.Description, reusedPrompt.ProtocolPrompt.Description);
    }

    [Fact]
    public async Task ManuallyConstructedPrompt_CanBeInvoked()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var manualPrompt = new Prompt
        {
            Name = "greeting",
            Description = "Generates a greeting prompt"
        };

        var clientPrompt = new McpClientPrompt(client, manualPrompt);

        var result = await clientPrompt.GetAsync(
            new Dictionary<string, object?> { ["name"] = "Test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Messages);
        var message = result.Messages.First();
        Assert.NotNull(message.Content);
        var textContent = message.Content as TextContentBlock;
        Assert.NotNull(textContent);
        Assert.Equal("Hello, Test!", textContent.Text);
    }

    [Fact]
    public async Task GetAsync_WithRequestOptions_PassesMetaToServer()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var prompt = prompts.Single(p => p.Name == "metadata_echo");

        RequestOptions requestOptions = new()
        {
            Meta = new JsonObject
            {
                ["traceId"] = "test-trace-123",
                ["customKey"] = "customValue"
            }
        };

        var result = await prompt.GetAsync(options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        var message = result.Messages.First();
        var textContent = Assert.IsType<TextContentBlock>(message.Content);
        var receivedMetadata = JsonNode.Parse(textContent.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Equal("test-trace-123", receivedMetadata["traceId"]?.GetValue<string>());
        Assert.Equal("customValue", receivedMetadata["customKey"]?.GetValue<string>());
    }
}
