using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// Extension methods for task-aware server operations.
/// </summary>
public static class McpTasksServerExtensions
{
    /// <summary>
    /// Sends a task status notification to the connected client.
    /// </summary>
    /// <param name="server">The server sending the notification.</param>
    /// <param name="notificationParams">The notification payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the send operation.</returns>
    public static Task SendTaskStatusNotificationAsync(
        this McpServer server,
        TaskStatusNotificationParams notificationParams,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(notificationParams);
#else
        if (server is null) throw new ArgumentNullException(nameof(server));
        if (notificationParams is null) throw new ArgumentNullException(nameof(notificationParams));
#endif

        return server.SendNotificationAsync(
            TasksProtocol.NotificationTaskStatus,
            notificationParams,
            McpTasksJsonContext.Default.Options,
            cancellationToken);
    }

    /// <summary>
    /// Creates a scope that routes server-initiated requests through the specified task store.
    /// </summary>
    /// <param name="server">The server whose outgoing requests should be redirected.</param>
    /// <param name="taskId">The related task identifier.</param>
    /// <param name="store">The task store used to surface input requests.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous outgoing-request behavior.</returns>
    public static IDisposable CreateMcpTaskScope(this McpServer server, string taskId, IMcpTaskStore store)
    {
#if NET
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(taskId);
        ArgumentNullException.ThrowIfNull(store);
#else
        if (server is null) throw new ArgumentNullException(nameof(server));
        if (taskId is null) throw new ArgumentNullException(nameof(taskId));
        if (store is null) throw new ArgumentNullException(nameof(store));
#endif

        return server.InterceptOutgoingRequests(async (method, paramsNode, cancellationToken) =>
        {
            var requestId = Guid.NewGuid().ToString("N");

            var inputRequest = new InputRequest
            {
                Method = method,
                Params = paramsNode is null
                    ? default
                    : JsonSerializer.SerializeToElement(paramsNode, McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonNode>()),
            };

            var tcs = new TaskCompletionSource<InputResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            void handler(InputResponseReceivedEventArgs args)
            {
                if (args.TaskId == taskId && args.RequestId == requestId)
                {
                    tcs.TrySetResult(args.Response);
                }
            }

            store.InputResponseReceived += handler;
            try
            {
                await store.SetInputRequestsAsync(
                    taskId,
                    new Dictionary<string, InputRequest> { [requestId] = inputRequest },
                    cancellationToken).ConfigureAwait(false);

#if NET
                var response = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
#else
                using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
                {
                    var response = await tcs.Task.ConfigureAwait(false);
                    return JsonNode.Parse(response.RawValue.GetRawText());
                }
#endif

#if NET
                return JsonNode.Parse(response.RawValue.GetRawText());
#endif
            }
            finally
            {
                store.InputResponseReceived -= handler;
            }
        });
    }
}
