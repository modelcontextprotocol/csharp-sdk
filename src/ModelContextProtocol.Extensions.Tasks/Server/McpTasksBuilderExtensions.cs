using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// Extension methods for <see cref="IMcpServerBuilder"/> to enable MCP Tasks support.
/// </summary>
public static class McpTasksBuilderExtensions
{
    /// <summary>
    /// Enables MCP Tasks support backed by the specified task store.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="store">The task store.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpServerBuilder WithTasks(this IMcpServerBuilder builder, IMcpTaskStore store)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (store is null) throw new ArgumentNullException(nameof(store));
#endif

        // Resolve ILoggerFactory from the provider (rather than requiring the caller to pass one) so the
        // background task body has somewhere to report failures. It is optional: if no logging is
        // registered, the options fall back to NullLoggerFactory.
        builder.Services.AddSingleton<IPostConfigureOptions<McpServerOptions>>(
            sp => new McpTasksPostConfigureOptions(
                store,
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetService<ILoggerFactory>()));
        return builder;
    }

    private sealed class McpTasksPostConfigureOptions(
        IMcpTaskStore store,
        IServiceScopeFactory serviceScopeFactory,
        ILoggerFactory? loggerFactory) : IPostConfigureOptions<McpServerOptions>
    {
        private readonly IMcpTaskStore _store = store;
        private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
        private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<McpTasksPostConfigureOptions>();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationSources = new(StringComparer.Ordinal);

        public void PostConfigure(string? name, McpServerOptions options)
        {
#if NET
            ArgumentNullException.ThrowIfNull(options);
#else
            if (options is null) throw new ArgumentNullException(nameof(options));
#endif

            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            if (!options.Capabilities.Extensions.ContainsKey(TasksProtocol.ExtensionId))
            {
                options.Capabilities.Extensions[TasksProtocol.ExtensionId] = new JsonObject();
            }

            options.RequestHandlers ??= new List<McpServerRequestHandler>();
            options.RequestHandlers.Add(new McpServerRequestHandler { Method = TasksProtocol.MethodTasksGet, Handler = HandleGetTask });
            options.RequestHandlers.Add(new McpServerRequestHandler { Method = TasksProtocol.MethodTasksUpdate, Handler = HandleUpdateTask });
            options.RequestHandlers.Add(new McpServerRequestHandler { Method = TasksProtocol.MethodTasksCancel, Handler = HandleCancelTask });

            // Augment Core's typed tool handler at the outer boundary. This lets the extension
            // return the task alternate immediately while the ordinary filters and tool run in the background.
            options.AddAlternateResultFilter<CallToolRequestParams, CallToolResult>(
                RequestMethods.ToolsCall,
                WrapCallToolWithTasks);
        }

        private McpRequestHandler<CallToolRequestParams, ResultOrAlternate<CallToolResult>> WrapCallToolWithTasks(
            McpRequestHandler<CallToolRequestParams, ResultOrAlternate<CallToolResult>> next) =>
            async (request, cancellationToken) =>
            {
                if (!IsJuly2026OrLaterProtocolRequest(request.JsonRpcRequest) || !HasTaskExtensionOptIn(request.Params?.Meta))
                {
                    return await next(request, cancellationToken).ConfigureAwait(false);
                }

                var executionScope = _serviceScopeFactory.CreateAsyncScope();
                request.Services = executionScope.ServiceProvider;

                McpTaskInfo taskInfo;
                try
                {
                    taskInfo = await _store.CreateTaskAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await executionScope.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                var taskCancellationSource = new CancellationTokenSource();
                _cancellationSources[taskInfo.TaskId] = taskCancellationSource;
                _ = Task.Run(
                    () => RunToolAsTaskAsync(next, request, taskInfo.TaskId, taskCancellationSource, executionScope),
                    CancellationToken.None);

                return ResultOrAlternate<CallToolResult>.FromAlternate(
                    ToCreateTaskResult(taskInfo),
                    McpTasksJsonContext.Default.CreateTaskResult);
            };

        private async Task RunToolAsTaskAsync(
            McpRequestHandler<CallToolRequestParams, ResultOrAlternate<CallToolResult>> next,
            RequestContext<CallToolRequestParams> request,
            string taskId,
            CancellationTokenSource taskCancellationSource,
            AsyncServiceScope executionScope)
        {
            try
            {
                try
                {
                    using (McpTasksServerExtensions.CreateMcpTaskScope(request.Server, taskId, _store))
                    {
                        await ExecuteToolPipelineAsync(next, request, taskId, taskCancellationSource.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await executionScope.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception outer)
            {
                // Expected outcomes are recorded by ExecuteToolPipelineAsync. Reaching here usually
                // means a custom store operation or scope disposal failed. Record it best-effort.
                _logger.LogError(outer, "Background execution of task '{TaskId}' terminated unexpectedly while recording its result.", taskId);

                try
                {
                    var error = new JsonRpcErrorDetail { Code = (int)McpErrorCode.InternalError, Message = outer.Message };
                    var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonRpcErrorDetail>());
                    await _store.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
                }
                catch (Exception storeEx)
                {
                    _logger.LogError(storeEx, "Failed to record the failure of background task '{TaskId}'.", taskId);
                }
            }
            finally
            {
                if (_cancellationSources.TryRemove(taskId, out var registeredCancellationSource))
                {
                    registeredCancellationSource.Dispose();
                }
                else
                {
                    taskCancellationSource.Dispose();
                }
            }
        }

        private async Task ExecuteToolPipelineAsync(
            McpRequestHandler<CallToolRequestParams, ResultOrAlternate<CallToolResult>> next,
            RequestContext<CallToolRequestParams> request,
            string taskId,
            CancellationToken taskCancellationToken)
        {
            try
            {
                var augmented = await next(request, taskCancellationToken).ConfigureAwait(false);

                if (augmented.IsAlternate)
                {
                    var error = new JsonRpcErrorDetail
                    {
                        Code = (int)McpErrorCode.InternalError,
                        Message = $"{nameof(IMcpTaskStore)} is configured and an inner alternate-result filter returned IsAlternate = true. Use only one alternate-result mechanism per request.",
                    };
                    var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonRpcErrorDetail>());
                    await _store.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
                    return;
                }

                var resultJson = JsonSerializer.SerializeToElement(augmented.Result!, McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolResult>());
                await _store.SetCompletedAsync(taskId, resultJson).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (taskCancellationToken.IsCancellationRequested)
            {
                await _store.SetCancelledAsync(taskId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (InputRequiredException)
            {
                var error = new JsonRpcErrorDetail
                {
                    Code = (int)McpErrorCode.InvalidRequest,
                    Message = "MRTR and tasks cannot be composed via [McpServerTool] yet.",
                };
                var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonRpcErrorDetail>());
                await _store.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
            }
            catch (McpProtocolException mcpEx)
            {
                // SEP-2663 §186: protocol exceptions store as failed with JSON-RPC error shape.
                var error = new JsonRpcErrorDetail { Code = (int)mcpEx.ErrorCode, Message = mcpEx.Message };
                var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonRpcErrorDetail>());
                await _store.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Non-protocol exceptions are wrapped as CallToolResult { IsError = true },
                // matching Core's BuildInitialAlternateToolFilter behavior.
                var errorResult = new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock
                    {
                        Text = ex is McpException
                            ? $"An error occurred invoking '{request.Params?.Name}': {ex.Message}"
                            : $"An error occurred invoking '{request.Params?.Name}'.",
                    }],
                };
                var resultJson = JsonSerializer.SerializeToElement(errorResult, McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolResult>());
                await _store.SetCompletedAsync(taskId, resultJson).ConfigureAwait(false);
            }
        }

        private async ValueTask<JsonNode?> HandleGetTask(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            GateToJuly2026OrLaterProtocol(request, TasksProtocol.MethodTasksGet);

            var requestParams = request.Params?.Deserialize(McpTasksJsonContext.Default.GetTaskRequestParams)
                ?? throw new McpProtocolException("Missing params for tasks/get", McpErrorCode.InvalidParams);

            var info = await _store.GetTaskAsync(requestParams.TaskId, cancellationToken).ConfigureAwait(false);
            if (info is null)
            {
                throw new McpProtocolException($"Unknown task: '{requestParams.TaskId}'", McpErrorCode.InvalidParams);
            }

            return JsonSerializer.SerializeToNode(ToGetTaskResult(info), McpTasksJsonContext.Default.GetTaskResult);
        }

        private async ValueTask<JsonNode?> HandleUpdateTask(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            GateToJuly2026OrLaterProtocol(request, TasksProtocol.MethodTasksUpdate);

            var taskId = request.Params?["taskId"]?.GetValue<string>()
                ?? throw new McpProtocolException("Missing params.taskId for tasks/update", McpErrorCode.InvalidParams);

            // Deserialize inputResponses using Core's options which can access the internal
            // InputResponsesCore backing property on RequestParams. The extension's source-gen
            // context cannot see that internal member.
            var inputResponses = request.Params?["inputResponses"]?.Deserialize(
                    McpJsonUtilities.DefaultOptions.GetTypeInfo<IDictionary<string, InputResponse>>())
                ?? new Dictionary<string, InputResponse>();

            await _store.ResolveInputRequestsAsync(taskId, inputResponses, cancellationToken).ConfigureAwait(false);

            return JsonSerializer.SerializeToNode(new UpdateTaskResult(), McpTasksJsonContext.Default.UpdateTaskResult);
        }

        private async ValueTask<JsonNode?> HandleCancelTask(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            GateToJuly2026OrLaterProtocol(request, TasksProtocol.MethodTasksCancel);

            var requestParams = request.Params?.Deserialize(McpTasksJsonContext.Default.CancelTaskRequestParams)
                ?? throw new McpProtocolException("Missing params for tasks/cancel", McpErrorCode.InvalidParams);

            await _store.SetCancelledAsync(requestParams.TaskId, cancellationToken).ConfigureAwait(false);

            if (_cancellationSources.TryRemove(requestParams.TaskId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            return JsonSerializer.SerializeToNode(new CancelTaskResult(), McpTasksJsonContext.Default.CancelTaskResult);
        }

        private static void GateToJuly2026OrLaterProtocol(JsonRpcRequest request, string method)
        {
            if (!IsJuly2026OrLaterProtocolRequest(request))
            {
                throw new McpProtocolException(
                    $"The method '{method}' requires a newer protocol revision that supports tasks " +
                    $"(the '{McpProtocolVersions.July2026ProtocolVersion}' revision or later); " +
                    $"the negotiated protocol version is '{request?.Context?.ProtocolVersion ?? "(none)"}'.",
                    McpErrorCode.MethodNotFound);
            }
        }

        private static bool HasTaskExtensionOptIn(JsonObject? meta) =>
            meta is not null &&
            meta[MetaKeys.ClientCapabilities] is JsonObject caps &&
            caps["extensions"] is JsonObject exts &&
            exts.ContainsKey(TasksProtocol.ExtensionId);

        private static bool IsJuly2026OrLaterProtocolRequest(JsonRpcRequest? request) =>
            McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(request?.Context?.ProtocolVersion);

        private static CreateTaskResult ToCreateTaskResult(McpTaskInfo info) => new()
        {
            TaskId = info.TaskId,
            Status = info.Status,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TimeToLive = info.TimeToLive,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
        };

        private static GetTaskResult ToGetTaskResult(McpTaskInfo info) => info.Status switch
        {
            McpTaskStatus.Working => new WorkingTaskResult
            {
                TaskId = info.TaskId,
                CreatedAt = info.CreatedAt,
                LastUpdatedAt = info.LastUpdatedAt,
                TimeToLive = info.TimeToLive,
                PollIntervalMs = info.PollIntervalMs,
                StatusMessage = info.StatusMessage,
                ResultType = "complete",
            },
            McpTaskStatus.Completed => new CompletedTaskResult
            {
                TaskId = info.TaskId,
                CreatedAt = info.CreatedAt,
                LastUpdatedAt = info.LastUpdatedAt,
                TimeToLive = info.TimeToLive,
                PollIntervalMs = info.PollIntervalMs,
                StatusMessage = info.StatusMessage,
                Result = info.Result ?? throw new InvalidOperationException($"Task '{info.TaskId}' is completed but has no result."),
                ResultType = "complete",
            },
            McpTaskStatus.Failed => new FailedTaskResult
            {
                TaskId = info.TaskId,
                CreatedAt = info.CreatedAt,
                LastUpdatedAt = info.LastUpdatedAt,
                TimeToLive = info.TimeToLive,
                PollIntervalMs = info.PollIntervalMs,
                StatusMessage = info.StatusMessage,
                Error = info.Error ?? throw new InvalidOperationException($"Task '{info.TaskId}' is failed but has no error."),
                ResultType = "complete",
            },
            McpTaskStatus.Cancelled => new CancelledTaskResult
            {
                TaskId = info.TaskId,
                CreatedAt = info.CreatedAt,
                LastUpdatedAt = info.LastUpdatedAt,
                TimeToLive = info.TimeToLive,
                PollIntervalMs = info.PollIntervalMs,
                StatusMessage = info.StatusMessage,
                ResultType = "complete",
            },
            McpTaskStatus.InputRequired => new InputRequiredTaskResult
            {
                TaskId = info.TaskId,
                CreatedAt = info.CreatedAt,
                LastUpdatedAt = info.LastUpdatedAt,
                TimeToLive = info.TimeToLive,
                PollIntervalMs = info.PollIntervalMs,
                StatusMessage = info.StatusMessage,
                InputRequests = info.InputRequests is IDictionary<string, InputRequest> dict
                    ? dict
                    : info.InputRequests?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, InputRequest>(),
                ResultType = "complete",
            },
            _ => throw new InvalidOperationException($"Unknown task status: {info.Status}"),
        };
    }
}
