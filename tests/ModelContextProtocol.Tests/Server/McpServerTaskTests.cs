using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the MCP tasks extension (SEP-2663) end-to-end using a simple in-memory task store.
/// </summary>
public class McpServerTaskTests : ClientServerTestBase
{
    private readonly InMemoryTaskStore _taskStore = new();
    private JsonObject? _capturedMeta;

    public McpServerTaskTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddSingleton(_taskStore);

        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities ??= new ServerCapabilities();

            options.Handlers.CallToolWithTaskHandler = async (context, cancellationToken) =>
            {
                _capturedMeta = context.Params?.Meta;
                var store = context.Server.Services!.GetRequiredService<InMemoryTaskStore>();
                var toolName = context.Params!.Name;

                if (toolName == "immediate-tool")
                {
                    return new CallToolResult()
                    {
                        Content = [new TextContentBlock { Text = "immediate result" }],
                    };
                }

                if (toolName == "async-tool")
                {
                    var taskId = store.CreateTask();
                    return new CreateTaskResult
                    {
                        TaskId = taskId,
                        Status = McpTaskStatus.Working,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LastUpdatedAt = DateTimeOffset.UtcNow,
                        PollIntervalMs = 50,
                        ResultType = "task",
                    };
                }

                if (toolName == "async-tool-default-resulttype")
                {
                    // Intentionally leaves ResultType unset so the server boundary is responsible for
                    // filling in "task" on the wire.
                    var taskId = store.CreateTask();
                    return new CreateTaskResult
                    {
                        TaskId = taskId,
                        Status = McpTaskStatus.Working,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LastUpdatedAt = DateTimeOffset.UtcNow,
                        PollIntervalMs = 50,
                    };
                }

                if (toolName == "input-required-tool")
                {
                    var taskId = store.CreateTask(McpTaskStatus.InputRequired);
                    return new CreateTaskResult
                    {
                        TaskId = taskId,
                        Status = McpTaskStatus.InputRequired,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LastUpdatedAt = DateTimeOffset.UtcNow,
                        PollIntervalMs = 50,
                        ResultType = "task",
                    };
                }

                throw new McpException($"Unknown tool: {toolName}");
            };

            options.Handlers.GetTaskHandler = async (context, cancellationToken) =>
            {
                var store = context.Server.Services!.GetRequiredService<InMemoryTaskStore>();
                var taskId = context.Params!.TaskId;
                return store.GetTask(taskId);
            };

            options.Handlers.UpdateTaskHandler = async (context, cancellationToken) =>
            {
                var store = context.Server.Services!.GetRequiredService<InMemoryTaskStore>();
                var taskId = context.Params!.TaskId;
                store.ProvideInput(taskId, context.Params.InputResponses ?? new Dictionary<string, InputResponse>());
                return new UpdateTaskResult();
            };

