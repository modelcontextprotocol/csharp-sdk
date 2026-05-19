using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the low-level MRTR server API — IsMrtrSupported, InputRequiredException,
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
            options.ProtocolVersion = "DRAFT-2026-v1";
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
    public async Task LowLevel_IncompleteResultException_WithoutExperimental_ReturnsError()
    {
        StartServer();
        // Client does NOT set DRAFT-2026-v1
        var clientOptions = new McpClientOptions();

        await using var client = await CreateMcpClientForServer(clientOptions);

        // The always-incomplete tool throws InputRequiredException with only requestState
        // and no inputRequests. Without MRTR negotiated, the backcompat layer can't resolve
        // the request (no inputRequests to dispatch), so it wraps it in an error.
        var exception = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("always-incomplete",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without input requests", exception.Message);
    }
}
