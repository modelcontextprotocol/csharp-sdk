using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies composition of the non-alternate <see cref="McpRequestFilters.CallToolFilters"/> with the
/// alternate <see cref="McpRequestFilters.CallToolWithAlternateFilters"/>.
/// </summary>
public class CallToolFilterMixingTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper)
{
#pragma warning disable MCPEXP002 // exercises the experimental CallToolWithAlternateFilters seam
    private readonly List<string> _invocations = [];

    private static McpRequestFilter<CallToolRequestParams, CallToolResult> PassThroughCallToolFilter =>
        next => next;

    private static McpRequestFilter<CallToolRequestParams, ResultOrAlternate<CallToolResult>> PassThroughAlternateFilter =>
        next => next;

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder) =>
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.CallToolHandler = (request, cancellationToken) =>
            {
                _invocations.Add("handler");
                return new ValueTask<CallToolResult>(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "mixed filters result" }],
                });
            };

            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
            {
                _invocations.Add("ordinary-before");
                var result = await next(request, cancellationToken);
                _invocations.Add("ordinary-after");
                return result;
            });

            options.Filters.Request.CallToolWithAlternateFilters.Add(next => async (request, cancellationToken) =>
            {
                _invocations.Add("alternate-before");
                var result = await next(request, cancellationToken);
                _invocations.Add("alternate-after");
                return result;
            });
        });

    [Fact]
    public async Task MixingCallToolFilters_WithAlternateFilters_ComposesInOrder()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "mixed-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["alternate-before", "ordinary-before", "handler", "ordinary-after", "alternate-after"],
            _invocations);
        Assert.Equal("mixed filters result", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
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
