using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that the built-in <c>ping</c> handler is gated by protocol version.
/// SEP-2575 (the 2026-07-28 revision) removes <c>ping</c>; servers must
/// respond with <c>-32601 MethodNotFound</c>. Initialize-handshake protocol
/// versions still support <c>ping</c> per the spec.
/// </summary>
public sealed class PingProtocolGatingTests : ClientServerTestBase
{
    public PingProtocolGatingTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
    }

    [Fact]
    public async Task Ping_OnJuly2026ProtocolSession_ReturnsMethodNotFound()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        });

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.MethodNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task Ping_OnInitializeHandshakeSession_StillSucceeds()
    {
        // Default server config; client pinned to 2025-11-25.
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
        });

        var result = await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }
}
