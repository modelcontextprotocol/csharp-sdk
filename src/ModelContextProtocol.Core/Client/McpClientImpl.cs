using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <inheritdoc/>
#pragma warning disable MCPEXP002
internal sealed partial class McpClientImpl : McpClient
{
    private static Implementation DefaultImplementation { get; } = new()
    {
        Name = AssemblyNameHelper.DefaultAssemblyName.Name ?? nameof(McpClient),
        Version = AssemblyNameHelper.DefaultAssemblyName.Version?.ToString() ?? "1.0.0",
    };

    private readonly ILogger _logger;
    private readonly ITransport _transport;
    private readonly string _endpointName;
    private readonly McpClientOptions _options;
    private readonly McpSessionHandler _sessionHandler;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private readonly McpTaskCancellationTokenProvider? _taskCancellationTokenProvider;

    private ServerCapabilities? _serverCapabilities;
    private Implementation? _serverInfo;
    private string? _serverInstructions;
    private string? _negotiatedProtocolVersion;

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientImpl"/> class.
    /// </summary>
    /// <param name="transport">The transport to use for communication with the server.</param>
    /// <param name="endpointName">The name of the endpoint for logging and debug purposes.</param>
    /// <param name="options">Options for the client, defining protocol version and capabilities.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    internal McpClientImpl(ITransport transport, string endpointName, McpClientOptions? options, ILoggerFactory? loggerFactory)
#pragma warning restore MCPEXP002
    {
        options ??= new();

        _transport = transport;
        _endpointName = $"Client ({options.ClientInfo?.Name ?? DefaultImplementation.Name} {options.ClientInfo?.Version ?? DefaultImplementation.Version})";
        _options = options;
        _logger = loggerFactory?.CreateLogger<McpClient>() ?? NullLogger<McpClient>.Instance;

        // Only allocate the cancellation token provider if a task store is configured
        if (options.TaskStore is not null)
        {
            _taskCancellationTokenProvider = new();
        }

        var notificationHandlers = new NotificationHandlers();
        var requestHandlers = new RequestHandlers();

        RegisterHandlers(options, notificationHandlers, requestHandlers);

        _sessionHandler = new McpSessionHandler(
            isServer: false,
            transport,
            endpointName,
            requestHandlers,
            notificationHandlers,
            incomingMessageFilter: null,
            outgoingMessageFilter: null,
            _logger);
    }

