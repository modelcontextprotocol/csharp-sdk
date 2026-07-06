using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// End-to-end tests for the SEP-2575 <c>subscriptions/listen</c> list-changed delivery over an
/// in-memory stream transport (the stdio-shaped path exercised by <see cref="ClientServerTestBase"/>).
/// Validates that a client on the 2026-07-28 protocol receives only the change notifications it subscribed to, each tagged
/// with the subscription id, and that initialize-handshake sessions keep receiving the session-wide broadcast.
/// </summary>
public class SubscriptionsListenTests : ClientServerTestBase
{
    public SubscriptionsListenTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<ListenTools>();
        mcpServerBuilder.WithPrompts<ListenPrompts>();
    }

    [Fact]
    public async Task July2026Protocol_ToolsListChangedSubscription_DeliversTaggedNotification_AndWithholdsUnsubscribed()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var ackChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        var toolsChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        var promptsChannel = Channel.CreateUnbounded<JsonRpcNotification>();

        await using var ackReg = client.RegisterNotificationHandler(NotificationMethods.SubscriptionsAcknowledgedNotification,
            (notification, _) => { ackChannel.Writer.TryWrite(notification); return default; });
        await using var toolsReg = client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification,
            (notification, _) => { toolsChannel.Writer.TryWrite(notification); return default; });
        await using var promptsReg = client.RegisterNotificationHandler(NotificationMethods.PromptListChangedNotification,
            (notification, _) => { promptsChannel.Writer.TryWrite(notification); return default; });

        using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var listenTask = SendSubscriptionsListenAsync(client, new SubscriptionsListenNotifications { ToolsListChanged = true }, listenCts.Token);

        // SEP-2575: the acknowledgement is always sent first, tagged with the subscription id.
        var ack = await ackChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        var subscriptionId = GetSubscriptionId(ack);
        Assert.NotNull(subscriptionId);

        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var serverTools = serverOptions.ToolCollection!;
        var serverPrompts = serverOptions.PromptCollection!;

        // A prompt change must never reach this client: it only subscribed to tool list changes. Because the
        // fan-out skips it without sending anything, the prompts channel stays empty for the rest of the test.
        serverPrompts.Add(McpServerPrompt.Create([McpServerPrompt(Name = "AddedPrompt")] () => "added"));

        // A tool change must arrive on the subscription stream, tagged with the same subscription id as the ack.
        serverTools.Add(McpServerTool.Create([McpServerTool(Name = "AddedTool")] () => "42"));
        var toolsNotification = await toolsChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(subscriptionId, GetSubscriptionId(toolsNotification));

        // Tear down the open subscription request before the client is disposed.
        await CancelSubscriptionAsync(listenCts, listenTask);

        // The prompt change fired before the (delivered) tool change, and notifications arrive in order
        // on the subscription stream, so any prompt notification would already be buffered by now.
        // Complete the writer and assert the channel drains empty - i.e. nothing was ever delivered,
        // not merely "nothing is buffered at this instant". WaitToReadAsync returns false only when the
        // channel is both empty and completed; a buffered erroneous notification would make it true.
        promptsChannel.Writer.Complete();
        Assert.False(await promptsChannel.Reader.WaitToReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task July2026Protocol_WithoutSubscription_DoesNotBroadcastListChanged()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var toolsChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        await using var toolsReg = client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification,
            (notification, _) => { toolsChannel.Writer.TryWrite(notification); return default; });

        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        serverOptions.ToolCollection!.Add(McpServerTool.Create([McpServerTool(Name = "AddedTool")] () => "42"));

        // The change notification must not be broadcast to a client on the 2026-07-28 protocol that never opened a
        // subscriptions/listen stream. The list-changed handler runs synchronously during Add (before
        // the ListTools round-trip below completes), so any erroneous broadcast would already be
        // buffered once the round-trip returns.
        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Complete the writer and assert the channel drains empty rather than just checking the current
        // buffer: WaitToReadAsync returns false only when the channel is both empty and completed.
        toolsChannel.Writer.Complete();
        Assert.False(await toolsChannel.Reader.WaitToReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeHandshake_ListChanged_IsBroadcast_WithoutSubscription()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
        });

        var toolsChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        await using var toolsReg = client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification,
            (notification, _) => { toolsChannel.Writer.TryWrite(notification); return default; });

        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        serverOptions.ToolCollection!.Add(McpServerTool.Create([McpServerTool(Name = "AddedTool")] () => "42"));

        // Initialize-handshake sessions keep the session-wide broadcast and the notification carries no subscription id.
        var notification = await toolsChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Null(GetSubscriptionId(notification));
    }

    private static Task SendSubscriptionsListenAsync(McpClient client, SubscriptionsListenNotifications notifications, CancellationToken cancellationToken)
    {
        var request = new JsonRpcRequest
        {
            Method = RequestMethods.SubscriptionsListen,
            Params = JsonSerializer.SerializeToNode(
                new SubscriptionsListenRequestParams { Notifications = notifications },
                McpJsonUtilities.DefaultOptions),
        };

        return client.SendRequestAsync(request, cancellationToken);
    }

    private static async Task CancelSubscriptionAsync(CancellationTokenSource listenCts, Task listenTask)
    {
        await listenCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listenTask);
    }

    private static string? GetSubscriptionId(JsonRpcNotification notification)
        => ((notification.Params as JsonObject)?["_meta"] as JsonObject)?[MetaKeys.SubscriptionId]?.ToJsonString();

    [McpServerToolType]
    private sealed class ListenTools
    {
        [McpServerTool, Description("Echoes the input back to the caller.")]
        public static string Echo([Description("The message to echo.")] string message) => message;
    }

    [McpServerPromptType]
    private sealed class ListenPrompts
    {
        [McpServerPrompt, Description("A simple prompt.")]
        public static ChatMessage Simple() => new(ChatRole.User, "hello");
    }
}
