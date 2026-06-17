using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Tests for the draft protocol revision (SEP-2575 + SEP-2567) connection flow on
/// <see cref="McpClient"/> — the client should call <c>server/discover</c> instead of
/// <c>initialize</c> when <see cref="McpClientOptions.ProtocolVersion"/> is set to
/// <see cref="McpSessionHandler.DraftProtocolVersion"/>.
/// </summary>
public class DraftConnectionTests : ClientServerTestBase
{
    private const string DraftVersion = McpHttpHeaders.DraftProtocolVersion;
    private const string LatestStableVersion = "2025-11-25";

    public DraftConnectionTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ServerInfo = new Implementation { Name = nameof(DraftConnectionTests), Version = "1.0" };
        });
    }

    [Fact]
    public async Task DraftClient_ConnectingToDraftServer_NegotiatesDraftVersion()
    {
        StartServer();

        var options = new McpClientOptions { ProtocolVersion = DraftVersion };
        await using var client = await CreateMcpClientForServer(options);

        Assert.Equal(DraftVersion, client.NegotiatedProtocolVersion);
        Assert.NotNull(client.ServerCapabilities);
        Assert.Equal(nameof(DraftConnectionTests), client.ServerInfo.Name);
    }

    [Fact]
    public async Task LegacyClient_ConnectingToDraftServer_NegotiatesLegacyVersion()
    {
        StartServer();

        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });

        Assert.NotEqual(DraftVersion, client.NegotiatedProtocolVersion);
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
        Assert.NotEmpty(discoverResult.SupportedVersions);
        Assert.Contains(LatestStableVersion, discoverResult.SupportedVersions);
        Assert.Equal(nameof(DraftConnectionTests), discoverResult.ServerInfo.Name);
    }

    [Fact]
    public async Task DraftServer_DiscoverIncludesDraftVersion()
    {
        StartServer();

        await using var client = await CreateMcpClientForServer();

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ServerDiscover },
            TestContext.Current.CancellationToken);

        var discoverResult = JsonSerializer.Deserialize<DiscoverResult>(response.Result, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(discoverResult);
        Assert.Contains(DraftVersion, discoverResult.SupportedVersions);
    }
}
