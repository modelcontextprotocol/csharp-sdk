using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that combining the non-alternate <see cref="McpRequestFilters.CallToolFilters"/> with the
/// alternate <see cref="McpRequestFilters.CallToolWithAlternateFilters"/> fails at configuration time
/// with an actionable message.
/// </summary>
public class CallToolFilterMixingTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private static McpRequestFilter<CallToolRequestParams, CallToolResult> PassThroughCallToolFilter =>
        next => next;

    private static McpRequestFilter<CallToolRequestParams, ResultOrAlternate<CallToolResult>> PassThroughAlternateFilter =>
        next => next;

    [Fact]
    public async Task MixingCallToolFilters_WithAlternateFilters_ThrowsActionableError()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };
        options.Filters.Request.CallToolFilters.Add(PassThroughCallToolFilter);
        options.Filters.Request.CallToolWithAlternateFilters.Add(PassThroughAlternateFilter);

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains(nameof(McpRequestFilters.CallToolFilters), ex.Message);
        Assert.Contains(nameof(McpRequestFilters.CallToolWithAlternateFilters), ex.Message);
        Assert.Contains("AddAuthorizationFilters", ex.Message);
        Assert.Contains("WithTasks", ex.Message);
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
}
