using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

#pragma warning disable MCPEXP002 // exercises the experimental alternate-result filter seam
public class AlternateResultFilterValidationTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private static McpRequestFilter<CallToolRequestParams, ResultOrAlternate<CallToolResult>> PassThroughAlternateFilter =>
        next => next;

    [Fact]
    public async Task AlternateResultFilter_ForUnknownMethod_ThrowsActionableError()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions();
        options.AddAlternateResultFilter("unknown/method", PassThroughAlternateFilter);

        var exception = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains("unknown/method", exception.Message);
        Assert.Contains("no matching typed handler", exception.Message);
    }

    [Fact]
    public async Task AlternateResultFilter_WithMismatchedTypes_ThrowsActionableError()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            Capabilities = new() { Prompts = new() },
        };
        options.AddAlternateResultFilter(RequestMethods.PromptsGet, PassThroughAlternateFilter);

        var exception = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains(RequestMethods.PromptsGet, exception.Message);
        Assert.Contains(nameof(CallToolRequestParams), exception.Message);
        Assert.Contains(nameof(GetPromptRequestParams), exception.Message);
    }

    [Fact]
    public async Task ExplicitAlternateHandler_WithMethodKeyedFilter_ThrowsActionableError()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        options.Handlers.CallToolWithAlternateHandler = static (request, cancellationToken) =>
            new ValueTask<ResultOrAlternate<CallToolResult>>(new CallToolResult());
        options.AddAlternateResultFilter(RequestMethods.ToolsCall, PassThroughAlternateFilter);

        var exception = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains(nameof(McpServerHandlers.CallToolWithAlternateHandler), exception.Message);
        Assert.Contains(RequestMethods.ToolsCall, exception.Message);
        Assert.Contains("replaces", exception.Message);
    }

    [Fact]
    public async Task ExplicitAlternateHandler_WithOrdinaryFilter_ThrowsActionableError()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        options.Handlers.CallToolWithAlternateHandler = static (request, cancellationToken) =>
            new ValueTask<ResultOrAlternate<CallToolResult>>(new CallToolResult());
        options.Filters.Request.CallToolFilters.Add(next => next);

        var exception = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains(nameof(McpServerHandlers.CallToolWithAlternateHandler), exception.Message);
        Assert.Contains(nameof(McpRequestFilters.CallToolFilters), exception.Message);
        Assert.Contains("replaces", exception.Message);
    }
}
#pragma warning restore MCPEXP002