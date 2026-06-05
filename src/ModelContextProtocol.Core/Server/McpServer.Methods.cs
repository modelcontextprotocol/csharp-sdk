using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) server that connects to and communicates with an MCP client.
/// </summary>
public abstract partial class McpServer : McpSession
{
    /// <summary>
    /// Caches request schemas for elicitation requests based on the type and serializer options.
    /// </summary>
    private static readonly ConditionalWeakTable<JsonSerializerOptions, ConcurrentDictionary<Type, ElicitRequestParams.RequestSchema>> s_elicitResultSchemaCache = new();

    private static Dictionary<string, HashSet<string>>? s_elicitAllowedProperties = null;

    /// <summary>
    /// Creates a new instance of an <see cref="McpServer"/>.
    /// </summary>
    /// <param name="transport">The transport to use for the server representing an already-established MCP session.</param>
    /// <param name="serverOptions">Configuration options for this server, including capabilities. </param>
    /// <param name="loggerFactory">Logger factory to use for logging. If null, logging will be disabled.</param>
    /// <param name="serviceProvider">Optional service provider to create new instances of tools and other dependencies.</param>
    /// <returns>An <see cref="McpServer"/> instance that should be disposed when no longer needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> or <paramref name="serverOptions"/> is <see langword="null"/>.</exception>
    public static McpServer Create(
        ITransport transport,
        McpServerOptions serverOptions,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null)
    {
        Throw.IfNull(transport);
        Throw.IfNull(serverOptions);

        return new McpServerImpl(transport, serverOptions, loggerFactory, serviceProvider);
    }

    /// <summary>
    /// Requests to sample an LLM via the client using the specified request parameters.
    /// </summary>
    /// <param name="requestParams">The parameters for the sampling request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the sampling result from the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// When called during task-augmented tool execution, this method automatically updates the task
    /// status to <see cref="McpTaskStatus.InputRequired"/> while waiting for the client response,
    /// then returns to <see cref="McpTaskStatus.Working"/> when the response is received.
    /// </remarks>
    public async ValueTask<CreateMessageResult> SampleAsync(
        CreateMessageRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);
        ThrowIfSamplingUnsupported();

        return await SendRequestWithTaskStatusTrackingAsync(
            RequestMethods.SamplingCreateMessage,
            requestParams,
            McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
            McpJsonUtilities.JsonContext.Default.CreateMessageResult,
            "Waiting for sampling response",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests to sample an LLM via the client as a task, allowing the server to poll for completion.
    /// </summary>
    /// <param name="requestParams">The parameters for the sampling request.</param>
    /// <param name="taskMetadata">The task metadata specifying TTL and other task-related options.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>An <see cref="McpTask"/> representing the created task on the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> or <paramref name="taskMetadata"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support sampling or task-augmented sampling.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// Use <see cref="GetTaskAsync"/> to poll for task status and <see cref="GetTaskResultAsync{TResult}"/>
    /// (with <see cref="CreateMessageResult"/>) to retrieve the final result when the task completes.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public async ValueTask<McpTask> SampleAsTaskAsync(
        CreateMessageRequestParams requestParams,
        McpTaskMetadata taskMetadata,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);
        Throw.IfNull(taskMetadata);
        ThrowIfSamplingUnsupported();
        ThrowIfTasksUnsupportedForSampling();

        // Set the task metadata on the request
        requestParams.Task = taskMetadata;

        var result = await SendRequestAsync(
            RequestMethods.SamplingCreateMessage,
            requestParams,
            McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
            McpJsonUtilities.JsonContext.Default.CreateTaskResult,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Task;
    }

    /// <summary>
    /// Requests to sample an LLM via the client using the provided chat messages and options.
    /// </summary>
    /// <param name="messages">The messages to send as part of the request.</param>
    /// <param name="chatOptions">The options to use for the request, including model parameters and constraints.</param>
    /// <param name="serializerOptions">The <see cref="JsonSerializerOptions"/> to use for serializing user-provided objects. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the chat response from the model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    public async Task<ChatResponse> SampleAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = default, JsonSerializerOptions? serializerOptions = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(messages);

        serializerOptions ??= McpJsonUtilities.DefaultOptions;

        StringBuilder? systemPrompt = null;

