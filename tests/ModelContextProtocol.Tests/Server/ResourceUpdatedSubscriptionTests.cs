using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Server;

public sealed class ResourceUpdatedSubscriptionTests : ClientServerTestBase
{
    public ResourceUpdatedSubscriptionTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options => options.Capabilities = new()
        {
            Resources = new() { Subscribe = true },
        });
        mcpServerBuilder.WithResources([McpServerResource.Create(() => "content", new() { UriTemplate = "test://resource/1" })]);
    }

    [Fact]
    public async Task July2026Protocol_ResourceUpdate_ReachesEveryMatchingListenStream()
    {
        await using McpClient client = await CreateMcpClientForServer();
        const string resourceUri = "test://resource/1";

        var acknowledgements = Channel.CreateUnbounded<JsonRpcNotification>();
        var updates = Channel.CreateUnbounded<JsonRpcNotification>();
        await using var ackRegistration = client.RegisterNotificationHandler(NotificationMethods.SubscriptionsAcknowledgedNotification,
            (notification, _) => { acknowledgements.Writer.TryWrite(notification); return default; });
        await using var updateRegistration = client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification,
            (notification, _) => { updates.Writer.TryWrite(notification); return default; });

        using var firstCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var secondCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var firstListen = ListenAsync(client, resourceUri, firstCts.Token);
        var firstAck = await acknowledgements.Reader.ReadAsync(TestContext.Current.CancellationToken);
        var secondListen = ListenAsync(client, resourceUri, secondCts.Token);
        var secondAck = await acknowledgements.Reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(resourceUri, GetGrantedUris(firstAck).Single());
        Assert.Equal(resourceUri, GetGrantedUris(secondAck).Single());
        var firstId = GetSubscriptionId(firstAck);
        var secondId = GetSubscriptionId(secondAck);
        Assert.NotEqual(firstId, secondId);

        // A different URI has no matching stream; the next update must be delivered on both streams.
        await Server.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = "test://resource/other" },
            cancellationToken: TestContext.Current.CancellationToken);
        await Server.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = resourceUri },
            cancellationToken: TestContext.Current.CancellationToken);

        var received = new[]
        {
            await updates.Reader.ReadAsync(TestContext.Current.CancellationToken),
            await updates.Reader.ReadAsync(TestContext.Current.CancellationToken),
        };
        Assert.Equal(new[] { firstId, secondId }.OrderBy(id => id, StringComparer.Ordinal),
            received.Select(GetSubscriptionId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(received, update => Assert.Equal(resourceUri, update.Params?["uri"]?.GetValue<string>()));

        // Preserve application metadata and leave the original message untouched while tagging each copy.
        var applicationParams = new JsonObject
        {
            ["uri"] = resourceUri,
            ["_meta"] = new JsonObject { ["source"] = "application" },
        };
        await Server.SendMessageAsync(new JsonRpcNotification
        {
            Method = NotificationMethods.ResourceUpdatedNotification,
            Params = applicationParams,
        }, TestContext.Current.CancellationToken);
        Assert.Null(applicationParams["_meta"]?[MetaKeys.SubscriptionId]);

        var metadataUpdates = new[]
        {
            await updates.Reader.ReadAsync(TestContext.Current.CancellationToken),
            await updates.Reader.ReadAsync(TestContext.Current.CancellationToken),
        };
        Assert.Equal(new[] { firstId, secondId }.OrderBy(id => id, StringComparer.Ordinal),
            metadataUpdates.Select(GetSubscriptionId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(metadataUpdates, update => Assert.Equal("application", update.Params?["_meta"]?["source"]?.GetValue<string>()));

        // A round-trip after the sends establishes that no update for the unrelated URI was queued.
        await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        updates.Writer.Complete();
        Assert.False(await updates.Reader.WaitToReadAsync(TestContext.Current.CancellationToken));

        await firstCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstListen);
        await secondCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondListen);
    }

    [Fact]
    public async Task July2026Protocol_WithoutListenStream_DoesNotBroadcastResourceUpdate()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var updates = Channel.CreateUnbounded<JsonRpcNotification>();
        await using var registration = client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification,
            (notification, _) => { updates.Writer.TryWrite(notification); return default; });

        // server/discover is only a probe. An ordinary request commits the modern protocol version
        // before the server sends an application-driven notification outside any request.
        await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await Server.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = "test://resource/1" },
            cancellationToken: TestContext.Current.CancellationToken);
        await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        updates.Writer.Complete();
        Assert.False(await updates.Reader.WaitToReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LegacyProtocol_ResourceUpdate_RemainsSessionWide()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
        });
        var updates = Channel.CreateUnbounded<JsonRpcNotification>();
        await using var registration = client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification,
            (notification, _) => { updates.Writer.TryWrite(notification); return default; });

        await Server.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = "test://resource/1" },
            cancellationToken: TestContext.Current.CancellationToken);

        var update = await updates.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("test://resource/1", update.Params?["uri"]?.GetValue<string>());
        Assert.Null(GetSubscriptionId(update));
    }

    private static Task ListenAsync(McpClient client, string uri, CancellationToken cancellationToken) =>
        client.SendRequestAsync(new JsonRpcRequest
        {
            Method = RequestMethods.SubscriptionsListen,
            Params = JsonSerializer.SerializeToNode(new SubscriptionsListenRequestParams
            {
                Notifications = new() { ResourceSubscriptions = [uri] },
            }, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);

    private static string? GetSubscriptionId(JsonRpcNotification notification) =>
        ((notification.Params as JsonObject)?["_meta"] as JsonObject)?[MetaKeys.SubscriptionId]?.ToJsonString();

    private static IList<string> GetGrantedUris(JsonRpcNotification acknowledgement) =>
        JsonSerializer.Deserialize<SubscriptionsAcknowledgedNotificationParams>(acknowledgement.Params, McpJsonUtilities.DefaultOptions)!
            .Notifications.ResourceSubscriptions!;
}

public sealed class ResourceUpdatedWithoutCapabilityTests : ClientServerTestBase
{
    public ResourceUpdatedWithoutCapabilityTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder) =>
        mcpServerBuilder.WithResources([McpServerResource.Create(() => "content", new() { UriTemplate = "test://resource/1" })]);

    [Fact]
    public async Task July2026Protocol_ResourceUpdate_IsNotSentWhenSubscribeCapabilityIsAbsent()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var acknowledgements = Channel.CreateUnbounded<JsonRpcNotification>();
        var updates = Channel.CreateUnbounded<JsonRpcNotification>();
        await using var ackRegistration = client.RegisterNotificationHandler(NotificationMethods.SubscriptionsAcknowledgedNotification,
            (notification, _) => { acknowledgements.Writer.TryWrite(notification); return default; });
        await using var updateRegistration = client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification,
            (notification, _) => { updates.Writer.TryWrite(notification); return default; });

        using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var listenTask = client.SendRequestAsync(new JsonRpcRequest
        {
            Method = RequestMethods.SubscriptionsListen,
            Params = JsonSerializer.SerializeToNode(new SubscriptionsListenRequestParams
            {
                Notifications = new() { ResourceSubscriptions = ["test://resource/1"] },
            }, McpJsonUtilities.DefaultOptions),
        }, listenCts.Token);

        var acknowledgement = await acknowledgements.Reader.ReadAsync(TestContext.Current.CancellationToken);
        var granted = JsonSerializer.Deserialize<SubscriptionsAcknowledgedNotificationParams>(
            acknowledgement.Params, McpJsonUtilities.DefaultOptions)!;
        Assert.Null(granted.Notifications.ResourceSubscriptions);

        await Server.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = "test://resource/1" },
            cancellationToken: TestContext.Current.CancellationToken);
        await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        updates.Writer.Complete();
        Assert.False(await updates.Reader.WaitToReadAsync(TestContext.Current.CancellationToken));

        await listenCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listenTask);
    }
}
