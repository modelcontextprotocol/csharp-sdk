using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Server;

public class McpServerToolUnknownArgumentsTests(ITestOutputHelper testOutput) : ClientServerTestBase(testOutput)
{
    private readonly List<Exception> _callToolExceptions = [];

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        JsonSerializerOptions strict = new(McpJsonUtilities.DefaultOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        mcpServerBuilder.WithTools<StrictUnknownArgumentTools>(strict);
        mcpServerBuilder.WithTools<DefaultUnknownArgumentTools>();
        mcpServerBuilder.WithRequestFilters(filters =>
        {
            filters.AddCallToolFilter(next => async (request, cancellationToken) =>
            {
                try
                {
                    return await next(request, cancellationToken);
                }
                catch (Exception ex)
                {
                    _callToolExceptions.Add(ex);
                    throw;
                }
            });
        });
    }

    [Fact]
    public async Task CallTool_Strict_UnknownArgument_ReturnsSpecificError()
    {
        await using McpClient client = await CreateMcpClientForServer();

        CallToolResult result = await client.CallToolAsync(
            "query_records",
            new Dictionary<string, object?> { ["table"] = "t", ["filters"] = "x" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is true);
        Assert.Equal(
            "An error occurred invoking 'query_records': Unknown argument 'filters'. Valid arguments: table, filter.",
            Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task CallTool_Strict_UnknownArgument_WhenToolHasInjectedParameter_ReturnsSpecificError()
    {
        await using McpClient client = await CreateMcpClientForServer();

        CallToolResult result = await client.CallToolAsync(
            "query_records_with_server",
            new Dictionary<string, object?> { ["table"] = "t", ["filters"] = "x" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is true);
        Assert.Equal(
            "An error occurred invoking 'query_records_with_server': Unknown argument 'filters'. Valid arguments: table, filter.",
            Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task CallTool_Strict_ValidArguments_ReachTheToolIntact()
    {
        await using McpClient client = await CreateMcpClientForServer();

        CallToolResult result = await client.CallToolAsync(
            "query_records",
            new Dictionary<string, object?> { ["table"] = "users", ["filter"] = "active" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is not true);
        Assert.Equal("ok table=users filter=active", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task CallTool_Strict_ErrorMessageOmitsArgumentValues()
    {
        await using McpClient client = await CreateMcpClientForServer();

        CallToolResult result = await client.CallToolAsync(
            "query_records",
            new Dictionary<string, object?> { ["table"] = "t", ["filters"] = "secret-value" },
            cancellationToken: TestContext.Current.CancellationToken);

        string text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.True(result.IsError is true);
        Assert.DoesNotContain("secret-value", text);
        Assert.Equal(
            "An error occurred invoking 'query_records': Unknown argument 'filters'. Valid arguments: table, filter.",
            text);
    }

    [Fact]
    public async Task CallTool_Default_IgnoresUnknownArguments()
    {
        await using McpClient client = await CreateMcpClientForServer();

        CallToolResult result = await client.CallToolAsync(
            "query_records_default",
            new Dictionary<string, object?> { ["table"] = "t", ["filters"] = "x" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is not true);
        Assert.Equal("ok table=t filter=<null>", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task CallTool_Strict_UnknownArgument_IsObservedAsMcpExceptionByFilter()
    {
        await using McpClient client = await CreateMcpClientForServer();

        CallToolResult result = await client.CallToolAsync(
            "query_records",
            new Dictionary<string, object?> { ["table"] = "t", ["filters"] = "x" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is true);
        McpException ex = Assert.IsType<McpException>(Assert.Single(_callToolExceptions));
        Assert.Equal("Unknown argument 'filters'. Valid arguments: table, filter.", ex.Message);
    }

    [McpServerToolType]
    public sealed class StrictUnknownArgumentTools
    {
        [McpServerTool(Name = "query_records")]
        public static string Query(string table, string? filter = null) =>
            $"ok table={table} filter={filter ?? "<null>"}";

        [McpServerTool(Name = "query_records_with_server")]
        public static string QueryWithServer(McpServer server, string table, string? filter = null) =>
            $"ok table={table} filter={filter ?? "<null>"}";
    }

    [McpServerToolType]
    public sealed class DefaultUnknownArgumentTools
    {
        [McpServerTool(Name = "query_records_default")]
        public static string Query(string table, string? filter = null) =>
            $"ok table={table} filter={filter ?? "<null>"}";
    }
}