    private void RegisterHandlers(McpClientOptions options, NotificationHandlers notificationHandlers, RequestHandlers requestHandlers)
    {
        McpClientHandlers handlers = options.Handlers;

        var notificationHandlersFromOptions = handlers.NotificationHandlers;
        var samplingHandler = handlers.SamplingHandler;
        var rootsHandler = handlers.RootsHandler;
        var elicitationHandler = handlers.ElicitationHandler;
        var taskStatusHandler = handlers.TaskStatusHandler;
        var taskStore = options.TaskStore;

        if (notificationHandlersFromOptions is not null)
        {
            notificationHandlers.RegisterRange(notificationHandlersFromOptions);
        }

        if (taskStatusHandler is not null)
        {
            notificationHandlers.Register(
                NotificationMethods.TaskStatusNotification,
                (notification, cancellationToken) =>
                {
                    if (JsonSerializer.Deserialize(notification.Params, McpJsonUtilities.JsonContext.Default.McpTaskStatusNotificationParams) is { } notificationParams)
                    {
                        var task = new McpTask
                        {
                            TaskId = notificationParams.TaskId,
                            Status = notificationParams.Status,
                            StatusMessage = notificationParams.StatusMessage,
                            CreatedAt = notificationParams.CreatedAt,
                            LastUpdatedAt = notificationParams.LastUpdatedAt,
                            TimeToLive = notificationParams.TimeToLive,
                            PollInterval = notificationParams.PollInterval
                        };
                        return taskStatusHandler(task, cancellationToken);
                    }

                    return default;
                });
        }

        if (samplingHandler is not null)
        {
            // If task store is configured, wrap the handler to support task-augmented requests
            if (taskStore is not null)
            {
                requestHandlers.Set(
                    RequestMethods.SamplingCreateMessage,
                    async (request, jsonRpcRequest, cancellationToken) =>
                    {
                        // Check if this is a task-augmented request
                        if (request?.Task is { } taskMetadata)
                        {
                            // Create task in store and return immediately
                            return await ExecuteAsTaskAsync(
                                taskStore,
                                taskMetadata,
                                jsonRpcRequest,
                                async ct =>
                                {
                                    var result = await samplingHandler(
                                        request,
                                        request.ProgressToken is { } token ? new TokenProgress(this, token) : NullProgress.Instance,
                                        ct).ConfigureAwait(false);
                                    return JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CreateMessageResult);
                                },
                                options.SendTaskStatusNotifications,
                                cancellationToken).ConfigureAwait(false);
                        }

                        // Normal synchronous execution - serialize result to JsonElement
                        var samplingResult = await samplingHandler(
                            request,
                            request?.ProgressToken is { } token ? new TokenProgress(this, token) : NullProgress.Instance,
                            cancellationToken).ConfigureAwait(false);
                        return JsonSerializer.SerializeToElement(samplingResult, McpJsonUtilities.JsonContext.Default.CreateMessageResult);
                    },
                    McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
                    McpJsonUtilities.JsonContext.Default.JsonElement); // Return JsonElement to support both CreateMessageResult and CreateTaskResult
            }
            else
            {
                requestHandlers.Set(
                    RequestMethods.SamplingCreateMessage,
                    (request, _, cancellationToken) => samplingHandler(
                        request,
                        request?.ProgressToken is { } token ? new TokenProgress(this, token) : NullProgress.Instance,
                        cancellationToken),
                    McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
                    McpJsonUtilities.JsonContext.Default.CreateMessageResult);
            }

            _options.Capabilities ??= new();
            _options.Capabilities.Sampling ??= new();
        }

        if (rootsHandler is not null)
        {
            requestHandlers.Set(
                RequestMethods.RootsList,
                (request, _, cancellationToken) => rootsHandler(request, cancellationToken),
                McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListRootsResult);

            _options.Capabilities ??= new();
            _options.Capabilities.Roots ??= new();
        }

        if (elicitationHandler is not null)
        {
            // If task store is configured, wrap the handler to support task-augmented requests
            if (taskStore is not null)
            {
                requestHandlers.Set(
                    RequestMethods.ElicitationCreate,
                    async (request, jsonRpcRequest, cancellationToken) =>
                    {
                        // Check if this is a task-augmented request
                        if (request?.Task is { } taskMetadata)
                        {
                            // Create task in store and return immediately
                            return await ExecuteAsTaskAsync(
                                taskStore,
                                taskMetadata,
                                jsonRpcRequest,
                                async ct =>
                                {
                                    var result = await elicitationHandler(request, ct).ConfigureAwait(false);
                                    result = ElicitResult.WithDefaults(request, result);
                                    return JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.ElicitResult);
                                },
                                options.SendTaskStatusNotifications,
                                cancellationToken).ConfigureAwait(false);
                        }

                        // Normal synchronous execution - serialize result to JsonElement
                        var elicitResult = await elicitationHandler(request, cancellationToken).ConfigureAwait(false);
                        elicitResult = ElicitResult.WithDefaults(request, elicitResult);
                        return JsonSerializer.SerializeToElement(elicitResult, McpJsonUtilities.JsonContext.Default.ElicitResult);
                    },
                    McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
                    McpJsonUtilities.JsonContext.Default.JsonElement); // Return JsonElement to support both ElicitResult and CreateTaskResult
            }
            else
            {
                requestHandlers.Set(
                    RequestMethods.ElicitationCreate,
                    async (request, _, cancellationToken) =>
                    {
                        var result = await elicitationHandler(request, cancellationToken).ConfigureAwait(false);
                        return ElicitResult.WithDefaults(request, result);
                    },
                    McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
                    McpJsonUtilities.JsonContext.Default.ElicitResult);
            }

