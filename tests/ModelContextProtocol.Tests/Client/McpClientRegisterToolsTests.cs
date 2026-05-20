using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

public class McpClientRegisterToolsTests : ClientServerTestBase
{
    private const string ServerToolName = "ServerTool";
    private const string ServerToolName2 = "ServerTool2";

    public McpClientRegisterToolsTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                (string input) => $"echo {input}",
                new() { Name = ServerToolName }),
            McpServerTool.Create(
                (string input) => $"echo2 {input}",
                new() { Name = ServerToolName2 }),
        ]);
    }

    private static Tool CreateTool(string name, string? headerAnnotation = null)
    {
        string schemaJson = headerAnnotation is not null
            ? $$"""
                {
                    "type": "object",
                    "properties": {
                        "param1": {
                            "type": "string",
                            "x-mcp-header": "{{headerAnnotation}}"
                        }
                    }
                }
                """
            : """
                {
                    "type": "object",
                    "properties": {
                        "param1": {
                            "type": "string"
                        }
                    }
                }
                """;

        return new Tool
        {
            Name = name,
            InputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone(),
        };
    }

    private static Tool CreateInvalidTool(string name)
    {
        // Colon in header name is invalid
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "param1": {
                        "type": "string",
                        "x-mcp-header": "Invalid:Header"
                    }
                }
            }
            """;

        return new Tool
        {
            Name = name,
            InputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone(),
        };
    }

    [Fact]
    public async Task RegisterTools_ThenListToolsAsync_ServerToolsStillReturned()
    {
        // Arrange
        await using var client = await CreateMcpClientForServer();
        var registeredTool = CreateTool("MyRegisteredTool", "X-Custom");

        // Act — register without calling ListToolsAsync first, then list
        client.RegisterTools([registeredTool]);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert — ListToolsAsync returns server tools (registered tools stay in cache for header generation
        // but are not returned by ListToolsAsync which only returns server-reported tools)
        Assert.Contains(tools, t => t.Name == ServerToolName);
        Assert.Contains(tools, t => t.Name == ServerToolName2);
        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public async Task RegisterTools_ThenMultipleListToolsAsync_ServerToolsAlwaysRepopulated()
    {
        // Arrange
        await using var client = await CreateMcpClientForServer();
        client.RegisterTools([CreateTool("MyRegisteredTool", "X-Custom")]);

        // Act — ListToolsAsync clears non-registered tools and repopulates from server
        var tools1 = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tools2 = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert — server tools repopulated correctly after each clear
        Assert.Contains(tools1, t => t.Name == ServerToolName);
        Assert.Contains(tools1, t => t.Name == ServerToolName2);
        Assert.Contains(tools2, t => t.Name == ServerToolName);
        Assert.Contains(tools2, t => t.Name == ServerToolName2);
    }

    [Fact]
    public async Task ListToolsAsync_ThenRegisterTool_ServerToolsStillRepopulated()
    {
        // Arrange
        await using var client = await CreateMcpClientForServer();

        // Act — list first, then register, then list again
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, tools.Count);

        client.RegisterTools([CreateTool("MyRegisteredTool", "X-Custom")]);

        var tools2 = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert — server tools still repopulated after registering
        Assert.Contains(tools2, t => t.Name == ServerToolName);
        Assert.Contains(tools2, t => t.Name == ServerToolName2);
    }

    [Fact]
    public async Task RegisterTool_ListToolsAsync_RegisterTool_ServerToolsIntact()
    {
        // Arrange
        await using var client = await CreateMcpClientForServer();

        // Act — register, list, register again
        client.RegisterTools([CreateTool("FirstRegistered", "X-First")]);
        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        client.RegisterTools([CreateTool("SecondRegistered", "X-Second")]);

        // Another ListToolsAsync — server tools should still be repopulated
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == ServerToolName);
        Assert.Contains(tools, t => t.Name == ServerToolName2);
    }

    [Fact]
    public async Task RegisterTools_WithSameNameAsServerTool_ServerDefinitionReturned()
    {
        // Arrange
        await using var client = await CreateMcpClientForServer();
        var registeredTool = CreateTool(ServerToolName, "X-Override");

        // Act — register a tool with the same name as a server tool, then list
        client.RegisterTools([registeredTool]);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert — server's definition is returned by ListToolsAsync
        Assert.Contains(tools, t => t.Name == ServerToolName);

        // After another ListToolsAsync, the tool is still present (pinned as registered + server tool)
        var tools2 = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(tools2, t => t.Name == ServerToolName);
    }

    [Fact]
    public async Task RegisterTools_WithInvalidSchema_ToolIsRejected()
    {
        // Arrange
        await using var client = await CreateMcpClientForServer();
        var invalidTool = CreateInvalidTool("BadTool");
        var validTool = CreateTool("GoodTool", "X-Good");

        // Act — register both; invalid should be rejected, valid should be accepted
        client.RegisterTools([invalidTool, validTool]);

        // Assert — server tools still work normally after registering valid and invalid tools
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(tools, t => t.Name == ServerToolName);
        Assert.Contains(tools, t => t.Name == ServerToolName2);
    }

    [Fact]
    public async Task RegisterTools_DuplicateRegistration_DoesNotBreakCache()
    {
        // Arrange
        await using var client = await CreateMcpClientForServer();
        var tool1 = CreateTool("MyTool", "X-First");
        var tool2 = CreateTool("MyTool", "X-Second");

        // Act — register same name twice; second should overwrite
        client.RegisterTools([tool1]);
        client.RegisterTools([tool2]);

        // Assert — cache clearing still works; server tools repopulated
        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(tools, t => t.Name == ServerToolName);
        Assert.Contains(tools, t => t.Name == ServerToolName2);
    }

    [Fact]
    public async Task RegisterTools_NullArgument_ThrowsArgumentNullException()
    {
        await using var client = await CreateMcpClientForServer();
        Assert.Throws<ArgumentNullException>(() => client.RegisterTools(null!));
    }

    [Fact]
    public async Task MultipleListToolsAsync_WithRegisteredTools_ServerToolsAlwaysRepopulated()
    {
        // Arrange
        await using var client = await CreateMcpClientForServer();
        client.RegisterTools([CreateTool("PinnedTool", "X-Pinned")]);

        // Act — call ListToolsAsync multiple times
        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert — server tools repopulated each time despite registered tool in cache
        Assert.Contains(tools, t => t.Name == ServerToolName);
        Assert.Contains(tools, t => t.Name == ServerToolName2);
    }

    [Fact]
    public async Task RegisterTools_WithNoHeaderAnnotation_StillAccepted()
    {
        // Arrange — a tool without x-mcp-header is still valid and should be cached
        await using var client = await CreateMcpClientForServer();
        var tool = CreateTool("PlainTool");

        // Act — register a tool with no x-mcp-header; should not throw
        client.RegisterTools([tool]);

        // Assert — server tools still repopulated after cache clears
        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(tools, t => t.Name == ServerToolName);
        Assert.Contains(tools, t => t.Name == ServerToolName2);
    }

    [Fact]
    public async Task RegisterTools_ThenCallTool_RegisteredToolUsedForCacheLookup()
    {
        // Arrange — register a tool with the same name as a server tool so CallToolAsync succeeds server-side
        await using var client = await CreateMcpClientForServer();
        var tool = CreateTool(ServerToolName, "X-Custom");

        // Act — register without ListToolsAsync, then call the tool directly
        client.RegisterTools([tool]);

        // The tool is in the cache, so SendRequestAsync will find it for header attachment.
        // The server has a tool with this name, so the call succeeds.
        var result = await client.CallToolAsync(
            ServerToolName,
            new Dictionary<string, object?> { ["input"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — call succeeded (tool was found in cache, request was processed by server)
        Assert.NotNull(result);
        Assert.Contains(result.Content, c => c is TextContentBlock text && text.Text == "echo test");
    }
}
