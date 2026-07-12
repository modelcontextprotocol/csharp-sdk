using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Connection-flow tests for the 2026-07-28 protocol revision (SEP-2575 + SEP-2567)
/// on <see cref="McpClient"/>. A client that requests
/// <see cref="McpProtocolVersions.July2026ProtocolVersion"/> calls <c>server/discover</c> rather than
/// <c>initialize</c>.
/// </summary>
public class July2026ProtocolConnectionTests : ClientServerTestBase
{
    private const string LatestStableVersion = McpProtocolVersions.November2025ProtocolVersion;

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

        var options = new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion };
        await using var client = await CreateMcpClientForServer(options);

        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);
        Assert.NotNull(client.ServerCapabilities);
        Assert.Equal(nameof(July2026ProtocolConnectionTests), client.ServerInfo.Name);
    }

    [Fact]
    public async Task Client_RequestingInitializeHandshakeVersion_NegotiatesIt()
    {
        StartServer();

        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });

        Assert.NotEqual(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task InitializeHandshakeClient_CannotCallServerDiscover()
    {
        // server/discover is registered unconditionally so the protocol boundary filter can return a structured
        // error, but initialize-handshake clients cannot use it after negotiating an older protocol version.
        StartServer();

        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.SendRequestAsync(
                new JsonRpcRequest { Method = RequestMethods.ServerDiscover },
                TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.MethodNotFound, exception.ErrorCode);
        Assert.Contains(RequestMethods.ServerDiscover, exception.Message, StringComparison.Ordinal);
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
        Assert.Equal([McpProtocolVersions.July2026ProtocolVersion], discoverResult.SupportedVersions);
    }
}