            _options.Capabilities ??= new();
            _options.Capabilities.Elicitation ??= new();
            if (_options.Capabilities.Elicitation.Form is null &&
                _options.Capabilities.Elicitation.Url is null)
            {
                // If both modes are null, default to form mode for backward compatibility.
                _options.Capabilities.Elicitation.Form = new();
            }
        }

        // Register task handlers if a task store is configured
        if (taskStore is not null)
        {
            RegisterTaskHandlers(requestHandlers, taskStore);
        }
    }

    /// <summary>
    /// Executes an operation as a task, creating the task immediately and running the operation asynchronously.
    /// </summary>
    private async ValueTask<JsonElement> ExecuteAsTaskAsync(
        IMcpTaskStore taskStore,
        McpTaskMetadata taskMetadata,
        JsonRpcRequest jsonRpcRequest,
        Func<CancellationToken, Task<JsonElement>> operation,
        bool sendNotifications,
        CancellationToken cancellationToken)
    {
        // Create the task in the store
        var mcpTask = await taskStore.CreateTaskAsync(
            taskMetadata,
            jsonRpcRequest.Id,
            jsonRpcRequest,
            SessionId,
            cancellationToken).ConfigureAwait(false);

        // Register the task for TTL-based cancellation
        var taskCancellationToken = _taskCancellationTokenProvider!.RequestToken(mcpTask.TaskId, mcpTask.TimeToLive);

        // Execute the operation asynchronously in the background
        _ = Task.Run(async () =>
        {
            try
            {
                // Send notification if enabled
                if (sendNotifications)
                {
                    var workingTask = await taskStore.GetTaskAsync(mcpTask.TaskId, SessionId, CancellationToken.None).ConfigureAwait(false);
                    if (workingTask is not null)
                    {
                        _ = NotifyTaskStatusAsync(workingTask, CancellationToken.None);
                    }
                }

                // Execute the operation with task-specific cancellation token
                var result = await operation(taskCancellationToken).ConfigureAwait(false);

                // Store the result
                var completedTask = await taskStore.StoreTaskResultAsync(
                    mcpTask.TaskId,
                    McpTaskStatus.Completed,
                    result,
                    SessionId,
                    CancellationToken.None).ConfigureAwait(false);

                // Send final notification if enabled
                if (sendNotifications)
                {
                    _ = NotifyTaskStatusAsync(completedTask, CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (taskCancellationToken.IsCancellationRequested)
            {
                // Task was cancelled via TTL expiration or explicit cancellation.
                // For TTL expiration, the task is deleted so no status update needed.
                // For explicit cancellation, the cancel handler already updates the status.
            }
            catch (Exception ex)
            {
                // Store error result using a simple string message
                try
                {
                    var errorElement = JsonSerializer.SerializeToElement(ex.Message, McpJsonUtilities.JsonContext.Default.String);
                    await taskStore.StoreTaskResultAsync(
                        mcpTask.TaskId,
                        McpTaskStatus.Failed,
                        errorElement,
                        SessionId,
                        CancellationToken.None).ConfigureAwait(false);

                    // Update task with error message
                    var failedTask = await taskStore.UpdateTaskStatusAsync(
                        mcpTask.TaskId,
                        McpTaskStatus.Failed,
                        ex.Message,
                        SessionId,
                        CancellationToken.None).ConfigureAwait(false);

                    // Send failure notification if enabled
                    if (sendNotifications)
                    {
                        _ = NotifyTaskStatusAsync(failedTask, CancellationToken.None);
                    }
                }
                catch
                {
                    // If we can't store the error result, there's not much we can do
                }
            }
            finally
            {
                // Clean up task cancellation tracking
                _taskCancellationTokenProvider!.Complete(mcpTask.TaskId);
            }
        }, CancellationToken.None);

        // Return the task result immediately
        var createTaskResult = new CreateTaskResult { Task = mcpTask };
        return JsonSerializer.SerializeToElement(createTaskResult, McpJsonUtilities.JsonContext.Default.CreateTaskResult);
    }

    /// <summary>
    /// Sends a task status notification to the connected server.
    /// </summary>
    private Task NotifyTaskStatusAsync(McpTask task, CancellationToken cancellationToken)
    {
        var notificationParams = new McpTaskStatusNotificationParams
        {
            TaskId = task.TaskId,
            Status = task.Status,
            StatusMessage = task.StatusMessage,
            CreatedAt = task.CreatedAt,
            LastUpdatedAt = task.LastUpdatedAt,
            TimeToLive = task.TimeToLive,
            PollInterval = task.PollInterval
        };

        return this.SendNotificationAsync(
            NotificationMethods.TaskStatusNotification,
            notificationParams,
            McpJsonUtilities.JsonContext.Default.McpTaskStatusNotificationParams,
            cancellationToken);
    }

    /// <summary>
    /// Registers handlers for task-related requests from the server.
    /// </summary>
    private void RegisterTaskHandlers(RequestHandlers requestHandlers, IMcpTaskStore taskStore)
    {
        // tasks/get handler - Retrieve task status
        requestHandlers.Set(
            RequestMethods.TasksGet,
            async (request, _, cancellationToken) =>
            {
                if (request?.TaskId is not { } taskId)
                {
                    throw new McpProtocolException("Missing required parameter 'taskId'", McpErrorCode.InvalidParams);
                }

                var task = await taskStore.GetTaskAsync(taskId, SessionId, cancellationToken).ConfigureAwait(false);
                if (task is null)
                {
                    throw new McpProtocolException($"Task not found: '{taskId}'", McpErrorCode.InvalidParams);
                }

                return new GetTaskResult
                {
                    TaskId = task.TaskId,
                    Status = task.Status,
                    StatusMessage = task.StatusMessage,
                    CreatedAt = task.CreatedAt,
                    LastUpdatedAt = task.LastUpdatedAt,
                    TimeToLive = task.TimeToLive,
                    PollInterval = task.PollInterval
                };
            },
            McpJsonUtilities.JsonContext.Default.GetTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.GetTaskResult);

        // tasks/result handler - Retrieve task result (blocking until terminal status)
        requestHandlers.Set(
            RequestMethods.TasksResult,
            async (request, _, cancellationToken) =>
            {
                if (request?.TaskId is not { } taskId)
                {
                    throw new McpProtocolException("Missing required parameter 'taskId'", McpErrorCode.InvalidParams);
                }

                // Poll until task reaches terminal status
                while (true)
                {
                    McpTask? task = await taskStore.GetTaskAsync(taskId, SessionId, cancellationToken).ConfigureAwait(false);
                    if (task is null)
                    {
                        throw new McpProtocolException($"Task not found: '{taskId}'", McpErrorCode.InvalidParams);
                    }

                    // If terminal, break and retrieve result
                    if (task.Status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled)
                    {
                        break;
                    }

                    // Poll according to task's pollInterval (default 1 second)
                    var pollInterval = task.PollInterval ?? TimeSpan.FromSeconds(1);
                    await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
                }

                // Retrieve the stored result
                return await taskStore.GetTaskResultAsync(taskId, SessionId, cancellationToken).ConfigureAwait(false);
            },
            McpJsonUtilities.JsonContext.Default.GetTaskPayloadRequestParams,
            McpJsonUtilities.JsonContext.Default.JsonElement);

        // tasks/list handler - List tasks with pagination
        requestHandlers.Set(
            RequestMethods.TasksList,
            async (request, _, cancellationToken) =>
            {
                var cursor = request?.Cursor;
                return await taskStore.ListTasksAsync(cursor, SessionId, cancellationToken).ConfigureAwait(false);
            },
            McpJsonUtilities.JsonContext.Default.ListTasksRequestParams,
            McpJsonUtilities.JsonContext.Default.ListTasksResult);

        // tasks/cancel handler - Cancel a task
        requestHandlers.Set(
            RequestMethods.TasksCancel,
            async (request, _, cancellationToken) =>
            {
                if (request?.TaskId is not { } taskId)
                {
                    throw new McpProtocolException("Missing required parameter 'taskId'", McpErrorCode.InvalidParams);
                }

                // Signal cancellation if task is still running
                _taskCancellationTokenProvider!.Cancel(taskId);

                var task = await taskStore.CancelTaskAsync(taskId, SessionId, cancellationToken).ConfigureAwait(false);
                if (task is null)
                {
                    throw new McpProtocolException($"Task not found: '{taskId}'", McpErrorCode.InvalidParams);
                }

                return new CancelMcpTaskResult
                {
                    TaskId = task.TaskId,
                    Status = task.Status,
                    StatusMessage = task.StatusMessage,
                    CreatedAt = task.CreatedAt,
                    LastUpdatedAt = task.LastUpdatedAt,
                    TimeToLive = task.TimeToLive,
                    PollInterval = task.PollInterval
                };
            },
            McpJsonUtilities.JsonContext.Default.CancelMcpTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.CancelMcpTaskResult);

        // Advertise task capabilities
        _options.Capabilities ??= new();
        var tasksCapability = _options.Capabilities.Tasks ??= new McpTasksCapability();
        tasksCapability.List ??= new ListMcpTasksCapability();
        tasksCapability.Cancel ??= new CancelMcpTasksCapability();
        var requestsCapability = tasksCapability.Requests ??= new RequestMcpTasksCapability();

        // Only advertise sampling tasks if sampling handler is present
        if (_options.Handlers.SamplingHandler is not null)
        {
            var samplingCapability = requestsCapability.Sampling ??= new SamplingMcpTasksCapability();
            samplingCapability.CreateMessage ??= new CreateMessageMcpTasksCapability();
        }

        // Only advertise elicitation tasks if elicitation handler is present
        if (_options.Handlers.ElicitationHandler is not null)
        {
            var elicitationCapability = requestsCapability.Elicitation ??= new ElicitationMcpTasksCapability();
            elicitationCapability.Create ??= new CreateElicitationMcpTasksCapability();
        }
    }

    /// <inheritdoc/>
    public override string? SessionId => _transport.SessionId;

    /// <inheritdoc/>
    public override string? NegotiatedProtocolVersion => _negotiatedProtocolVersion;

    /// <inheritdoc/>
    public override ServerCapabilities ServerCapabilities => _serverCapabilities ?? throw new InvalidOperationException("The client is not connected.");

    /// <inheritdoc/>
    public override Implementation ServerInfo => _serverInfo ?? throw new InvalidOperationException("The client is not connected.");

    /// <inheritdoc/>
    public override string? ServerInstructions => _serverInstructions;

    /// <summary>
    /// Asynchronously connects to an MCP server, establishes the transport connection, and completes the initialization handshake.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // We don't want the ConnectAsync token to cancel the message processing loop after we've successfully connected.
            // The session handler handles cancelling the loop upon its disposal.
            _ = _sessionHandler.ProcessMessagesAsync(CancellationToken.None);

            // Perform initialization sequence
            using var initializationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            initializationCts.CancelAfter(_options.InitializationTimeout);

            try
            {
                // Send initialize request
                string requestProtocol = _options.ProtocolVersion ?? McpSessionHandler.LatestProtocolVersion;
                var initializeResponse = await SendRequestAsync(
                    RequestMethods.Initialize,
                    new InitializeRequestParams
                    {
                        ProtocolVersion = requestProtocol,
                        Capabilities = _options.Capabilities ?? new ClientCapabilities(),
                        ClientInfo = _options.ClientInfo ?? DefaultImplementation,
                    },
                    McpJsonUtilities.JsonContext.Default.InitializeRequestParams,
                    McpJsonUtilities.JsonContext.Default.InitializeResult,
                    cancellationToken: initializationCts.Token).ConfigureAwait(false);

                // Store server information
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    LogServerCapabilitiesReceived(_endpointName,
                        capabilities: JsonSerializer.Serialize(initializeResponse.Capabilities, McpJsonUtilities.JsonContext.Default.ServerCapabilities),
                        serverInfo: JsonSerializer.Serialize(initializeResponse.ServerInfo, McpJsonUtilities.JsonContext.Default.Implementation));
                }

                _serverCapabilities = initializeResponse.Capabilities;
                _serverInfo = initializeResponse.ServerInfo;
                _serverInstructions = initializeResponse.Instructions;

                // Validate protocol version
                bool isResponseProtocolValid =
                    _options.ProtocolVersion is { } optionsProtocol ? optionsProtocol == initializeResponse.ProtocolVersion :
                    McpSessionHandler.SupportedProtocolVersions.Contains(initializeResponse.ProtocolVersion);
                if (!isResponseProtocolValid)
                {
                    LogServerProtocolVersionMismatch(_endpointName, requestProtocol, initializeResponse.ProtocolVersion);
                    throw new McpException($"Server protocol version mismatch. Expected {requestProtocol}, got {initializeResponse.ProtocolVersion}");
                }

                _negotiatedProtocolVersion = initializeResponse.ProtocolVersion;

                // Update session handler with the negotiated protocol version for telemetry
                _sessionHandler.NegotiatedProtocolVersion = _negotiatedProtocolVersion;

                // Send initialized notification
                await this.SendNotificationAsync(
                    NotificationMethods.InitializedNotification,
                    new InitializedNotificationParams(),
                    McpJsonUtilities.JsonContext.Default.InitializedNotificationParams,
                    cancellationToken: initializationCts.Token).ConfigureAwait(false);

            }
            catch (OperationCanceledException oce) when (initializationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                LogClientInitializationTimeout(_endpointName);
                throw new TimeoutException("Initialization timed out", oce);
            }
        }
        catch (Exception e)
        {
            LogClientInitializationError(_endpointName, e);
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }

        LogClientConnected(_endpointName);
    }

    /// <summary>
    /// Configures the client to use an already initialized session without performing the handshake.
    /// </summary>
    /// <param name="resumeOptions">The metadata captured from the previous session that should be applied to the resumed client.</param>
    internal void ResumeSession(ResumeClientSessionOptions resumeOptions)
    {
        Throw.IfNull(resumeOptions);
        Throw.IfNull(resumeOptions.ServerCapabilities);
        Throw.IfNull(resumeOptions.ServerInfo);

        _ = _sessionHandler.ProcessMessagesAsync(CancellationToken.None);

        _serverCapabilities = resumeOptions.ServerCapabilities;
        _serverInfo = resumeOptions.ServerInfo;
        _serverInstructions = resumeOptions.ServerInstructions;
        _negotiatedProtocolVersion = resumeOptions.NegotiatedProtocolVersion
            ?? _options.ProtocolVersion
            ?? McpSessionHandler.LatestProtocolVersion;

        // Update session handler with the negotiated protocol version for telemetry
        _sessionHandler.NegotiatedProtocolVersion = _negotiatedProtocolVersion;

        LogClientSessionResumed(_endpointName);
    }

    /// <inheritdoc/>
    public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        => _sessionHandler.SendRequestAsync(request, cancellationToken);

    /// <inheritdoc/>
    public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        => _sessionHandler.SendMessageAsync(message, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
        => _sessionHandler.RegisterNotificationHandler(method, handler);

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        using var _ = await _disposeLock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _taskCancellationTokenProvider?.Dispose();
        await _sessionHandler.DisposeAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} client received server '{ServerInfo}' capabilities: '{Capabilities}'.")]
    private partial void LogServerCapabilitiesReceived(string endpointName, string capabilities, string serverInfo);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} client initialization error.")]
    private partial void LogClientInitializationError(string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} client initialization timed out.")]
    private partial void LogClientInitializationTimeout(string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} client protocol version mismatch with server. Expected '{Expected}', received '{Received}'.")]
    private partial void LogServerProtocolVersionMismatch(string endpointName, string expected, string received);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} client created and connected.")]
    private partial void LogClientConnected(string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} client resumed existing session.")]
    private partial void LogClientSessionResumed(string endpointName);

}
