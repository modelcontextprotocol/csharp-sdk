using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Tests.Server;

public class CallToolResultOfTTests : ClientServerTestBase
{
    private McpServerPrimitiveCollection<McpServerTool> _toolCollection = [];

    public CallToolResultOfTTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.ToolCollection = _toolCollection;
        });
    }

    [Fact]
    public async Task CallToolAsyncOfT_DeserializesStructuredContent()
    {
        JsonSerializerOptions serOpts = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        _toolCollection.Add(McpServerTool.Create(
            () => new CallToolResult<PersonData> { Content = new PersonData { Name = "Alice", Age = 30 } },
            new() { Name = "get_person", SerializerOptions = serOpts }));

        StartServer();
        var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync<PersonData>(
            "get_person",
            options: new() { JsonSerializerOptions = serOpts },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Alice", result.Name);
        Assert.Equal(30, result.Age);
    }

    [Fact]
    public async Task CallToolAsyncOfT_ThrowsOnIsError()
    {
        JsonSerializerOptions serOpts = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        _toolCollection.Add(McpServerTool.Create(
            () => new CallToolResult<string> { Content = "something went wrong", IsError = true },
            new() { Name = "error_tool", SerializerOptions = serOpts }));

        StartServer();
        var client = await CreateMcpClientForServer();

        var ex = await Assert.ThrowsAsync<McpException>(
            () => client.CallToolAsync<string>(
                "error_tool",
                options: new() { JsonSerializerOptions = serOpts },
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("something went wrong", ex.Message);
    }

    [Fact]
    public async Task CallToolAsyncOfT_FallsBackToTextContent()
    {
        // Use a regular tool that returns structured text, not CallToolResult<T>
        JsonSerializerOptions serOpts = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        _toolCollection.Add(McpServerTool.Create(
            () => new PersonData { Name = "Bob", Age = 25 },
            new() { Name = "text_tool", UseStructuredContent = true, SerializerOptions = serOpts }));

        StartServer();
        var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync<PersonData>(
            "text_tool",
            options: new() { JsonSerializerOptions = serOpts },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Bob", result.Name);
        Assert.Equal(25, result.Age);
    }

    [Fact]
    public async Task CallToolResultOfT_AdvertisesOutputSchema()
    {
        JsonSerializerOptions serOpts = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        _toolCollection.Add(McpServerTool.Create(
            () => new CallToolResult<PersonData> { Content = new PersonData { Name = "Alice", Age = 30 } },
            new() { Name = "schema_tool", SerializerOptions = serOpts }));

        StartServer();
        var client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = Assert.Single(tools);

        Assert.NotNull(tool.ProtocolTool.OutputSchema);
        Assert.Equal("object", tool.ProtocolTool.OutputSchema.Value.GetProperty("type").GetString());
        Assert.True(tool.ProtocolTool.OutputSchema.Value.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("Name", out _));
        Assert.True(props.TryGetProperty("Age", out _));
    }

    [Fact]
    public async Task CallToolAsyncOfT_WithAsyncTool_Works()
    {
        JsonSerializerOptions serOpts = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        _toolCollection.Add(McpServerTool.Create(
            async () =>
            {
                await Task.CompletedTask;
                return new CallToolResult<PersonData> { Content = new PersonData { Name = "Charlie", Age = 35 } };
            },
            new() { Name = "async_tool", SerializerOptions = serOpts }));

        StartServer();
        var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync<PersonData>(
            "async_tool",
            options: new() { JsonSerializerOptions = serOpts },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Charlie", result.Name);
        Assert.Equal(35, result.Age);
    }

    [Fact]
    public async Task CallToolAsyncOfT_WithArguments_Works()
    {
        JsonSerializerOptions serOpts = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        _toolCollection.Add(McpServerTool.Create(
            (string name, int age) => new CallToolResult<PersonData>
            {
                Content = new PersonData { Name = name, Age = age }
            },
            new() { Name = "create_person", SerializerOptions = serOpts }));

        StartServer();
        var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync<PersonData>(
            "create_person",
            new Dictionary<string, object?> { ["name"] = "Diana", ["age"] = 28 },
            options: new() { JsonSerializerOptions = serOpts },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Diana", result.Name);
        Assert.Equal(28, result.Age);
    }

    private class PersonData
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }
}
