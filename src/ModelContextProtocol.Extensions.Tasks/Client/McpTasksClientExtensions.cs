using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// Extension methods for task-aware client operations.
/// </summary>
public static class McpTasksClientExtensions
{
    /// <summary>
    /// Calls a tool and returns either an immediate result or a created task.
    /// </summary>
    public static async ValueTask<ResultOrCreatedTask<CallToolResult>> CallToolAsTaskAsync(
        this McpClient client,
        CallToolRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestParams);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
#endif

        var paramsWithMeta = new CallToolRequestParams
        {
            Name = requestParams.Name,
            Arguments = requestParams.Arguments,
            Meta = IsJuly2026OrLaterProtocol(client) ? GetMetaWithTaskCapability(requestParams.Meta) : requestParams.Meta,
        };

        JsonRpcRequest jsonRpcRequest = new()
        {
            Method = RequestMethods.ToolsCall,
            Params = JsonSerializer.SerializeToNode(paramsWithMeta, McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolRequestParams>()),
        };

        JsonRpcResponse response = await client.SendRequestAsync(jsonRpcRequest, cancellationToken).ConfigureAwait(false);

        if (response.Result is JsonObject resultObj &&
            resultObj.TryGetPropertyValue("resultType", out var resultTypeNode) &&
            string.Equals(resultTypeNode?.GetValue<string>(), "task", StringComparison.Ordinal))
        {
            var taskCreated = resultObj.Deserialize(McpTasksJsonContext.Default.CreateTaskResult)
                ?? throw new JsonException("Failed to deserialize CreateTaskResult from response.");
            return new ResultOrCreatedTask<CallToolResult>(taskCreated);
        }

        var callToolResult = JsonSerializer.Deserialize(response.Result, McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolResult>())
            ?? throw new JsonException("Failed to deserialize CallToolResult from response.");
        return new ResultOrCreatedTask<CallToolResult>(callToolResult);
    }

    /// <summary>
    /// Calls a tool and, if needed, polls the created task to completion.
    /// </summary>
    public static async ValueTask<CallToolResult> CallToolWithPollingAsync(
        this McpClient client,
        CallToolRequestParams requestParams,
        int maxConsecutiveStuckPolls = 60,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestParams);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
