using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

public class McpClientToolTests : ClientServerTestBase
{
    public McpClientToolTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Add a simple echo tool for testing
        mcpServerBuilder.WithTools([McpServerTool.Create((string message) => $"Echo: {message}", new() { Name = "echo", Description = "Echoes back the message" })]);
        
        // Add a tool with parameters for testing
        mcpServerBuilder.WithTools([McpServerTool.Create((int a, int b) => a + b, new() { Name = "add", Description = "Adds two numbers" })]);
        
        // Add a tool that returns complex result
        mcpServerBuilder.WithTools([McpServerTool.Create((string name, int age) => $"Person: {name}, Age: {age}", new() { Name = "createPerson", Description = "Creates a person description" })]);
    }

    [Fact]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // Get a tool definition from the server
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var originalTool = tools.First(t => t.Name == "echo");
        var toolDefinition = originalTool.ProtocolTool;

        // Create a new McpClientTool using the public constructor
        var newTool = new McpClientTool(client, toolDefinition);

        Assert.NotNull(newTool);
        Assert.Equal("echo", newTool.Name);
        Assert.Equal("Echoes back the message", newTool.Description);
        Assert.Same(toolDefinition, newTool.ProtocolTool);
    }

    [Fact]
    public async Task Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        var toolDefinition = new Tool
        {
            Name = "test",
            Description = "Test tool"
        };

        var exception = Assert.Throws<ArgumentNullException>(() => new McpClientTool(null!, toolDefinition));
        Assert.Equal("client", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_WithNullTool_ThrowsArgumentNullException()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = Assert.Throws<ArgumentNullException>(() => new McpClientTool(client, null!));
        Assert.Equal("tool", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_WithNullSerializerOptions_UsesDefaultOptions()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var toolDefinition = new Tool
        {
            Name = "test",
            Description = "Test tool"
        };

        var tool = new McpClientTool(client, toolDefinition, serializerOptions: null);

        Assert.NotNull(tool.JsonSerializerOptions);
        Assert.Same(McpJsonUtilities.DefaultOptions, tool.JsonSerializerOptions);
    }

    [Fact]
    public async Task Constructor_WithCustomSerializerOptions_UsesProvidedOptions()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var toolDefinition = new Tool
        {
            Name = "test",
            Description = "Test tool"
        };

        var customOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var tool = new McpClientTool(client, toolDefinition, customOptions);

        Assert.NotNull(tool.JsonSerializerOptions);
        Assert.Same(customOptions, tool.JsonSerializerOptions);
    }

    [Fact]
    public async Task ReuseToolDefinition_AcrossDifferentClients_InvokesSuccessfully()
    {
        // Create first client and get tool definition
        Tool toolDefinition;
        {
            await using McpClient client1 = await CreateMcpClientForServer();
            var tools = await client1.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            var echoTool = tools.First(t => t.Name == "echo");
            toolDefinition = echoTool.ProtocolTool;
        }

        // Create second client (simulating reconnect)
        await using McpClient client2 = await CreateMcpClientForServer();

        // Create new McpClientTool with cached tool definition and new client
        var reusedTool = new McpClientTool(client2, toolDefinition);

        // Invoke the tool using the new client
        var result = await reusedTool.CallAsync(
            new Dictionary<string, object?> { ["message"] = "Hello from reused tool" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        var textContent = result.Content.FirstOrDefault() as TextContentBlock;
        Assert.NotNull(textContent);
        Assert.Equal("Echo: Hello from reused tool", textContent.Text);
    }

    [Fact]
    public async Task ReuseToolDefinition_WithComplexParameters_InvokesSuccessfully()
    {
        // Create first client and get tool definition
        Tool toolDefinition;
        {
            await using McpClient client1 = await CreateMcpClientForServer();
            var tools = await client1.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            var addTool = tools.First(t => t.Name == "add");
            toolDefinition = addTool.ProtocolTool;
        }

        // Create second client
        await using McpClient client2 = await CreateMcpClientForServer();

        // Create new McpClientTool with cached tool definition
        var reusedTool = new McpClientTool(client2, toolDefinition);

        // Invoke the tool with integer parameters
        var result = await reusedTool.CallAsync(
            new Dictionary<string, object?> { ["a"] = 5, ["b"] = 7 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        var textContent = result.Content.FirstOrDefault() as TextContentBlock;
        Assert.NotNull(textContent);
        Assert.Equal("12", textContent.Text);
    }

    [Fact]
    public async Task ReuseToolDefinition_PreservesToolMetadata()
    {
        await using McpClient client = await CreateMcpClientForServer();
        
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var originalTool = tools.First(t => t.Name == "createPerson");
        var toolDefinition = originalTool.ProtocolTool;

        // Create new McpClientTool with cached tool definition
        var reusedTool = new McpClientTool(client, toolDefinition);

        // Verify metadata is preserved
        Assert.Equal(originalTool.Name, reusedTool.Name);
        Assert.Equal(originalTool.Description, reusedTool.Description);
        Assert.Equal(originalTool.ProtocolTool.Name, reusedTool.ProtocolTool.Name);
        Assert.Equal(originalTool.ProtocolTool.Description, reusedTool.ProtocolTool.Description);
        
        // Verify JSON schema is preserved
        Assert.Equal(
            JsonSerializer.Serialize(originalTool.JsonSchema, McpJsonUtilities.DefaultOptions),
            JsonSerializer.Serialize(reusedTool.JsonSchema, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public async Task ManuallyConstructedTool_CanBeInvoked()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // Manually construct a Tool object matching the server's tool
        var manualTool = new Tool
        {
            Name = "echo",
            Description = "Echoes back the message",
            InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "message": { "type": "string" }
                    }
                }
                """).RootElement.Clone()
        };

        // Create McpClientTool with manually constructed tool
        var clientTool = new McpClientTool(client, manualTool);

        // Invoke the tool
        var result = await clientTool.CallAsync(
            new Dictionary<string, object?> { ["message"] = "Test message" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        var textContent = result.Content.FirstOrDefault() as TextContentBlock;
        Assert.NotNull(textContent);
        Assert.Equal("Echo: Test message", textContent.Text);
    }

    [Fact]
    public async Task ReuseToolDefinition_WithInvokeAsync_WorksCorrectly()
    {
        Tool toolDefinition;
        {
            await using McpClient client1 = await CreateMcpClientForServer();
            var tools = await client1.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            var addTool = tools.First(t => t.Name == "add");
            toolDefinition = addTool.ProtocolTool;
        }

        await using McpClient client2 = await CreateMcpClientForServer();
        var reusedTool = new McpClientTool(client2, toolDefinition);

        // Use AIFunction.InvokeAsync (inherited method)
        var result = await reusedTool.InvokeAsync(
            new() { ["a"] = 10, ["b"] = 20 },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        
        // InvokeAsync returns a JsonElement containing the serialized CallToolResult
        var jsonElement = Assert.IsType<JsonElement>(result);
        var callToolResult = JsonSerializer.Deserialize<CallToolResult>(jsonElement, McpJsonUtilities.DefaultOptions);
        
        Assert.NotNull(callToolResult);
        Assert.NotNull(callToolResult.Content);
        var textContent = callToolResult.Content.FirstOrDefault() as TextContentBlock;
        Assert.NotNull(textContent);
        Assert.Equal("30", textContent.Text);
    }

    [Fact]
    public async Task Constructor_WithToolWithoutDescription_UsesEmptyDescription()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var toolWithoutDescription = new Tool
        {
            Name = "noDescTool",
            Description = null
        };

        var clientTool = new McpClientTool(client, toolWithoutDescription);

        Assert.Equal("noDescTool", clientTool.Name);
        Assert.Equal(string.Empty, clientTool.Description);
    }

    [Fact]
    public async Task ReuseToolDefinition_MultipleClients_AllWorkIndependently()
    {
        // Get tool definition from first client
        Tool toolDefinition;
        {
            await using McpClient client1 = await CreateMcpClientForServer();
            var tools = await client1.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            var echoTool = tools.First(t => t.Name == "echo");
            toolDefinition = echoTool.ProtocolTool;
        }

        // Create and invoke on second client
        string text2;
        {
            await using McpClient client2 = await CreateMcpClientForServer();
            var tool2 = new McpClientTool(client2, toolDefinition);
            var result2 = await tool2.CallAsync(
                new Dictionary<string, object?> { ["message"] = "From client 2" },
                cancellationToken: TestContext.Current.CancellationToken);
            text2 = (result2.Content.FirstOrDefault() as TextContentBlock)?.Text ?? string.Empty;
        }

        // Create and invoke on third client
        string text3;
        {
            await using McpClient client3 = await CreateMcpClientForServer();
            var tool3 = new McpClientTool(client3, toolDefinition);
            var result3 = await tool3.CallAsync(
                new Dictionary<string, object?> { ["message"] = "From client 3" },
                cancellationToken: TestContext.Current.CancellationToken);
            text3 = (result3.Content.FirstOrDefault() as TextContentBlock)?.Text ?? string.Empty;
        }

        // Verify both worked
        Assert.Equal("Echo: From client 2", text2);
        Assert.Equal("Echo: From client 3", text3);
    }
}