        if (chatOptions?.Instructions is { } instructions)
        {
            (systemPrompt ??= new()).Append(instructions);
        }

        List<SamplingMessage> samplingMessages = [];
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (systemPrompt is null)
                {
                    systemPrompt = new();
                }
                else
                {
                    systemPrompt.AppendLine();
                }

                systemPrompt.Append(message.Text);
                continue;
            }

            Role role = message.Role == ChatRole.Assistant ? Role.Assistant : Role.User;

            // Group all content blocks from this message into a single SamplingMessage
            List<ContentBlock> contentBlocks = [];
            foreach (var content in message.Contents)
            {
                if (content.ToContentBlock() is { } contentBlock)
                {
                    contentBlocks.Add(contentBlock);
                }
            }

            if (contentBlocks.Count > 0)
            {
                samplingMessages.Add(new()
                {
                    Role = role,
                    Content = contentBlocks,
                });
            }
        }

        ModelPreferences? modelPreferences = null;
        if (chatOptions?.ModelId is { } modelId)
        {
            modelPreferences = new() { Hints = [new() { Name = modelId }] };
        }

        IList<Tool>? tools = null;
        if (chatOptions?.Tools is { Count: > 0 })
        {
            foreach (var tool in chatOptions.Tools)
            {
                if (tool is AIFunctionDeclaration af)
                {
                    (tools ??= []).Add(new()
                    {
                        Name = af.Name,
                        Description = af.Description,
                        InputSchema = af.JsonSchema,
                        Meta = af.AdditionalProperties.ToJsonObject(serializerOptions),
                    });
                }
            }
        }

        ToolChoice? toolChoice = chatOptions?.ToolMode switch
        {
            NoneChatToolMode => new() { Mode = ToolChoice.ModeNone },
            AutoChatToolMode => new() { Mode = ToolChoice.ModeAuto },
            RequiredChatToolMode => new() { Mode = ToolChoice.ModeRequired },
            _ => null,
        };

        var result = await SampleAsync(new CreateMessageRequestParams
        {
            MaxTokens = chatOptions?.MaxOutputTokens ?? ServerOptions.MaxSamplingOutputTokens,
            Messages = samplingMessages,
            ModelPreferences = modelPreferences,
            StopSequences = chatOptions?.StopSequences?.ToArray(),
            SystemPrompt = systemPrompt?.ToString(),
            Temperature = chatOptions?.Temperature,
            ToolChoice = toolChoice,
            Tools = tools,
            Meta = chatOptions?.AdditionalProperties?.ToJsonObject(serializerOptions),
        }, cancellationToken).ConfigureAwait(false);

        List<AIContent> responseContents = [];
        foreach (var block in result.Content)
        {
            if (block.ToAIContent(serializerOptions) is { } content)
            {
                responseContents.Add(content);
            }
        }

        return new(new ChatMessage(result.Role is Role.User ? ChatRole.User : ChatRole.Assistant, responseContents))
        {
            CreatedAt = DateTimeOffset.UtcNow,
            FinishReason = result.StopReason switch
            {
                CreateMessageResult.StopReasonEndTurn => ChatFinishReason.Stop,
                CreateMessageResult.StopReasonMaxTokens => ChatFinishReason.Length,
                CreateMessageResult.StopReasonStopSequence => ChatFinishReason.Stop,
                CreateMessageResult.StopReasonToolUse => ChatFinishReason.ToolCalls,
                _ => null,
            },
            ModelId = result.Model,
        };
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> wrapper that can be used to send sampling requests to the client.
    /// </summary>
    /// <param name="serializerOptions">The <see cref="JsonSerializerOptions"/> to use for serialization. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <returns>The <see cref="IChatClient"/> that can be used to issue sampling requests to the client.</returns>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    public IChatClient AsSamplingChatClient(JsonSerializerOptions? serializerOptions = null)
    {
        ThrowIfSamplingUnsupported();

        return new SamplingChatClient(this, serializerOptions ?? McpJsonUtilities.DefaultOptions);
    }

    /// <summary>Gets an <see cref="ILogger"/> on which logged messages will be sent as notifications to the client.</summary>
    /// <returns>An <see cref="ILogger"/> that can be used to log to the client.</returns>
    public ILoggerProvider AsClientLoggerProvider() => 
        new ClientLoggerProvider(this);

    /// <summary>
    /// Requests the client to list the roots it exposes.
    /// </summary>
    /// <param name="requestParams">The parameters for the list roots request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the list of roots exposed by the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support roots.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    public ValueTask<ListRootsResult> RequestRootsAsync(
        ListRootsRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);
        ThrowIfRootsUnsupported();

        return SendRequestAsync(
            RequestMethods.RootsList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListRootsResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests additional information from the user via the client, allowing the server to elicit structured data.
    /// </summary>
    /// <param name="requestParams">The parameters for the elicitation request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the elicitation result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support elicitation.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// When called during task-augmented tool execution, this method automatically updates the task
    /// status to <see cref="McpTaskStatus.InputRequired"/> while waiting for user input,
    /// then returns to <see cref="McpTaskStatus.Working"/> when the response is received.
    /// </remarks>
    public async ValueTask<ElicitResult> ElicitAsync(
        ElicitRequestParams requestParams, 
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);
        ThrowIfElicitationUnsupported(requestParams);

        var result = await SendRequestWithTaskStatusTrackingAsync(
            RequestMethods.ElicitationCreate,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
            McpJsonUtilities.JsonContext.Default.ElicitResult,
            "Waiting for user input",
            cancellationToken).ConfigureAwait(false);

        return ElicitResult.WithDefaults(requestParams, result);
    }

    /// <summary>
    /// Requests additional information from the user via the client as a task, allowing the server to poll for completion.
    /// </summary>
    /// <param name="requestParams">The parameters for the elicitation request.</param>
    /// <param name="taskMetadata">The task metadata specifying TTL and other task-related options.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>An <see cref="McpTask"/> representing the created task on the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> or <paramref name="taskMetadata"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support elicitation or task-augmented elicitation.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// Use <see cref="GetTaskAsync"/> to poll for task status and <see cref="GetTaskResultAsync{TResult}"/>
    /// (with <see cref="ElicitResult"/>) to retrieve the final result when the task completes.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public async ValueTask<McpTask> ElicitAsTaskAsync(
        ElicitRequestParams requestParams,
        McpTaskMetadata taskMetadata,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);
        Throw.IfNull(taskMetadata);
        ThrowIfElicitationUnsupported(requestParams);
        ThrowIfTasksUnsupportedForElicitation();

        // Set the task metadata on the request
        requestParams.Task = taskMetadata;

        var result = await SendRequestAsync(
            RequestMethods.ElicitationCreate,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
            McpJsonUtilities.JsonContext.Default.CreateTaskResult,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Task;
    }

    /// <summary>
    /// Retrieves the current state of a specific task from the client.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to retrieve.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The current state of the task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="taskId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="InvalidOperationException">The client does not support tasks.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public async ValueTask<McpTask> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(taskId);
        ThrowIfTasksUnsupported();

        var result = await SendRequestAsync(
            RequestMethods.TasksGet,
            new GetTaskRequestParams { TaskId = taskId },
            McpJsonUtilities.JsonContext.Default.GetTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.GetTaskResult,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Convert GetTaskResult to McpTask
        return new McpTask
        {
            TaskId = result.TaskId,
            Status = result.Status,
            StatusMessage = result.StatusMessage,
            CreatedAt = result.CreatedAt,
            LastUpdatedAt = result.LastUpdatedAt,
            TimeToLive = result.TimeToLive,
            PollInterval = result.PollInterval
        };
    }

    /// <summary>
    /// Retrieves the result of a completed task from the client, blocking until the task reaches a terminal state.
    /// </summary>
    /// <typeparam name="TResult">The type to deserialize the task result into.</typeparam>
    /// <param name="taskId">The unique identifier of the task whose result to retrieve.</param>
    /// <param name="jsonSerializerOptions">Optional serializer options for deserializing the result.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the task, deserialized into type <typeparamref name="TResult"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="taskId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="InvalidOperationException">The client does not support tasks.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This method sends a tasks/result request to the client, which will block until the task completes if it hasn't already.
    /// The client handles all polling logic internally.
    /// </para>
    /// <para>
    /// For sampling tasks, use <see cref="CreateMessageResult"/> as <typeparamref name="TResult"/>.
    /// For elicitation tasks, use <see cref="ElicitResult"/> as <typeparamref name="TResult"/>.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public async ValueTask<TResult?> GetTaskResultAsync<TResult>(
        string taskId,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(taskId);
        ThrowIfTasksUnsupported();

        var result = await SendRequestAsync(
            RequestMethods.TasksResult,
            new GetTaskPayloadRequestParams { TaskId = taskId },
            McpJsonUtilities.JsonContext.Default.GetTaskPayloadRequestParams,
            McpJsonUtilities.JsonContext.Default.JsonElement,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var serializerOptions = jsonSerializerOptions ?? McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        var typeInfo = serializerOptions.GetTypeInfo<TResult>();
        return result.Deserialize(typeInfo);
    }

    /// <summary>
    /// Retrieves a list of all tasks from the client.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all tasks.</returns>
    /// <exception cref="InvalidOperationException">The client does not support tasks or task listing.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public async ValueTask<IList<McpTask>> ListTasksAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfTasksUnsupported();
        ThrowIfTaskListingUnsupported();

        List<McpTask>? tasks = null;
        ListTasksRequestParams requestParams = new();
        do
        {
            var taskResults = await ListTasksAsync(requestParams, cancellationToken).ConfigureAwait(false);
            if (tasks is null)
            {
                tasks = new List<McpTask>(taskResults.Tasks.Count);
            }

            foreach (var mcpTask in taskResults.Tasks)
            {
                tasks.Add(mcpTask);
            }

            requestParams.Cursor = taskResults.NextCursor;
        }
        while (requestParams.Cursor is not null);

        return tasks;
    }

    /// <summary>
    /// Retrieves a list of tasks from the client.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request as provided by the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support tasks or task listing.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// The <see cref="ListTasksAsync(CancellationToken)"/> overload retrieves all tasks by automatically handling pagination.
    /// This overload works with the lower-level <see cref="ListTasksRequestParams"/> and <see cref="ListTasksResult"/>, returning the raw result from the client.
    /// Any pagination needs to be managed by the caller.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public ValueTask<ListTasksResult> ListTasksAsync(
        ListTasksRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);
        ThrowIfTasksUnsupported();
        ThrowIfTaskListingUnsupported();

        return SendRequestAsync(
            RequestMethods.TasksList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListTasksRequestParams,
            McpJsonUtilities.JsonContext.Default.ListTasksResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cancels a running task on the client.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to cancel.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The updated state of the task after cancellation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="taskId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="InvalidOperationException">The client does not support tasks or task cancellation.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// Cancelling a task requests that the client stop execution. The client may not immediately cancel the task,
    /// and may choose to allow the task to complete if it's close to finishing.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public async ValueTask<McpTask> CancelTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(taskId);
        ThrowIfTasksUnsupported();
        ThrowIfTaskCancellationUnsupported();

        var result = await SendRequestAsync(
            RequestMethods.TasksCancel,
            new CancelMcpTaskRequestParams { TaskId = taskId },
            McpJsonUtilities.JsonContext.Default.CancelMcpTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.CancelMcpTaskResult,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Convert CancelMcpTaskResult to McpTask
        return new McpTask
        {
            TaskId = result.TaskId,
            Status = result.Status,
            StatusMessage = result.StatusMessage,
            CreatedAt = result.CreatedAt,
            LastUpdatedAt = result.LastUpdatedAt,
            TimeToLive = result.TimeToLive,
            PollInterval = result.PollInterval
        };
    }

    /// <summary>
    /// Polls a task on the client until it reaches a terminal state.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to poll.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task in its terminal state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="taskId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="InvalidOperationException">The client does not support tasks.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This method repeatedly calls <see cref="GetTaskAsync"/> until the task reaches a terminal status.
    /// It respects the <see cref="McpTask.PollInterval"/> returned by the client to determine how long
    /// to wait between polling attempts.
    /// </para>
    /// <para>
    /// For retrieving the actual result of a completed task, use <see cref="GetTaskResultAsync{TResult}"/>
    /// or <see cref="WaitForTaskResultAsync{TResult}"/>.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public async ValueTask<McpTask> PollTaskUntilCompleteAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(taskId);

        McpTask task;
        do
        {
            task = await GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);

            // If task is in a terminal state, we're done
            if (task.Status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled)
            {
                break;
            }

            // Wait for the poll interval before checking again (default to 1 second)
            var pollInterval = task.PollInterval ?? TimeSpan.FromSeconds(1);
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
        while (true);

        return task;
    }

    /// <summary>
    /// Waits for a task on the client to complete and retrieves its result.
    /// </summary>
    /// <typeparam name="TResult">The type to deserialize the task result into.</typeparam>
    /// <param name="taskId">The unique identifier of the task whose result to retrieve.</param>
    /// <param name="jsonSerializerOptions">Optional serializer options for deserializing the result.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A tuple containing the final task state and its result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="taskId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="InvalidOperationException">The client does not support tasks.</exception>
    /// <exception cref="McpException">The task failed or was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// This method combines <see cref="PollTaskUntilCompleteAsync"/> and <see cref="GetTaskResultAsync{TResult}"/>
    /// to provide a convenient way to wait for a task to complete and retrieve its result in a single call.
    /// </para>
    /// <para>
    /// If the task completes with a status of <see cref="McpTaskStatus.Failed"/> or <see cref="McpTaskStatus.Cancelled"/>,
    /// an <see cref="McpException"/> is thrown.
    /// </para>
    /// <para>
    /// For sampling tasks, use <see cref="CreateMessageResult"/> as <typeparamref name="TResult"/>.
    /// For elicitation tasks, use <see cref="ElicitResult"/> as <typeparamref name="TResult"/>.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public async ValueTask<(McpTask Task, TResult? Result)> WaitForTaskResultAsync<TResult>(
        string taskId,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(taskId);

        // Poll until task reaches terminal state
        var task = await PollTaskUntilCompleteAsync(taskId, cancellationToken).ConfigureAwait(false);

        // Check for failure or cancellation
        if (task.Status == McpTaskStatus.Failed)
        {
            throw new McpException($"Task '{taskId}' failed: {task.StatusMessage ?? "Unknown error"}");
        }

        if (task.Status == McpTaskStatus.Cancelled)
        {
            throw new McpException($"Task '{taskId}' was cancelled");
        }

        // Retrieve the result
        var result = await GetTaskResultAsync<TResult>(taskId, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);

        return (task, result);
    }

    /// <summary>
    /// Requests additional information from the user via the client, constructing a request schema from the
    /// public serializable properties of <typeparamref name="T"/> and deserializing the response into <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type describing the expected input shape. Only primitive members are supported (string, number, boolean, enum).</typeparam>
    /// <param name="message">The message to present to the user.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>An <see cref="ElicitResult{T}"/> with the user's response, if accepted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="InvalidOperationException">The client does not support elicitation.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// Elicitation uses a constrained subset of JSON Schema and only supports strings, numbers/integers, booleans and string enums.
    /// Unsupported member types are ignored when constructing the schema.
    /// </remarks>
    public async ValueTask<ElicitResult<T>> ElicitAsync<T>(
        string message,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(message);

        var serializerOptions = options?.JsonSerializerOptions ?? McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        var dict = s_elicitResultSchemaCache.GetValue(serializerOptions, _ => new());

        var schema = dict.GetOrAdd(typeof(T),
#if NET
            static (t, s) => BuildRequestSchema(t, s), serializerOptions);
#else
            type => BuildRequestSchema(type, serializerOptions));
