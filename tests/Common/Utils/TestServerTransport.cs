using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Utils;

public class TestServerTransport : ITransport
{
    private readonly Channel<JsonRpcMessage> _messageChannel;

    public bool IsConnected { get; set; }

    public ChannelReader<JsonRpcMessage> MessageReader => _messageChannel;

    public IList<JsonRpcMessage> SentMessages { get; } = [];

    public Action<JsonRpcMessage>? OnMessageSent { get; set; }

    public string? SessionId => null;

    public TestServerTransport()
    {
        _messageChannel = Channel.CreateUnbounded<JsonRpcMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        IsConnected = true;
    }

    public ValueTask DisposeAsync()
    {
        _messageChannel.Writer.TryComplete();
        IsConnected = false;
        return default;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        if (message is JsonRpcRequest request)
        {
            if (request.Method == RequestMethods.RootsList)
                await ListRootsAsync(request, cancellationToken);
            else if (request.Method == RequestMethods.SamplingCreateMessage)
                await SamplingAsync(request, cancellationToken);
            else if (request.Method == RequestMethods.ElicitationCreate)
                await ElicitAsync(request, cancellationToken);
            else if (request.Method == RequestMethods.TasksGet)
                await TasksGetAsync(request, cancellationToken);
            else if (request.Method == RequestMethods.TasksResult)
                await TasksResultAsync(request, cancellationToken);
            else if (request.Method == RequestMethods.TasksList)
                await TasksListAsync(request, cancellationToken);
            else if (request.Method == RequestMethods.TasksCancel)
                await TasksCancelAsync(request, cancellationToken);
            else
                await WriteMessageAsync(request, cancellationToken);
        }
        else if (message is JsonRpcNotification notification)
        {
            await WriteMessageAsync(notification, cancellationToken);
        }

        OnMessageSent?.Invoke(message);
    }

    private async Task ListRootsAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(new ListRootsResult
            {
                Roots = []
            }, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    private async Task SamplingAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        // Check if the request is task-augmented (has Task metadata)
        var requestParams = JsonSerializer.Deserialize<CreateMessageRequestParams>(request.Params, McpJsonUtilities.DefaultOptions);
        if (requestParams?.Task is not null && MockTask is not null)
        {
            // Return a task-augmented response
            await WriteMessageAsync(new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToNode(new CreateTaskResult { Task = MockTask }, McpJsonUtilities.DefaultOptions),
            }, cancellationToken);
        }
        else
        {
            // Return a normal sampling response
            await WriteMessageAsync(new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToNode(new CreateMessageResult { Content = [new TextContentBlock { Text = "" }], Model = "model" }, McpJsonUtilities.DefaultOptions),
            }, cancellationToken);
        }
    }

    private async Task ElicitAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        // Check if the request is task-augmented (has Task metadata)
        var requestParams = JsonSerializer.Deserialize<ElicitRequestParams>(request.Params, McpJsonUtilities.DefaultOptions);
        if (requestParams?.Task is not null && MockTask is not null)
        {
            // Return a task-augmented response
            await WriteMessageAsync(new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToNode(new CreateTaskResult { Task = MockTask }, McpJsonUtilities.DefaultOptions),
            }, cancellationToken);
        }
        else
        {
            // Return a normal elicitation response
            await WriteMessageAsync(new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToNode(new ElicitResult { Action = "decline" }, McpJsonUtilities.DefaultOptions),
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Gets or sets the task to return from tasks/get requests.
    /// </summary>
    public McpTask? MockTask { get; set; }

    /// <summary>
    /// Gets or sets the result to return from tasks/result requests.
    /// </summary>
    public object? MockTaskResult { get; set; }

    /// <summary>
    /// Gets or sets the list of tasks to return from tasks/list requests.
    /// </summary>
    public McpTask[]? MockTaskList { get; set; }

    private async Task TasksGetAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var task = MockTask ?? new McpTask
        {
            TaskId = "test-task-id",
            Status = McpTaskStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(new GetTaskResult
            {
                TaskId = task.TaskId,
                Status = task.Status,
                StatusMessage = task.StatusMessage,
                CreatedAt = task.CreatedAt,
                LastUpdatedAt = task.LastUpdatedAt,
                TimeToLive = task.TimeToLive,
                PollInterval = task.PollInterval
            }, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    private async Task TasksResultAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var result = MockTaskResult ?? new CreateMessageResult
        {
            Content = [new TextContentBlock { Text = "Task result" }],
            Model = "test-model"
        };

        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(result, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    private async Task TasksListAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var tasks = MockTaskList ?? [
            new McpTask
            {
                TaskId = "task-1",
                Status = McpTaskStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastUpdatedAt = DateTimeOffset.UtcNow,
            },
            new McpTask
            {
                TaskId = "task-2",
                Status = McpTaskStatus.Working,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                LastUpdatedAt = DateTimeOffset.UtcNow,
            }
        ];

        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(new ListTasksResult
            {
                Tasks = tasks,
            }, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    private async Task TasksCancelAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var task = MockTask ?? new McpTask
        {
            TaskId = "test-task-id",
            Status = McpTaskStatus.Cancelled,
            StatusMessage = "Task cancelled by request",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(new CancelMcpTaskResult
            {
                TaskId = task.TaskId,
                Status = McpTaskStatus.Cancelled,
                StatusMessage = task.StatusMessage ?? "Task cancelled",
                CreatedAt = task.CreatedAt,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                TimeToLive = task.TimeToLive,
                PollInterval = task.PollInterval
            }, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    private async Task WriteMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        await _messageChannel.Writer.WriteAsync(message, cancellationToken);
    }

    /// <summary>
    /// Sends a message from the client to the server (simulating client-to-server communication).
    /// </summary>
    public async Task SendClientMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        await _messageChannel.Writer.WriteAsync(message, cancellationToken);
    }
}