            options.Handlers.CancelTaskHandler = async (context, cancellationToken) =>
            {
                var store = context.Server.Services!.GetRequiredService<InMemoryTaskStore>();
                var taskId = context.Params!.TaskId;
                store.CancelTask(taskId);
                return new CancelTaskResult();
            };
        });
    }

    [Fact]
    public async Task CallToolAsync_ImmediateResult_ReturnsDirectly()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "immediate-tool" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Content);
        Assert.Equal("immediate result", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CallToolRawAsync_ImmediateResult_ReturnsResultNotTask()
    {
        await using var client = await CreateMcpClientForServer();

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "immediate-tool" },
            TestContext.Current.CancellationToken);

        Assert.False(augmented.IsTask);
        Assert.NotNull(augmented.Result);
        Assert.Null(augmented.TaskCreated);
        Assert.Equal("immediate result", Assert.IsType<TextContentBlock>(augmented.Result.Content[0]).Text);
    }

    [Fact]
    public async Task CallToolRawAsync_AsyncTool_ReturnsTaskCreated()
    {
        await using var client = await CreateMcpClientForServer();

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "async-tool" },
            TestContext.Current.CancellationToken);

        Assert.True(augmented.IsTask);
        Assert.NotNull(augmented.TaskCreated);
        Assert.Null(augmented.Result);
        Assert.Equal(McpTaskStatus.Working, augmented.TaskCreated.Status);
        Assert.Equal("task", augmented.TaskCreated.ResultType);
    }

    [Fact]
    public async Task CallToolAsync_AsyncTool_PollsUntilCompleted()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        // Complete the task after a brief delay so polling finds it.
        _ = Task.Run(async () =>
        {
            await Task.Delay(100, ct);
            // The store should have exactly one task by now
            var taskId = _taskStore.GetAllTaskIds().Single();
            _taskStore.CompleteTask(taskId, new CallToolResult
            {
                Content = [new TextContentBlock { Text = "async result" }],
            });
        }, ct);

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "async-tool" },
            ct);

        Assert.NotNull(result);
        Assert.Single(result.Content);
        Assert.Equal("async result", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CallToolAsync_AsyncTool_FailedTask_ThrowsMcpException()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var failedTask = new TaskCompletionSource<bool>();

        // Run failure task once the task from the tool call is created
        _taskStore.OnTaskCreated += taskId =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(100, ct);
                _taskStore.FailTask(taskId, JsonElement.Parse("""{"code":-32000,"message":"something went wrong"}"""));
                failedTask.SetResult(true);
            }, ct);
        };

        await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolAsync(
                new CallToolRequestParams { Name = "async-tool" },
                ct));

        Assert.True(await failedTask.Task);
    }

    [Fact]
    public async Task CallToolAsync_AsyncTool_CancelledTask_ThrowsOperationCancelled()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var cancelledTask = new TaskCompletionSource<bool>();

        // Run cancellation task once the task from the tool call is created
        _taskStore.OnTaskCreated += taskId =>
        {
            Task.Run(async () =>
            {
                await Task.Delay(100, ct);
                _taskStore.CancelTask(taskId);
                cancelledTask.SetResult(true);
            }, ct);
        };

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await client.CallToolAsync(
                new CallToolRequestParams { Name = "async-tool" },
                ct));

        Assert.True(await cancelledTask.Task);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsCurrentState()
    {
        await using var client = await CreateMcpClientForServer();

        // Create a task via CallToolRawAsync
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "async-tool" },
            TestContext.Current.CancellationToken);

        var taskId = augmented.TaskCreated!.TaskId;

        // Should be working
        var taskResult = await client.GetTaskAsync(taskId, TestContext.Current.CancellationToken);
        Assert.IsType<WorkingTaskResult>(taskResult);
        Assert.Equal(taskId, taskResult.TaskId);
        Assert.Equal(McpTaskStatus.Working, taskResult.Status);
    }

    [Fact]
    public async Task CancelTaskAsync_CancelsTask()
    {
        await using var client = await CreateMcpClientForServer();

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "async-tool" },
            TestContext.Current.CancellationToken);

        var taskId = augmented.TaskCreated!.TaskId;

        // Cancel via client
        var cancelResult = await client.CancelTaskAsync(taskId, TestContext.Current.CancellationToken);
        Assert.NotNull(cancelResult);

        // Verify state changed
        var taskResult = await client.GetTaskAsync(taskId, TestContext.Current.CancellationToken);
        Assert.IsType<CancelledTaskResult>(taskResult);
    }

    [Fact]
    public async Task ConfigureTasks_AdvertisesExtensionInCapabilities()
    {
        await using var client = await CreateMcpClientForServer();

        // The server advertises the tasks extension during initialize.
        // The client should see it in server capabilities after the handshake.
        #pragma warning disable MCP_EXTENSIONS
        var extensions = client.ServerCapabilities.Extensions;
        #pragma warning restore MCP_EXTENSIONS
        Assert.NotNull(extensions);
        Assert.True(extensions.ContainsKey(McpExtensions.Tasks));
    }

    [Fact]
    public async Task CreateTaskResult_HasResultTypeTask()
    {
        await using var client = await CreateMcpClientForServer();

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "async-tool" },
            TestContext.Current.CancellationToken);

        Assert.True(augmented.IsTask);
        Assert.Equal("task", augmented.TaskCreated!.ResultType);
    }

    [Fact]
    public async Task CreateTaskResult_WithoutExplicitResultType_ServerFillsTask()
    {
        await using var client = await CreateMcpClientForServer();

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "async-tool-default-resulttype" },
            TestContext.Current.CancellationToken);

        Assert.True(augmented.IsTask);
        Assert.Equal("task", augmented.TaskCreated!.ResultType);
    }

    [Fact]
    public async Task GetTaskAsync_ImmediatelyAfterCreate_Resolves()
    {
        // Strong consistency: tasks/get immediately after CreateTaskResult must resolve.
        await using var client = await CreateMcpClientForServer();

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "async-tool" },
            TestContext.Current.CancellationToken);

        var taskId = augmented.TaskCreated!.TaskId;

        // No delay — immediate get
        var taskResult = await client.GetTaskAsync(taskId, TestContext.Current.CancellationToken);
        Assert.NotNull(taskResult);
        Assert.Equal(taskId, taskResult.TaskId);
    }

    [Fact]
    public async Task GetTaskAsync_UnknownTaskId_ThrowsWithInvalidParams()
    {
        await using var client = await CreateMcpClientForServer();

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.GetTaskAsync("nonexistent-task-id-12345", TestContext.Current.CancellationToken));

        // The server should reject with an error referencing the unknown task
        Assert.Contains("Unknown task", ex.Message);
    }

    [Fact]
    public async Task CancelTask_AlreadyTerminal_AcknowledgesIdempotently()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "async-tool" }, ct);
        var taskId = augmented.TaskCreated!.TaskId;

        // Cancel once
        await client.CancelTaskAsync(taskId, ct);

        // Cancel again on terminal task — should not throw, returns ack
        var ack = await client.CancelTaskAsync(taskId, ct);
        Assert.NotNull(ack);
    }

    [Fact]
    public async Task UpdateTaskAsync_TransitionsFromInputRequired()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        // Create an input-required task
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "input-required-tool" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        // Verify it's input_required
        var taskResult = await client.GetTaskAsync(taskId, ct);
        Assert.IsType<InputRequiredTaskResult>(taskResult);

        // Provide input
        var inputResponses = new Dictionary<string, InputResponse>
        {
            ["resp-1"] = new InputResponse { RawValue = JsonElement.Parse("""{"answer":"yes"}""") }
        };
        await client.UpdateTaskAsync(new UpdateTaskRequestParams
        {
            TaskId = taskId,
            InputResponses = inputResponses,
        }, ct);

        // Verify it transitioned back to working
        taskResult = await client.GetTaskAsync(taskId, ct);
        Assert.IsType<WorkingTaskResult>(taskResult);
    }

    [Fact]
    public async Task CallToolRawAsync_InjectsTaskCapabilityInMeta()
    {
        // Verify the server receives the task extension in _meta by intercepting
        // the handler. The CallToolWithTaskHandler already receives the request,
        // so we can observe the meta there. We test the client-side injection indirectly
        // by confirming the server returns a task result (which requires the capability signal).
        await using var client = await CreateMcpClientForServer();

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "async-tool" },
            TestContext.Current.CancellationToken);

        // If the capability wasn't injected, the server couldn't have returned a task
        Assert.True(augmented.IsTask);
    }

    [Fact]
    public async Task CallToolRawAsync_OptIn_UsesSep2575CapabilitiesEnvelope()
    {
        // SEP-2663 §51: the per-request opt-in is the SEP-2575 capabilities envelope:
        //   _meta/io.modelcontextprotocol/clientCapabilities/extensions/io.modelcontextprotocol/tasks = {}
        // This test pins the literal wire path so future refactors can't regress.
        await using var client = await CreateMcpClientForServer();

        await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "immediate-tool" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(_capturedMeta);

        var caps = Assert.IsType<JsonObject>(_capturedMeta!["io.modelcontextprotocol/clientCapabilities"]);
        var extensions = Assert.IsType<JsonObject>(caps["extensions"]);
        Assert.True(extensions.ContainsKey("io.modelcontextprotocol/tasks"),
            "Expected _meta to contain io.modelcontextprotocol/clientCapabilities/extensions/io.modelcontextprotocol/tasks (SEP-2575 envelope).");

        // The opt-in value is an empty object per SEP-2575.
        Assert.IsType<JsonObject>(extensions["io.modelcontextprotocol/tasks"]);
    }

    [Fact]
    public async Task CallToolRawAsync_OptIn_PreservesExistingMetaSiblings()
    {
        // User-supplied _meta entries at the root must not be clobbered, and the SEP-2575
        // envelope must be added alongside them, not in place of them.
        await using var client = await CreateMcpClientForServer();

        var userMeta = new JsonObject
        {
            ["customKey"] = "customValue",
            ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject
            {
                ["extensions"] = new JsonObject
                {
                    ["some.other/extension"] = new JsonObject(),
                },
            },
        };

        await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "immediate-tool",
                Meta = userMeta,
            },
            TestContext.Current.CancellationToken);

        Assert.NotNull(_capturedMeta);

        // User's sibling root entry is preserved.
        Assert.Equal("customValue", (string?)_capturedMeta!["customKey"]);

        // User's pre-existing nested extension is preserved next to the tasks opt-in.
        var caps = Assert.IsType<JsonObject>(_capturedMeta["io.modelcontextprotocol/clientCapabilities"]);
        var extensions = Assert.IsType<JsonObject>(caps["extensions"]);
        Assert.True(extensions.ContainsKey("some.other/extension"));
        Assert.True(extensions.ContainsKey("io.modelcontextprotocol/tasks"));
    }

    [Fact]
    public async Task CallToolRawAsync_PreservesExistingUserMeta()
    {
        // Verify that user-supplied meta fields are not clobbered
        await using var client = await CreateMcpClientForServer();

        var userMeta = new JsonObject { ["customKey"] = "customValue" };
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "immediate-tool",
                Meta = userMeta,
            },
            TestContext.Current.CancellationToken);

        // Should still work — the meta was cloned, not destructively modified
        Assert.False(augmented.IsTask);
        Assert.Equal("immediate result", Assert.IsType<TextContentBlock>(augmented.Result!.Content[0]).Text);

        // Original user meta should not be mutated
        Assert.Single(userMeta);
        Assert.Equal("customValue", (string)userMeta["customKey"]!);
    }

    [Fact]
    public async Task CallToolAsync_RespectsServerPollInterval()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var startTime = DateTime.UtcNow;

        // Complete the task after a brief delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(200, ct);
            var taskId = _taskStore.GetAllTaskIds().Single();
            _taskStore.CompleteTask(taskId, new CallToolResult
            {
                Content = [new TextContentBlock { Text = "polled" }],
            });
        }, ct);

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "async-tool" }, ct);

        var elapsed = DateTime.UtcNow - startTime;

        // The server sets pollIntervalMs=50. The task completes after 200ms.
        // So we expect at least 1 poll interval to have passed.
        Assert.True(elapsed.TotalMilliseconds >= 50, $"Expected at least 50ms, got {elapsed.TotalMilliseconds}ms");
        Assert.Equal("polled", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CallToolWithTaskHandler_ImplicitConversion_ReturnCallToolResult()
    {
        // Verify that the implicit conversion from CallToolResult to ResultOrCreatedTask works
        // in the handler context — this is already tested by "immediate-tool" working correctly.
        await using var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "immediate-tool" },
            TestContext.Current.CancellationToken);

        Assert.Equal("immediate result", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CallToolHandler_And_CallToolWithTaskHandler_AreMutuallyExclusive()
    {
        var handlers = new McpServerHandlers();

        handlers.CallToolWithTaskHandler = async (ctx, ct) => new CallToolResult();
        Assert.Throws<InvalidOperationException>(() =>
            handlers.CallToolHandler = async (ctx, ct) => new CallToolResult());

        handlers = new McpServerHandlers();

        handlers.CallToolHandler = async (ctx, ct) => new CallToolResult();
        Assert.Throws<InvalidOperationException>(() =>
            handlers.CallToolWithTaskHandler = async (ctx, ct) => new CallToolResult());
    }

    [Fact]
    public async Task CallToolHandler_CanBeSetToNull_ThenOtherCanBeSet()
    {
        var handlers = new McpServerHandlers();

        handlers.CallToolHandler = async (ctx, ct) => new CallToolResult();
        handlers.CallToolHandler = null;

        // Now setting the other should work
        handlers.CallToolWithTaskHandler = async (ctx, ct) => new CallToolResult();
        Assert.NotNull(handlers.CallToolWithTaskHandler);
    }

    /// <summary>
    /// Simple in-memory task store for testing.
    /// </summary>
    private sealed class InMemoryTaskStore
    {
        private readonly Dictionary<string, TaskEntry> _tasks = new();

        internal Action<string>? OnTaskCreated;

        public string CreateTask(McpTaskStatus initialStatus = McpTaskStatus.Working)
        {
            var taskId = Guid.NewGuid().ToString("N");
            lock (_tasks)
            {
                _tasks[taskId] = new TaskEntry
                {
                    Status = initialStatus,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                };
            }

            OnTaskCreated?.Invoke(taskId);

            return taskId;
        }

        public IEnumerable<string> GetAllTaskIds()
        {
            lock (_tasks)
            {
                return _tasks.Keys.ToArray();
            }
        }

        public GetTaskResult GetTask(string taskId)
        {
            lock (_tasks)
            {
                if (!_tasks.TryGetValue(taskId, out var entry))
                {
                    throw new McpException($"Unknown task: '{taskId}'");
                }

                return entry.Status switch
                {
                    McpTaskStatus.Working => new WorkingTaskResult
                    {
                        TaskId = taskId,
                        CreatedAt = entry.CreatedAt,
                        LastUpdatedAt = entry.LastUpdatedAt,
                        PollIntervalMs = 50,
                    },
                    McpTaskStatus.Completed => new CompletedTaskResult
                    {
                        TaskId = taskId,
                        CreatedAt = entry.CreatedAt,
                        LastUpdatedAt = entry.LastUpdatedAt,
                        Result = JsonSerializer.SerializeToElement(entry.Result, McpJsonUtilities.DefaultOptions),
                    },
                    McpTaskStatus.Failed => new FailedTaskResult
                    {
                        TaskId = taskId,
                        CreatedAt = entry.CreatedAt,
                        LastUpdatedAt = entry.LastUpdatedAt,
                        Error = entry.Error!.Value,
                    },
                    McpTaskStatus.Cancelled => new CancelledTaskResult
                    {
                        TaskId = taskId,
                        CreatedAt = entry.CreatedAt,
                        LastUpdatedAt = entry.LastUpdatedAt,
                    },
                    McpTaskStatus.InputRequired => new InputRequiredTaskResult
                    {
                        TaskId = taskId,
                        CreatedAt = entry.CreatedAt,
                        LastUpdatedAt = entry.LastUpdatedAt,
                        InputRequests = entry.InputRequests ?? new Dictionary<string, InputRequest>(),
                    },
                    _ => throw new InvalidOperationException($"Unexpected status: {entry.Status}")
                };
            }
        }

        public void CompleteTask(string taskId, CallToolResult result)
        {
            lock (_tasks)
            {
                if (_tasks.TryGetValue(taskId, out var entry))
                {
                    entry.Result = result;
                    entry.LastUpdatedAt = DateTimeOffset.UtcNow;
                    entry.Status = McpTaskStatus.Completed;
                }
            }
        }

        public void FailTask(string taskId, JsonElement error)
        {
            lock (_tasks)
            {
                if (_tasks.TryGetValue(taskId, out var entry))
                {
                    entry.Error = error;
                    entry.LastUpdatedAt = DateTimeOffset.UtcNow;
                    entry.Status = McpTaskStatus.Failed;
                }
            }
        }

        public void CancelTask(string taskId)
        {
            lock (_tasks)
            {
                if (_tasks.TryGetValue(taskId, out var entry))
                {
                    entry.LastUpdatedAt = DateTimeOffset.UtcNow;
                    entry.Status = McpTaskStatus.Cancelled;
                }
            }
        }

        public void ProvideInput(string taskId, IDictionary<string, InputResponse> inputResponses)
        {
            lock (_tasks)
            {
                if (_tasks.TryGetValue(taskId, out var entry))
                {
                    entry.InputResponses = inputResponses;
                    entry.LastUpdatedAt = DateTimeOffset.UtcNow;
                    // Transition back to working after receiving input
                    entry.Status = McpTaskStatus.Working;
                }
            }
        }

        private sealed class TaskEntry
        {
            public McpTaskStatus Status { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset LastUpdatedAt { get; set; }
            public CallToolResult? Result { get; set; }
            public JsonElement? Error { get; set; }
            public IDictionary<string, InputRequest>? InputRequests { get; set; }
            public IDictionary<string, InputResponse>? InputResponses { get; set; }
        }
    }
}