#endif

        var request = new ElicitRequestParams
        {
            Message = message,
            RequestedSchema = schema,
            Meta = options?.GetMetaForRequest(),
        };

        ThrowIfElicitationUnsupported(request);

        var raw = await ElicitAsync(request, cancellationToken).ConfigureAwait(false);

        if (!raw.IsAccepted || raw.Content is null)
        {
            return new ElicitResult<T> { Action = raw.Action, Content = default };
        }

        JsonObject obj = [];
        foreach (var kvp in raw.Content)
        {
            obj[kvp.Key] = JsonNode.Parse(kvp.Value.GetRawText());
        }

        T? typed = JsonSerializer.Deserialize(obj, serializerOptions.GetTypeInfo<T>());
        return new ElicitResult<T> { Action = raw.Action, Content = typed };
    }

    /// <summary>
    /// Builds a request schema for elicitation based on the public serializable properties of <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The type of the schema being built.</param>
    /// <param name="serializerOptions">The serializer options to use.</param>
    /// <returns>The built request schema.</returns>
    /// <exception cref="McpProtocolException"></exception>
    private static ElicitRequestParams.RequestSchema BuildRequestSchema(Type type, JsonSerializerOptions serializerOptions)
    {
        var schema = new ElicitRequestParams.RequestSchema();
        var props = schema.Properties;

        JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(type);

        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            throw new McpProtocolException($"Type '{type.FullName}' is not supported for elicitation requests.");
        }

        foreach (JsonPropertyInfo pi in typeInfo.Properties)
        {
            var def = CreatePrimitiveSchema(pi.PropertyType, serializerOptions);
            props[pi.Name] = def;
        }

        return schema;
    }

    /// <summary>
    /// Creates a primitive schema definition for the specified type, if supported.
    /// </summary>
    /// <param name="type">The type to create the schema for.</param>
    /// <param name="serializerOptions">The serializer options to use.</param>
    /// <returns>The created primitive schema definition.</returns>
    /// <exception cref="McpProtocolException">The type is not supported.</exception>
    private static ElicitRequestParams.PrimitiveSchemaDefinition CreatePrimitiveSchema(Type type, JsonSerializerOptions serializerOptions)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            throw new McpProtocolException($"Type '{type.FullName}' is not a supported property type for elicitation requests. Nullable types are not supported.");
        }

        var typeInfo = serializerOptions.GetTypeInfo(type);

        if (typeInfo.Kind != JsonTypeInfoKind.None)
        {
            throw new McpProtocolException($"Type '{type.FullName}' is not a supported property type for elicitation requests.");
        }

        var jsonElement = AIJsonUtilities.CreateJsonSchema(type, serializerOptions: serializerOptions);

        if (!TryValidateElicitationPrimitiveSchema(jsonElement, type, out var error))
        {
            throw new McpProtocolException(error);
        }

        return
            jsonElement.Deserialize(McpJsonUtilities.JsonContext.Default.PrimitiveSchemaDefinition) ??
            throw new McpProtocolException($"Type '{type.FullName}' is not a supported property type for elicitation requests.");
    }

    /// <summary>
    /// Validate the produced schema strictly to the subset we support. We only accept an object schema
    /// with a supported primitive type keyword and no additional unsupported keywords.Reject things like
    /// {}, 'true', or schemas that include unrelated keywords(e.g.items, properties, patternProperties, etc.).
    /// </summary>
    /// <param name="schema">The schema to validate.</param>
    /// <param name="type">The type of the schema being validated, just for reporting errors.</param>
    /// <param name="error">The error message, if validation fails.</param>
    /// <returns></returns>
    private static bool TryValidateElicitationPrimitiveSchema(JsonElement schema, Type type,
        [NotNullWhen(false)] out string? error)
    {
        if (schema.ValueKind is not JsonValueKind.Object)
        {
            error = $"Schema generated for type '{type.FullName}' is invalid: expected an object schema.";
            return false;
        }

        if (!schema.TryGetProperty("type", out JsonElement typeProperty)
            || typeProperty.ValueKind is not JsonValueKind.String)
        {
            error = $"Schema generated for type '{type.FullName}' is invalid: missing or invalid 'type' keyword.";
            return false;
        }

        var typeKeyword = typeProperty.GetString();

        if (string.IsNullOrEmpty(typeKeyword))
        {
            error = $"Schema generated for type '{type.FullName}' is invalid: empty 'type' value.";
            return false;
        }

        if (typeKeyword is not ("string" or "number" or "integer" or "boolean"))
        {
            error = $"Schema generated for type '{type.FullName}' is invalid: unsupported primitive type '{typeKeyword}'.";
            return false;
        }

        s_elicitAllowedProperties ??= new()
        {
            ["string"] = ["type", "title", "description", "minLength", "maxLength", "format", "enum", "enumNames"],
            ["number"] = ["type", "title", "description", "minimum", "maximum"],
            ["integer"] = ["type", "title", "description", "minimum", "maximum"],
            ["boolean"] = ["type", "title", "description", "default"]
        };

        var allowed = s_elicitAllowedProperties[typeKeyword];

        foreach (JsonProperty prop in schema.EnumerateObject())
        {
            if (!allowed.Contains(prop.Name))
            {
                error = $"The property '{type.FullName}.{prop.Name}' is not supported for elicitation.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void ThrowIfSamplingUnsupported()
    {
        if (ClientCapabilities?.Sampling is null)
        {
            if (ClientCapabilities is null)
            {
                throw new InvalidOperationException("Sampling is not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support sampling.");
        }
    }

    private void ThrowIfRootsUnsupported()
    {
        if (ClientCapabilities?.Roots is null)
        {
            if (ClientCapabilities is null)
            {
                throw new InvalidOperationException("Roots are not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support roots.");
        }
    }

    private void ThrowIfElicitationUnsupported(ElicitRequestParams request)
    {
        if (ClientCapabilities is null)
        {
            throw new InvalidOperationException("Elicitation is not supported in stateless mode.");
        }

        var elicitationCapability = ClientCapabilities.Elicitation;
        if (elicitationCapability is null)
        {
            throw new InvalidOperationException("Client does not support elicitation requests.");
        }

        if (string.Equals(request.Mode, "form", StringComparison.Ordinal))
        {
            if (request.RequestedSchema is null)
            {
                throw new ArgumentException("Form mode elicitation requests require a requested schema.");
            }

            if (elicitationCapability.Form is null)
            {
                throw new InvalidOperationException("Client does not support form mode elicitation requests.");
            }
        }
        else if (string.Equals(request.Mode, "url", StringComparison.Ordinal))
        {
            if (request.Url is null)
            {
                throw new ArgumentException("URL mode elicitation requests require a URL.");
            }

            if (request.ElicitationId is null)
            {
                throw new ArgumentException("URL mode elicitation requests require an elicitation ID.");
            }

            if (elicitationCapability.Url is null)
            {
                throw new InvalidOperationException("Client does not support URL mode elicitation requests.");
            }
        }
    }

    private void ThrowIfTasksUnsupportedForSampling()
    {
        if (ClientCapabilities?.Tasks?.Requests?.Sampling?.CreateMessage is null)
        {
            if (ClientCapabilities is null)
            {
                throw new InvalidOperationException("Task-augmented sampling is not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support task-augmented sampling requests.");
        }
    }

    private void ThrowIfTasksUnsupportedForElicitation()
    {
        if (ClientCapabilities?.Tasks?.Requests?.Elicitation?.Create is null)
        {
            if (ClientCapabilities is null)
            {
                throw new InvalidOperationException("Task-augmented elicitation is not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support task-augmented elicitation requests.");
        }
    }

    private void ThrowIfTasksUnsupported()
    {
        if (ClientCapabilities?.Tasks is null)
        {
            if (ClientCapabilities is null)
            {
                throw new InvalidOperationException("Tasks are not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support tasks.");
        }
    }

    private void ThrowIfTaskListingUnsupported()
    {
        if (ClientCapabilities?.Tasks?.List is null)
        {
            throw new InvalidOperationException("Client does not support task listing.");
        }
    }

    private void ThrowIfTaskCancellationUnsupported()
    {
        if (ClientCapabilities?.Tasks?.Cancel is null)
        {
            throw new InvalidOperationException("Client does not support task cancellation.");
        }
    }

    /// <summary>
    /// Sends a request to the client, automatically updating task status to InputRequired during
    /// the request when called within a task execution context.
    /// </summary>
    private async ValueTask<TResult> SendRequestWithTaskStatusTrackingAsync<TParams, TResult>(
        string method,
        TParams requestParams,
        JsonTypeInfo<TParams> paramsTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        string inputRequiredMessage,
        CancellationToken cancellationToken)
        where TParams : RequestParams
        where TResult : notnull
    {
        var taskContext = TaskExecutionContext.Current;
        
        // If we're not in a task execution context, just send the request normally
        if (taskContext is null)
        {
            return await SendRequestAsync(method, requestParams, paramsTypeInfo, resultTypeInfo, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // Update task status to InputRequired
        var inputRequiredTask = await taskContext.TaskStore.UpdateTaskStatusAsync(
            taskContext.TaskId,
            Protocol.McpTaskStatus.InputRequired,
            inputRequiredMessage,
            taskContext.SessionId,
            CancellationToken.None).ConfigureAwait(false);

        // Send notification if enabled
        if (taskContext.SendNotifications && taskContext.NotifyTaskStatusFunc is not null)
        {
            _ = taskContext.NotifyTaskStatusFunc(inputRequiredTask, CancellationToken.None);
        }

        try
        {
            // Send the actual request
            return await SendRequestAsync(method, requestParams, paramsTypeInfo, resultTypeInfo, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Update task status back to Working
            var workingTask = await taskContext.TaskStore.UpdateTaskStatusAsync(
                taskContext.TaskId,
                Protocol.McpTaskStatus.Working,
                null, // Clear status message
                taskContext.SessionId,
                CancellationToken.None).ConfigureAwait(false);

            // Send notification if enabled
            if (taskContext.SendNotifications && taskContext.NotifyTaskStatusFunc is not null)
            {
                _ = taskContext.NotifyTaskStatusFunc(workingTask, CancellationToken.None);
            }
        }
    }

    /// <summary>Provides an <see cref="IChatClient"/> implementation that's implemented via client sampling.</summary>
    private sealed class SamplingChatClient(McpServer server, JsonSerializerOptions serializerOptions) : IChatClient
    {
        private readonly McpServer _server = server;
        private readonly JsonSerializerOptions _serializerOptions = serializerOptions;

        /// <inheritdoc/>
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken cancellationToken = default) =>
            _server.SampleAsync(messages, chatOptions, _serializerOptions, cancellationToken);

        /// <inheritdoc/>
        async IAsyncEnumerable<ChatResponseUpdate> IChatClient.GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? chatOptions, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var response = await GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        /// <inheritdoc/>
        object? IChatClient.GetService(Type serviceType, object? serviceKey)
        {
            Throw.IfNull(serviceType);

            return
                serviceKey is not null ? null :
                serviceType.IsInstanceOfType(this) ? this :
                serviceType.IsInstanceOfType(_server) ? _server :
                null;
        }

        /// <inheritdoc/>
        void IDisposable.Dispose() { } // nop
    }

    /// <summary>
    /// Sends a task status notification to the connected client.
    /// </summary>
    /// <param name="task">The task whose status changed.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous notification operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The notification failed or the client returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This method sends an optional status notification to inform the client of task state changes.
    /// According to the MCP specification, receivers MAY send this notification but are not required to.
    /// Clients must not rely on receiving these notifications and should continue polling via tasks/get.
    /// </para>
    /// <para>
    /// The notification is sent using the standard <c>notifications/tasks/status</c> method and includes
    /// the full task state information.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public Task NotifyTaskStatusAsync(
        McpTask task,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(task);

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

        return SendNotificationAsync(
            NotificationMethods.TaskStatusNotification,
            notificationParams,
            McpJsonUtilities.JsonContext.Default.McpTaskStatusNotificationParams,
            cancellationToken);
    }

    /// <summary>
    /// Provides an <see cref="ILoggerProvider"/> implementation for creating loggers
    /// that send logging message notifications to the client for logged messages.
    /// </summary>
    private sealed class ClientLoggerProvider(McpServer server) : ILoggerProvider
    {
        private readonly McpServer _server = server;

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            Throw.IfNull(categoryName);

            return new ClientLogger(_server, categoryName);
        }

        /// <inheritdoc />
        void IDisposable.Dispose() { }

        private sealed class ClientLogger(McpServer server, string categoryName) : ILogger
        {
            private readonly McpServer _server = server;
            private readonly string _categoryName = categoryName;

            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) =>
                _server?.LoggingLevel is { } loggingLevel &&
                McpServerImpl.ToLoggingLevel(logLevel) >= loggingLevel;

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                Throw.IfNull(formatter);

                LogInternal(logLevel, formatter(state, exception));

                void LogInternal(LogLevel level, string message)
                {
                    _ = _server.SendNotificationAsync(NotificationMethods.LoggingMessageNotification, new LoggingMessageNotificationParams
                    {
                        Level = McpServerImpl.ToLoggingLevel(level),
                        Data = JsonSerializer.SerializeToElement(message, McpJsonUtilities.JsonContext.Default.String),
                        Logger = _categoryName,
                    });
                }
            }
        }
    }
}
