using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the custom <see cref="McpServerHandlers.SubscriptionsListenHandler"/> (SEP-2575, issue #1662).
/// A custom handler is a full replacement for the built-in <c>subscriptions/listen</c> handling: it owns the
/// stream, sends the acknowledgement itself, streams application-defined notifications, and receives no
/// automatic <c>*/list_changed</c> fan-out. These tests run over the in-memory stream transport exercised by
/// <see cref="ClientServerTestBase"/>.
/// </summary>
public class SubscriptionsListenHandlerTests : ClientServerTestBase
{
    private const string CustomResourceUri = "custom://event/1";
    private const string SentinelResourceUri = "custom://event/sentinel";

    // Signalled after the custom handler has sent its acknowledgement and first notification and is waiting
    // for cancellation. Lets cancellation tests wait until the handler is actually holding the stream open.
    private readonly TaskCompletionSource<bool> _handlerHoldingStream = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Signalled from the custom handler's finally block, proving it observed cancellation and cleaned up.
    private readonly TaskCompletionSource<bool> _handlerCleanedUp = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Set by the fan-out suppression test to ask the still-open handler to emit one more notification. Because
    // that notification is delivered on the same stream, it is a happens-after marker: once the client sees it,
    // any notification the SDK would have fanned out earlier on the same stream must already have been delivered.
    private readonly TaskCompletionSource<bool> _emitSentinelRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SubscriptionsListenHandlerTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Register a tool so the server advertises tools.listChanged; this lets the replacement test below
        // trigger a collection change and prove the built-in fan-out no longer runs for the custom handler.
        mcpServerBuilder.WithTools<ListenTools>();

        mcpServerBuilder.WithSubscriptionsListenHandler(async (request, cancellationToken) =>
        {
            var subscriptionId = request.JsonRpcRequest.Id;

            // SEP-2575 requires the acknowledgement to be the first message on the stream. The custom handler
            // owns this: it echoes back the requested filters as granted and tags the ack with the id.
            var ack = new JsonRpcNotification
            {
                Method = NotificationMethods.SubscriptionsAcknowledgedNotification,
                Params = JsonSerializer.SerializeToNode(
                    new SubscriptionsAcknowledgedNotificationParams { Notifications = request.Params.Notifications },
                    McpJsonUtilities.DefaultOptions),
            };
            TagWithSubscriptionId(ack, subscriptionId);
            await request.Server.SendMessageAsync(ack, cancellationToken);

            // Stream one application-defined notification the built-in handler could never produce on its own,
            // proving the handler drives the stream. Tagged with the same subscription id.
            var updated = new JsonRpcNotification
            {
                Method = NotificationMethods.ResourceUpdatedNotification,
                Params = new JsonObject { ["uri"] = CustomResourceUri },
            };
            TagWithSubscriptionId(updated, subscriptionId);
            await request.Server.SendMessageAsync(updated, cancellationToken);

            _handlerHoldingStream.TrySetResult(true);

            try
            {
                // Remain active for the subscription lifetime, exactly like the built-in handler, until the
                // request-scoped token is cancelled (notifications/cancelled on stdio, client disconnect on HTTP).
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(
                    static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled);

                // If a test asks for an ordered marker (fan-out suppression test), emit one more notification on
                // the same stream and then keep holding; otherwise just wait for cancellation.
                if (await Task.WhenAny(_emitSentinelRequested.Task, cancelled.Task).ConfigureAwait(false) == _emitSentinelRequested.Task)
                {
                    var sentinel = new JsonRpcNotification
                    {
                        Method = NotificationMethods.ResourceUpdatedNotification,
                        Params = new JsonObject { ["uri"] = SentinelResourceUri },
                    };
                    TagWithSubscriptionId(sentinel, subscriptionId);
                    await request.Server.SendMessageAsync(sentinel, cancellationToken);

                    await cancelled.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                _handlerCleanedUp.TrySetResult(true);
            }

            return new EmptyResult();
        });
    }

    [Fact]
    public async Task CustomHandler_July2026_SendsAcknowledgementThenTaggedNotification()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        });

        // Capture the acknowledgement and the streamed notification on a single ordered channel so the test
        // proves the acknowledgement is delivered FIRST, per SEP-2575, not merely that both arrive. Separate
        // channels would let a reversed stream (notification before ack) still pass.
        var streamChannel = Channel.CreateUnbounded<JsonRpcNotification>();

        await using var ackReg = client.RegisterNotificationHandler(NotificationMethods.SubscriptionsAcknowledgedNotification,
            (notification, _) => { streamChannel.Writer.TryWrite(notification); return default; });
        await using var updatedReg = client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification,
            (notification, _) => { streamChannel.Writer.TryWrite(notification); return default; });

        using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var listenTask = SendSubscriptionsListenAsync(
            client, new SubscriptionsListenNotifications { ResourcesListChanged = true }, listenCts.Token);

        // The acknowledgement is always first and carries the subscription id.
        var ack = await streamChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NotificationMethods.SubscriptionsAcknowledgedNotification, ack.Method);
        var subscriptionId = GetSubscriptionId(ack);
        Assert.NotNull(subscriptionId);

        // The custom application notification arrives next, tagged with the same subscription id.
        var updated = await streamChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NotificationMethods.ResourceUpdatedNotification, updated.Method);
        Assert.Equal(subscriptionId, GetSubscriptionId(updated));
        Assert.Equal(CustomResourceUri, (updated.Params as JsonObject)?["uri"]?.GetValue<string>());

        await CancelSubscriptionAsync(listenCts, listenTask);
    }

    [Fact]
    public async Task CustomHandler_ReplacesBuiltIn_SuppressesAutomaticListChangedFanOut()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        });

        var ackChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        var toolsChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        var updatedChannel = Channel.CreateUnbounded<JsonRpcNotification>();

        await using var ackReg = client.RegisterNotificationHandler(NotificationMethods.SubscriptionsAcknowledgedNotification,
            (notification, _) => { ackChannel.Writer.TryWrite(notification); return default; });
        await using var toolsReg = client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification,
            (notification, _) => { toolsChannel.Writer.TryWrite(notification); return default; });
        await using var updatedReg = client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification,
            (notification, _) => { updatedChannel.Writer.TryWrite(notification); return default; });

        using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        // Request tools/list_changed. The built-in handler would fan these out; the custom handler replaces it
        // and never tracks the subscription, so the SDK must deliver nothing automatically for this change.
        var listenTask = SendSubscriptionsListenAsync(
            client, new SubscriptionsListenNotifications { ToolsListChanged = true }, listenCts.Token);

        // Wait until the custom handler is holding the stream open (ack + first notification already sent).
        await _handlerHoldingStream.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ackChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);

        // Mutate the tool collection. With the built-in handler this would deliver a tagged tools/list_changed;
        // under the custom replacement handler it must not, because _activeSubscriptions is never populated.
        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        serverOptions.ToolCollection!.Add(McpServerTool.Create([McpServerTool(Name = "AddedTool")] () => "42"));

        // Ask the still-open handler to emit an ordered marker AFTER the mutation and wait for it to arrive.
        // Notifications are delivered in order on the subscription stream, so once the marker is observed any
        // tools/list_changed the SDK would have (erroneously) fanned out for the mutation must already have
        // been delivered too. Observing the marker with an empty tools channel therefore proves suppression
        // without depending on timing or on fire-and-forget fan-out completing synchronously.
        _emitSentinelRequested.TrySetResult(true);
        JsonRpcNotification marker;
        do
        {
            marker = await updatedChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        }
        while ((marker.Params as JsonObject)?["uri"]?.GetValue<string>() != SentinelResourceUri);

        await CancelSubscriptionAsync(listenCts, listenTask);

        // Completed-and-empty proves nothing was ever delivered, not merely "nothing buffered right now".
        toolsChannel.Writer.Complete();
        Assert.False(await toolsChannel.Reader.WaitToReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CustomHandler_PreJuly2026_IsRejectedWithMethodNotFound()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
        });

        var request = new JsonRpcRequest
        {
            Method = RequestMethods.SubscriptionsListen,
            Params = JsonSerializer.SerializeToNode(
                new SubscriptionsListenRequestParams { Notifications = new SubscriptionsListenNotifications { ResourcesListChanged = true } },
                McpJsonUtilities.DefaultOptions),
        };

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.SendRequestAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.MethodNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task CustomHandler_OnCancellation_ObservesTokenAndCleansUp()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        });

        // Use an explicit request id so the test can address it in a notifications/cancelled message,
        // which is the transport-level cancellation signal for a long-lived subscriptions/listen on stdio
        // (an HTTP client would instead disconnect, which cancels the same request-scoped token).
        var subscriptionId = new RequestId("listen-cancel-1");
        using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var listenTask = SendSubscriptionsListenAsync(
            client, new SubscriptionsListenNotifications { ResourcesListChanged = true }, listenCts.Token, subscriptionId);

        // Ensure the handler is actively holding the stream before cancelling.
        await _handlerHoldingStream.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Deterministically drive server-side cancellation by sending notifications/cancelled for the request
        // id. The server routes this to the request-scoped token the handler is observing.
        await client.SendMessageAsync(new JsonRpcNotification
        {
            Method = NotificationMethods.CancelledNotification,
            Params = JsonSerializer.SerializeToNode(
                new CancelledNotificationParams { RequestId = subscriptionId },
                McpJsonUtilities.DefaultOptions),
        }, TestContext.Current.CancellationToken);

        // The handler observed the token: its cancellation registration ran and its finally block completed.
        await _handlerCleanedUp.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // A cancelled request sends no response, so unblock the client side and confirm it observes cancellation.
        await CancelSubscriptionAsync(listenCts, listenTask);
    }

    private static Task SendSubscriptionsListenAsync(
        McpClient client, SubscriptionsListenNotifications notifications, CancellationToken cancellationToken, RequestId requestId = default)
    {
        var request = new JsonRpcRequest
        {
            Method = RequestMethods.SubscriptionsListen,
            Params = JsonSerializer.SerializeToNode(
                new SubscriptionsListenRequestParams { Notifications = notifications },
                McpJsonUtilities.DefaultOptions),
        };

        if (requestId.Id is not null)
        {
            request.Id = requestId;
        }

        return client.SendRequestAsync(request, cancellationToken);
    }

    private static async Task CancelSubscriptionAsync(CancellationTokenSource listenCts, Task listenTask)
    {
        await listenCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listenTask);
    }

    private static string? GetSubscriptionId(JsonRpcNotification notification)
        => ((notification.Params as JsonObject)?["_meta"] as JsonObject)?[MetaKeys.SubscriptionId]?.ToJsonString();

    private static void TagWithSubscriptionId(JsonRpcNotification notification, RequestId subscriptionId)
    {
        var paramsObject = notification.Params as JsonObject ?? new JsonObject();
        if (paramsObject["_meta"] is not JsonObject meta)
        {
            meta = new JsonObject();
            paramsObject["_meta"] = meta;
        }

        meta[MetaKeys.SubscriptionId] = subscriptionId.Id switch
        {
            string stringId => JsonValue.Create(stringId),
            long longId => JsonValue.Create(longId),
            _ => null,
        };

        notification.Params = paramsObject;
    }

    [McpServerToolType]
    private sealed class ListenTools
    {
        [McpServerTool, System.ComponentModel.Description("Echoes the input back to the caller.")]
        public static string Echo([System.ComponentModel.Description("The message to echo.")] string message) => message;
    }
}
