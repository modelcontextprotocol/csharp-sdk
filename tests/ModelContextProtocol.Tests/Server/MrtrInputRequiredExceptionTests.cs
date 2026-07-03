using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the MRTR server API - IsMrtrSupported, InputRequiredException,
/// and client auto-retry of incomplete results.
/// </summary>
public class MrtrInputRequiredExceptionTests : ClientServerTestBase
{
    private readonly ServerMessageTracker _messageTracker = new();

    public MrtrInputRequiredExceptionTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ProtocolVersion = "2026-07-28";
            _messageTracker.AddFilters(options.Filters.Message);
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                static string (McpServer server) =>
                {
                    throw new InputRequiredException(requestState: "should-not-work");
                },
                new McpServerToolCreateOptions
                {
                    Name = "always-incomplete",
                    Description = "Tool that always throws InputRequiredException"
                }),
        ]);
    }

    [Fact]
    public async Task InputRequiredException_WithoutInputRequests_ExhaustsRetries()
    {
        StartServer();
        var clientOptions = new McpClientOptions();

        await using var client = await CreateMcpClientForServer(clientOptions);

        // The always-incomplete tool throws InputRequiredException with only requestState
        // and no inputRequests. The client has nothing to dispatch, so it keeps retrying
        // with the same requestState until the retry budget is exhausted.
        var exception = await Assert.ThrowsAsync<McpException>(() =>
            client.CallToolAsync("always-incomplete",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("more than", exception.Message);
    }
}