#endif

        var augmented = await client.CallToolAsTaskAsync(requestParams, cancellationToken).ConfigureAwait(false);
        if (!augmented.IsTask)
        {
            return augmented.Result!;
        }

        return await PollTaskToCompletionAsync(client, augmented.TaskCreated!, maxConsecutiveStuckPolls, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a task by ID.
    /// </summary>
    public static ValueTask<GetTaskResult> GetTaskAsync(
        this McpClient client,
        string taskId,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(taskId);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (taskId is null) throw new ArgumentNullException(nameof(taskId));
#endif

        return client.GetTaskAsync(new GetTaskRequestParams { TaskId = taskId }, cancellationToken);
    }

    /// <summary>
    /// Retrieves a task using explicit request parameters.
    /// </summary>
    public static ValueTask<GetTaskResult> GetTaskAsync(
        this McpClient client,
        GetTaskRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestParams);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
#endif

        ThrowIfTasksNotSupported(client, nameof(GetTaskAsync));
        return client.SendRequestAsync<GetTaskRequestParams, GetTaskResult>(
            TasksProtocol.MethodTasksGet,
            requestParams,
            McpTasksJsonContext.Default.Options,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Updates a task with input responses.
    /// </summary>
    public static async ValueTask<UpdateTaskResult> UpdateTaskAsync(
        this McpClient client,
        UpdateTaskRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestParams);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
#endif

        ThrowIfTasksNotSupported(client, nameof(UpdateTaskAsync));

        // Manually construct the JSON params because InputResponses is backed by an internal
        // property in Core that the extension's source-gen context cannot access for serialization.
        JsonObject paramsObj = new()
        {
            ["taskId"] = requestParams.TaskId,
        };

        if (requestParams.InputResponses is { Count: > 0 } inputResponses)
        {
            paramsObj["inputResponses"] = JsonSerializer.SerializeToNode(
                inputResponses,
                McpJsonUtilities.DefaultOptions.GetTypeInfo<IDictionary<string, InputResponse>>());
        }

        JsonRpcRequest jsonRpcRequest = new()
        {
            Method = TasksProtocol.MethodTasksUpdate,
            Params = paramsObj,
        };

        JsonRpcResponse response = await client.SendRequestAsync(jsonRpcRequest, cancellationToken).ConfigureAwait(false);
        return response.Result?.Deserialize(McpTasksJsonContext.Default.UpdateTaskResult)
            ?? new UpdateTaskResult();
    }

    /// <summary>
    /// Requests task cancellation by ID.
    /// </summary>
    public static ValueTask<CancelTaskResult> CancelTaskAsync(
        this McpClient client,
        string taskId,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(taskId);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (taskId is null) throw new ArgumentNullException(nameof(taskId));
#endif

        return client.CancelTaskAsync(new CancelTaskRequestParams { TaskId = taskId }, cancellationToken);
    }

    /// <summary>
    /// Requests task cancellation using explicit request parameters.
    /// </summary>
    public static ValueTask<CancelTaskResult> CancelTaskAsync(
        this McpClient client,
        CancelTaskRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestParams);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
#endif

        ThrowIfTasksNotSupported(client, nameof(CancelTaskAsync));
        return client.SendRequestAsync<CancelTaskRequestParams, CancelTaskResult>(
            TasksProtocol.MethodTasksCancel,
            requestParams,
            McpTasksJsonContext.Default.Options,
            cancellationToken: cancellationToken);
    }

    private static async ValueTask<CallToolResult> PollTaskToCompletionAsync(
        McpClient client,
        CreateTaskResult taskCreated,
        int maxConsecutiveStuckPolls,
        CancellationToken cancellationToken)
    {
        string taskId = taskCreated.TaskId;
        long pollIntervalMs = taskCreated.PollIntervalMs ?? 1000;
        HashSet<string>? resolvedRequestKeys = null;
        bool isFirstPoll = true;
        int consecutiveStuckPolls = 0;

        while (true)
        {
            if (!isFirstPoll)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(pollIntervalMs), cancellationToken).ConfigureAwait(false);
            }

            isFirstPoll = false;

            var taskResult = await GetTaskAsync(client, taskId, cancellationToken).ConfigureAwait(false);
            if (taskResult.PollIntervalMs is { } newInterval)
            {
                pollIntervalMs = newInterval;
            }

            switch (taskResult)
            {
                case CompletedTaskResult completed:
                    return JsonSerializer.Deserialize(completed.Result, McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolResult>())
                        ?? throw new JsonException("Failed to deserialize CallToolResult from completed task.");

                case FailedTaskResult failed:
                    throw new McpException($"Task '{taskId}' failed: {failed.Error}");

                case CancelledTaskResult:
                    throw new OperationCanceledException($"Task '{taskId}' was cancelled by the server.");

                case InputRequiredTaskResult inputRequired:
                    var newRequests = new Dictionary<string, InputRequest>();
                    if (inputRequired.InputRequests is { } incomingRequests)
                    {
                        foreach (var kvp in incomingRequests)
                        {
                            if (resolvedRequestKeys is null || !resolvedRequestKeys.Contains(kvp.Key))
                            {
                                newRequests[kvp.Key] = kvp.Value;
                            }
                        }
                    }

                    if (newRequests.Count > 0)
                    {
                        consecutiveStuckPolls = 0;

                        IDictionary<string, InputResponse> inputResponses;
                        try
                        {
                            inputResponses = await client.ResolveInputRequestsAsync(newRequests, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            try
                            {
                                await CancelTaskAsync(client, taskId, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch
                            {
                            }

                            throw;
                        }

                        await UpdateTaskAsync(client, new UpdateTaskRequestParams
                        {
                            TaskId = taskId,
                            InputResponses = inputResponses,
                        }, cancellationToken).ConfigureAwait(false);

                        resolvedRequestKeys ??= new HashSet<string>(StringComparer.Ordinal);
                        foreach (var key in inputResponses.Keys)
                        {
                            resolvedRequestKeys.Add(key);
                        }
                    }
                    else if (++consecutiveStuckPolls >= maxConsecutiveStuckPolls)
                    {
                        try
                        {
                            await CancelTaskAsync(client, taskId, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                        }

                        throw new McpException(
                            $"Task '{taskId}' has remained in '{McpTaskStatus.InputRequired}' for {maxConsecutiveStuckPolls} consecutive polls " +
                            "without publishing new input requests after all previously requested inputs were resolved.");
                    }

                    break;

                case WorkingTaskResult:
                    consecutiveStuckPolls = 0;
                    break;

                default:
                    throw new McpException($"Unexpected task result type '{taskResult.GetType().Name}' for task '{taskId}'.");
            }
        }
    }

    private static JsonObject GetMetaWithTaskCapability(JsonObject? existingMeta)
    {
        JsonObject meta = existingMeta is not null
            ? (JsonObject)existingMeta.DeepClone()
            : [];

        if (meta[MetaKeys.ClientCapabilities] is not JsonObject capsRoot)
        {
            capsRoot = [];
            meta[MetaKeys.ClientCapabilities] = capsRoot;
        }

        if (capsRoot["extensions"] is not JsonObject extensionsRoot)
        {
            extensionsRoot = [];
            capsRoot["extensions"] = extensionsRoot;
        }

        if (!extensionsRoot.ContainsKey(TasksProtocol.ExtensionId))
        {
            extensionsRoot[TasksProtocol.ExtensionId] = new JsonObject();
        }

        return meta;
    }

    private static bool IsJuly2026OrLaterProtocol(McpClient client) =>
        McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(client.NegotiatedProtocolVersion);

    private static void ThrowIfTasksNotSupported(McpClient client, string operationName)
    {
        if (!IsJuly2026OrLaterProtocol(client))
        {
            throw new InvalidOperationException(
                $"'{operationName}' requires a newer protocol revision that supports tasks " +
                $"(the '{McpProtocolVersions.July2026ProtocolVersion}' revision or later). " +
                $"The negotiated protocol version is '{client.NegotiatedProtocolVersion ?? "(none)"}'.");
        }
    }
}
