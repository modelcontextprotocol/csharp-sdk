using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that the legacy <c>resources/subscribe</c> and <c>resources/unsubscribe</c> RPCs are
/// gated by protocol version. SEP-2575 (the 2026-07-28 revision) removes them in favor of
/// <c>subscriptions/listen</c> with <c>resourceSubscriptions</c>; servers must respond with
/// <c>-32601 MethodNotFound</c>. Initialize-handshake protocol versions still support the legacy
/// RPCs per the spec.
/// </summary>
public sealed class ResourceSubscriptionProtocolGatingTests : ClientServerTestBase
{
    public ResourceSubscriptionProtocolGatingTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithResources<SubscribableResources>();
    }

    [McpServerResourceType]
    private sealed class SubscribableResources
    {
        [McpServerResource(UriTemplate = "test://resource/{id}"), Description("A subscribable test resource")]
        public static string GetResource(string id) => $"Resource content: {id}";
    }

    [Fact]
    public async Task Subscribe_OnJuly2026ProtocolSession_ReturnsMethodNotFound()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        });

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.SubscribeToResourceAsync(
                new SubscribeRequestParams { Uri = "test://resource/1" },
                TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.MethodNotFound, ex.ErrorCode);
        // The rejection must point callers at the SEP-2575 replacement.
        Assert.Contains(RequestMethods.SubscriptionsListen, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsubscribe_OnJuly2026ProtocolSession_ReturnsMethodNotFound()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        });

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.UnsubscribeFromResourceAsync(
                new UnsubscribeRequestParams { Uri = "test://resource/1" },
                TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.MethodNotFound, ex.ErrorCode);
        Assert.Contains(RequestMethods.SubscriptionsListen, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subscribe_OnInitializeHandshakeSession_StillSucceeds()
    {
        // Default server config; client pinned to 2025-11-25.
        StartServer();
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
        });

        // Should complete without throwing on the initialize-handshake revision.
        await client.SubscribeToResourceAsync(
            new SubscribeRequestParams { Uri = "test://resource/1" },
            TestContext.Current.CancellationToken);

        await client.UnsubscribeFromResourceAsync(
            new UnsubscribeRequestParams { Uri = "test://resource/1" },
            TestContext.Current.CancellationToken);
    }
}
