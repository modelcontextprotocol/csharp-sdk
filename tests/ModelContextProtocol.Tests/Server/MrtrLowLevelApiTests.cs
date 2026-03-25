using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the low-level MRTR server API — IsMrtrSupported, IncompleteResultException,
/// and client auto-retry of incomplete results.
/// </summary>
public class MrtrLowLevelApiTests : ClientServerTestBase
{
    private readonly ServerMessageTracker _messageTracker = new();

    public MrtrLowLevelApiTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ExperimentalProtocolVersion = "2026-06-XX";
            _messageTracker.AddFilters(options.Filters.Message);
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                static string (McpServer server) =>
                {
                    throw new IncompleteResultException(requestState: "should-not-work");
                },
                new McpServerToolCreateOptions
                {
                    Name = "always-incomplete",
                    Description = "Tool that always throws IncompleteResultException"
                }),
        ]);
    }

    [Fact]
    public async Task LowLevel_IncompleteResultException_WithoutExperimental_ReturnsError()
    {
        StartServer();
        // Client does NOT set ExperimentalProtocolVersion
        var clientOptions = new McpClientOptions();

        await using var client = await CreateMcpClientForServer(clientOptions);

        // The always-incomplete tool throws IncompleteResultException with only requestState
        // and no inputRequests. Without MRTR negotiated, the backcompat layer can't resolve
        // the request (no inputRequests to dispatch), so it wraps it in an error.
        var exception = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("always-incomplete",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without input requests", exception.Message);
    }
}
