using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies composition for the non-alternate <see cref="McpRequestFilters.CallToolFilters"/>
/// and alternate <see cref="McpRequestFilters.CallToolWithAlternateFilters"/> pipelines.
/// </summary>
public class CallToolFilterMixingTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
#pragma warning disable MCPEXP002 // exercises the experimental CallToolWithAlternateFilters seam
    private static McpRequestFilter<CallToolRequestParams, CallToolResult> PassThroughCallToolFilter =>
        next => next;

    private static McpRequestInvocationFilter<CallToolRequestParams, ResultOrAlternate<CallToolResult>> PassThroughAlternateFilter =>
        static (context, next, cancellationToken) => next(context, cancellationToken);

    [Fact]
    public async Task MixingCallToolFilters_WithAlternateFilters_Succeeds()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        options.Filters.Request.CallToolWithAlternateFilters.Add(PassThroughAlternateFilter);
        options.Filters.Request.CallToolFilters.Add(PassThroughCallToolFilter);

        await using var server = McpServer.Create(transport, options, LoggerFactory);

        Assert.NotNull(server);
    }

    [Fact]
    public async Task AlternateFilters_AddedAfterOrdinaryFilters_Succeeds()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        options.Filters.Request.CallToolFilters.Add(PassThroughCallToolFilter);
        options.Filters.Request.CallToolWithAlternateFilters.Add(PassThroughAlternateFilter);

        await using var server = McpServer.Create(transport, options, LoggerFactory);

        Assert.NotNull(server);
    }

    [Fact]
    public async Task CallToolFilters_WithExplicitAlternateHandler_ThrowsActionableError()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        options.Handlers.CallToolWithAlternateHandler = static (_, _) =>
            new(new ResultOrAlternate<CallToolResult>(new CallToolResult()));
        options.Filters.Request.CallToolFilters.Add(PassThroughCallToolFilter);

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains(nameof(McpRequestFilters.CallToolFilters), ex.Message);
        Assert.Contains(nameof(McpServerHandlers.CallToolWithAlternateHandler), ex.Message);
        Assert.Contains("replaces the ordinary tool-call pipeline", ex.Message);
    }

    [Fact]
    public async Task CallToolFiltersAlone_Succeeds()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        options.Filters.Request.CallToolFilters.Add(PassThroughCallToolFilter);

        await using var server = McpServer.Create(transport, options, LoggerFactory);
        Assert.NotNull(server);
    }

    [Fact]
    public async Task AlternateFiltersAlone_Succeeds()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        options.Filters.Request.CallToolWithAlternateFilters.Add(PassThroughAlternateFilter);

        await using var server = McpServer.Create(transport, options, LoggerFactory);
        Assert.NotNull(server);
    }
#pragma warning restore MCPEXP002
}
