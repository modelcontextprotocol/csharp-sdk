using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Connection-flow tests for the 2026-07-28 protocol revision (SEP-2575 + SEP-2567)
/// on <see cref="McpClient"/>. A client that requests
/// <see cref="McpHttpHeaders.July2026ProtocolVersion"/> calls <c>server/discover</c> rather than
/// <c>initialize</c>.
/// </summary>
public class July2026ProtocolConnectionTests : ClientServerTestBase
{
    private const string LatestStableVersion = "2025-11-25";

    public July2026ProtocolConnectionTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ServerInfo = new Implementation { Name = nameof(July2026ProtocolConnectionTests), Version = "1.0" };
        });
    }

    [Fact]
    public async Task Client_RequestingJuly2026Protocol_NegotiatesIt()
    {
        StartServer();

        var options = new McpClientOptions { ProtocolVersion = McpHttpHeaders.July2026ProtocolVersion };
        await using var client = await CreateMcpClientForServer(options);

        Assert.Equal(McpHttpHeaders.July2026ProtocolVersion, client.NegotiatedProtocolVersion);
        Assert.NotNull(client.ServerCapabilities);
        Assert.Equal(nameof(July2026ProtocolConnectionTests), client.ServerInfo.Name);
    }

    [Fact]
    public async Task Client_RequestingLegacyVersion_NegotiatesLegacy()
    {
        StartServer();

        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });

        Assert.NotEqual(McpHttpHeaders.July2026ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task LegacyClient_CanCallServerDiscover()
    {
        // server/discover is registered unconditionally, so a legacy client can probe it
        // (e.g., to learn capabilities without doing a second initialize).
        StartServer();

        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ServerDiscover },
            TestContext.Current.CancellationToken);

        var discoverResult = JsonSerializer.Deserialize<DiscoverResult>(response.Result, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(discoverResult);
        Assert.Equal("complete", discoverResult.ResultType);
        Assert.NotEmpty(discoverResult.SupportedVersions);
        Assert.Contains(LatestStableVersion, discoverResult.SupportedVersions);
        Assert.Equal(nameof(July2026ProtocolConnectionTests), discoverResult.ServerInfo.Name);
    }

    [Fact]
    public async Task ServerDiscover_IncludesJuly2026ProtocolVersion()
    {
        StartServer();

        await using var client = await CreateMcpClientForServer();

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ServerDiscover },
            TestContext.Current.CancellationToken);

        var discoverResult = JsonSerializer.Deserialize<DiscoverResult>(response.Result, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(discoverResult);
        Assert.Equal("complete", discoverResult.ResultType);
        Assert.Contains(McpHttpHeaders.July2026ProtocolVersion, discoverResult.SupportedVersions);
    }
}
