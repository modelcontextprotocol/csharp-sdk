using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests to verify that handlers are synthesized for empty collections that can be populated dynamically.
/// This addresses the issue where handlers were only created when collections had items.
/// </summary>
public class EmptyCollectionTests : ClientServerTestBase
{
    public EmptyCollectionTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) { }

    private McpServerResourceCollection _resourceCollection = [];
    private McpServerPrimitiveCollection<McpServerTool> _toolCollection = [];
    private McpServerPrimitiveCollection<McpServerPrompt> _promptCollection = [];

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder) =>
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.ResourceCollection = _resourceCollection;
            options.ToolCollection = _toolCollection;
            options.PromptCollection = _promptCollection;
        });

    [Fact]
    public async Task EmptyResourceCollection_CanAddResourcesDynamically()
    {
        var client = await CreateMcpClientForServer();

        // Initially, the resource collection is empty
        var initialResources = await client.ListResourcesAsync(options: null, TestContext.Current.CancellationToken);
        Assert.Empty(initialResources);

        // Add a resource dynamically
        _resourceCollection.Add(McpServerResource.Create(
            () => "test content",
            new() { UriTemplate = "test://resource/1" }));

        // The resource should now be listed
        var updatedResources = await client.ListResourcesAsync(options: null, TestContext.Current.CancellationToken);
        Assert.Single(updatedResources);
        Assert.Equal("test://resource/1", updatedResources[0].Uri);
    }

    [Fact]
    public async Task EmptyToolCollection_CanAddToolsDynamically()
    {
        var client = await CreateMcpClientForServer();

        // Initially, the tool collection is empty
        var initialTools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(initialTools);

        // Add a tool dynamically
        _toolCollection.Add(McpServerTool.Create(
            () => "test result",
            new() { Name = "test_tool", Description = "A test tool" }));

        // The tool should now be listed
        var updatedTools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(updatedTools);
        Assert.Equal("test_tool", updatedTools[0].Name);
    }

    [Fact]
    public async Task EmptyPromptCollection_CanAddPromptsDynamically()
    {
        var client = await CreateMcpClientForServer();

        // Initially, the prompt collection is empty
        var initialPrompts = await client.ListPromptsAsync(options: null, TestContext.Current.CancellationToken);
        Assert.Empty(initialPrompts);

        // Add a prompt dynamically
        _promptCollection.Add(McpServerPrompt.Create(
            () => new ChatMessage(ChatRole.User, "test prompt"),
            new() { Name = "test_prompt", Description = "A test prompt" }));

        // The prompt should now be listed
        var updatedPrompts = await client.ListPromptsAsync(options: null, TestContext.Current.CancellationToken);
        Assert.Single(updatedPrompts);
        Assert.Equal("test_prompt", updatedPrompts[0].Name);
    }

    [Fact]
    public async Task EmptyResourceCollection_CanCallReadResourceAfterAddingDynamically()
    {
        var client = await CreateMcpClientForServer();

        // Add a resource dynamically
        _resourceCollection.Add(McpServerResource.Create(
            () => "dynamic content",
            new() { UriTemplate = "test://resource/dynamic" }));

        // Read the resource
        var result = await client.ReadResourceAsync("test://resource/dynamic", options: null, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Contents);
        Assert.IsType<TextResourceContents>(result.Contents[0]);
        Assert.Equal("dynamic content", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task EmptyToolCollection_CanCallToolAfterAddingDynamically()
    {
        var client = await CreateMcpClientForServer();

        // Add a tool dynamically
        _toolCollection.Add(McpServerTool.Create(
            () => "dynamic result",
            new() { Name = "dynamic_tool", Description = "A dynamic tool" }));

        // Call the tool
        var result = await client.CallToolAsync("dynamic_tool", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Content);
        Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("dynamic result", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task EmptyPromptCollection_CanGetPromptAfterAddingDynamically()
    {
        var client = await CreateMcpClientForServer();

        // Add a prompt dynamically
        _promptCollection.Add(McpServerPrompt.Create(
            () => new ChatMessage(ChatRole.User, "dynamic prompt content"),
            new() { Name = "dynamic_prompt", Description = "A dynamic prompt" }));

        // Get the prompt
        var result = await client.GetPromptAsync("dynamic_prompt", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Messages);
        Assert.Equal(Role.User, result.Messages[0].Role);
        Assert.IsType<TextContentBlock>(result.Messages[0].Content);
        Assert.Equal("dynamic prompt content", ((TextContentBlock)result.Messages[0].Content).Text);
    }
}

/// <summary>
/// Tests to verify that handlers are NOT synthesized when collections are null.
/// This ensures we don't unnecessarily create capabilities when nothing is configured.
/// </summary>
public class NullCollectionTests : ClientServerTestBase
{
    public NullCollectionTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) { }

    [Fact]
    public async Task ListFails()
    {
        Assert.Null(Server.ServerOptions.Capabilities?.Resources);
        Assert.Null(Server.ServerOptions.Capabilities?.Tools);
        Assert.Null(Server.ServerOptions.Capabilities?.Prompts);

        var client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
}
