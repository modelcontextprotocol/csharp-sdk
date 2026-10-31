using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

#pragma warning disable MCPEXP002 // Client request filters are experimental

namespace ModelContextProtocol.Tests.Client;

public class McpClientCallToolFilterTests : ClientServerTestBase
{
    private int _deleteInvocations;

    public McpClientCallToolFilterTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                (string input) => $"echo {input}",
                new() { Name = "echo", ReadOnly = true, Destructive = false }),
            McpServerTool.Create(
                (string id) =>
                {
                    Interlocked.Increment(ref _deleteInvocations);
                    return $"deleted {id}";
                },
                new() { Name = "delete_record", Destructive = true }),
        ]);
    }

    private static McpClientRequestFilter<CallToolRequestParams, CallToolResult> BlockDestructiveTools(List<Tool?>? observedTools = null) =>
        next => async (request, cancellationToken) =>
        {
            observedTools?.Add(request.Tool);

            // Fail closed: an unknown tool is treated like a destructive one.
            if (request.Tool?.Annotations?.DestructiveHint is not false)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = $"Blocked by policy: {request.Params.Name}" }],
                };
            }

            return await next(request, cancellationToken);
        };

    private static string GetText(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    [Fact]
    public async Task Filter_SeesAnnotations_AndBlocksDestructiveToolBeforeItReachesServer()
    {
        List<Tool?> observedTools = [];
        McpClientOptions options = new();
        options.Filters.Request.CallToolFilters.Add(BlockDestructiveTools(observedTools));

        await using McpClient client = await CreateMcpClientForServer(options);
        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var blocked = await client.CallToolAsync("delete_record", new Dictionary<string, object?> { ["id"] = "42" }, cancellationToken: TestContext.Current.CancellationToken);
        var allowed = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["input"] = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(blocked.IsError);
        Assert.Equal("Blocked by policy: delete_record", GetText(blocked));
        Assert.Equal(0, _deleteInvocations);

        Assert.NotEqual(true, allowed.IsError);
        Assert.Equal("echo hi", GetText(allowed));

        Assert.Collection(observedTools,
            tool => Assert.True(tool!.Annotations!.DestructiveHint),
            tool => Assert.True(tool!.Annotations!.ReadOnlyHint));
    }

    [Fact]
    public async Task Filter_ToolIsNull_WhenToolWasNotListed()
    {
        List<Tool?> observedTools = [];
        McpClientOptions options = new();
        options.Filters.Request.CallToolFilters.Add(BlockDestructiveTools(observedTools));

        await using McpClient client = await CreateMcpClientForServer(options);

        // No ListToolsAsync, so the client has no definition for the tool and the policy fails closed.
        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["input"] = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Null(Assert.Single(observedTools));
    }

    [Fact]
    public async Task Filter_ToolIsAvailable_ForToolsRegisteredWithAddKnownTools()
    {
        List<Tool?> observedTools = [];
        McpClientOptions options = new();
        options.Filters.Request.CallToolFilters.Add(BlockDestructiveTools(observedTools));

        await using McpClient client = await CreateMcpClientForServer(options);
        client.AddKnownTools([new Tool
        {
            Name = "echo",
            InputSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
            Annotations = new() { DestructiveHint = false },
        }]);

        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["input"] = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("echo hi", GetText(result));
        Assert.False(Assert.Single(observedTools)!.Annotations!.DestructiveHint);
    }

    [Fact]
    public async Task Filter_RunsForEveryCallToolEntryPoint()
    {
        int filterInvocations = 0;
        McpClientOptions options = new();
        options.Filters.Request.CallToolFilters.Add(next => (request, cancellationToken) =>
        {
            Interlocked.Increment(ref filterInvocations);
            return next(request, cancellationToken);
        });

        await using McpClient client = await CreateMcpClientForServer(options);
        var ct = TestContext.Current.CancellationToken;
        var echo = Assert.Single(await client.ListToolsAsync(cancellationToken: ct), t => t.Name == "echo");
        var args = new Dictionary<string, object?> { ["input"] = "hi" };

        await client.CallToolAsync("echo", args, cancellationToken: ct);
        await client.CallToolAsync("echo", args, progress: new Progress<ProgressNotificationValue>(), cancellationToken: ct);
        await client.CallToolAsync(new CallToolRequestParams { Name = "echo", Arguments = new Dictionary<string, JsonElement> { ["input"] = JsonSerializer.SerializeToElement("hi", McpJsonUtilities.DefaultOptions) } }, ct);
        await echo.CallAsync(args, cancellationToken: ct);
        await echo.InvokeAsync(new AIFunctionArguments(args), ct);

        Assert.Equal(5, filterInvocations);
    }

    [Fact]
    public async Task Filters_RunInRegistrationOrder_FirstIsOutermost()
    {
        List<string> log = [];
        McpClientOptions options = new();
        options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
        {
            log.Add("first:before");
            var result = await next(request, cancellationToken);
            log.Add("first:after");
            return result;
        });
        options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
        {
            log.Add("second:before");
            var result = await next(request, cancellationToken);
            log.Add("second:after");
            return result;
        });

        await using McpClient client = await CreateMcpClientForServer(options);
        await client.CallToolAsync("echo", new Dictionary<string, object?> { ["input"] = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["first:before", "second:before", "second:after", "first:after"], log);
    }

    [Fact]
    public async Task Filter_CanRewriteArgumentsAndResult()
    {
        McpClientOptions options = new();
        options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
        {
            request.Params = new CallToolRequestParams
            {
                Name = request.Params.Name,
                Arguments = new Dictionary<string, JsonElement> { ["input"] = JsonSerializer.SerializeToElement("[redacted]", McpJsonUtilities.DefaultOptions) },
            };

            var result = await next(request, cancellationToken);
            result.Meta = new() { ["audited"] = true };
            return result;
        });

        await using McpClient client = await CreateMcpClientForServer(options);
        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["input"] = "4111-1111-1111-1111" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("echo [redacted]", GetText(result));
        Assert.True(result.Meta!["audited"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Filter_ExceptionPropagatesToCaller_AndRequestIsNotSent()
    {
        McpClientOptions options = new();
        options.Filters.Request.CallToolFilters.Add(next => (request, cancellationToken) =>
            throw new InvalidOperationException("denied"));

        await using McpClient client = await CreateMcpClientForServer(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CallToolAsync("delete_record", new Dictionary<string, object?> { ["id"] = "42" }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("denied", ex.Message);
        Assert.Equal(0, _deleteInvocations);
    }

    [Fact]
    public void Filters_SetNull_Throws()
    {
        McpClientOptions options = new();

        Assert.Throws<ArgumentNullException>(() => options.Filters = null!);
        Assert.Throws<ArgumentNullException>(() => options.Filters.Request = null!);
        Assert.Throws<ArgumentNullException>(() => options.Filters.Request.CallToolFilters = null!);
    }
}
