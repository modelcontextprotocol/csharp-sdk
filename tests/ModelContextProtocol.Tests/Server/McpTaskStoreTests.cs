using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the <see cref="IMcpTaskStore"/>-based auto-wiring of tools/call into tasks.
/// Verifies that setting <see cref="McpServerOptions.TaskStore"/> enables task support
/// for <see cref="McpServerTool"/>-based tools.
/// </summary>
public class McpTaskStoreTests : ClientServerTestBase
{
    public McpTaskStoreTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<TaskStoreTestTools>();

        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
            };
        });
    }

    [Fact]
    public async Task CallToolRawAsync_WithTaskCapability_ReturnsCreateTaskResult()
    {
        await using var client = await CreateMcpClientForServer();

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "slow-tool" },
            TestContext.Current.CancellationToken);

        // Because the client signals task support and a TaskStore is configured,
        // the server should wrap the tool execution in a task.
        Assert.True(augmented.IsTask);
        Assert.NotNull(augmented.TaskCreated);
        Assert.Equal(McpTaskStatus.Working, augmented.TaskCreated.Status);
    }

    [Fact]
    public async Task CallToolAsync_WithTaskStore_PollsToCompletion()
    {
        await using var client = await CreateMcpClientForServer();

        // CallToolAsync should poll until the background execution completes.
        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "slow-tool" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Content);
        Assert.Equal("slow result", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CallToolAsync_WithTaskStore_FastTool_StillCreatesTask()
    {
        await using var client = await CreateMcpClientForServer();

        // Even a fast tool should go through the task store when the client signals capability.
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "fast-tool" },
            TestContext.Current.CancellationToken);

        Assert.True(augmented.IsTask);
    }

    [Fact]
    public async Task GetTaskAsync_ViaStore_ReturnsCompletedResult()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "fast-tool" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        // The fast-tool returns immediately in the background, so poll briefly
        GetTaskResult? taskResult = null;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is CompletedTaskResult)
            {
                break;
            }
        }

        Assert.IsType<CompletedTaskResult>(taskResult);
    }

    [Fact]
    public async Task CancelTaskAsync_ViaStore_TransitionsToCancelled()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        // Create a slow task that won't complete on its own
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "slow-tool" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        // Cancel it
        await client.CancelTaskAsync(taskId, ct);

        // Verify state
        var taskResult = await client.GetTaskAsync(taskId, ct);
        Assert.IsType<CancelledTaskResult>(taskResult);
    }

    [Fact]
    public async Task GetTaskAsync_UnknownId_ThrowsWithInvalidParams()
    {
        await using var client = await CreateMcpClientForServer();

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.GetTaskAsync("nonexistent-id", TestContext.Current.CancellationToken));

        Assert.Contains("Unknown task", ex.Message);
    }

    [Fact]
    public async Task ToolExecution_Failure_StoresAsCompletedWithError()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "failing-tool" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        // Poll until completed (tool exceptions are wrapped as isError:true results)
        GetTaskResult? taskResult = null;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is CompletedTaskResult)
            {
                break;
            }
        }

        var completed = Assert.IsType<CompletedTaskResult>(taskResult);
        // The tool result has isError: true
        Assert.True(completed.Result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task McpProtocolException_FromTool_StoresAsFailedWithJsonRpcErrorShape()
    {
        // SEP-2663 §186: failed.error MUST be a JSON-RPC error object {code, message, data?}.
        // When a tool throws McpProtocolException, the task-store wrapper must serialize the error
        // payload with the exception's ErrorCode and Message preserved on the wire.
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "throws-mcp-protocol" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        GetTaskResult? taskResult = null;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is FailedTaskResult)
            {
                break;
            }
        }

        var failed = Assert.IsType<FailedTaskResult>(taskResult);

        // The error MUST be a JSON-RPC error object with at least 'code' and 'message'.
        Assert.Equal(JsonValueKind.Object, failed.Error.ValueKind);
        Assert.Equal((int)McpErrorCode.InvalidParams, failed.Error.GetProperty("code").GetInt32());
        Assert.Equal("custom-protocol-message", failed.Error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InputRequiredException_FromTool_FailsTaskWithActionableMessage()
    {
        // [McpServerTool] methods that throw InputRequiredException can't compose with the task-store
        // wrapper today: the taskId was already returned synchronously and there's no way to surface
        // InputRequiredResult retroactively. The wrapper must fail the task with a clear, actionable
        // message instead of leaking the raw exception through the generic catch.
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "mrtr-tool" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        GetTaskResult? taskResult = null;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is FailedTaskResult)
            {
                break;
            }
        }

        var failed = Assert.IsType<FailedTaskResult>(taskResult);
        Assert.Equal(JsonValueKind.Object, failed.Error.ValueKind);
        Assert.Equal((int)McpErrorCode.InvalidRequest, failed.Error.GetProperty("code").GetInt32());

        var message = failed.Error.GetProperty("message").GetString();
        Assert.NotNull(message);
        Assert.Contains("MRTR", message);
        Assert.Contains(nameof(McpServerHandlers.CallToolWithTaskHandler), message);
    }

    [Fact]
    public async Task ElicitTool_ViaTask_RedirectsThroughStore()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    // Client responds to the elicitation
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
                }
            }
        });
        var ct = TestContext.Current.CancellationToken;

        // CallToolAsync will poll and resolve input requests automatically.
        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "elicit-tool" }, ct);

        Assert.NotNull(result);
        Assert.Equal("accepted", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task SampleTool_ViaTask_RedirectsThroughStore()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (request, progress, ct) =>
                {
                    return new ValueTask<CreateMessageResult>(new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "sampled response" }],
                        Model = "test-model",
                    });
                }
            }
        });
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "sample-tool" }, ct);

        Assert.NotNull(result);
        Assert.Equal("sampled response", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task RootsTool_ViaTask_RedirectsThroughStore()
    {
        // Verifies that server-initiated roots/list calls issued from inside a [McpServerTool]
        // running under the task wrapper are redirected through the task store as input requests
        // (rather than being sent as direct JSON-RPC requests to the client).
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Roots = new RootsCapability(),
            },
            Handlers = new McpClientHandlers
            {
                RootsHandler = (request, ct) =>
                    new ValueTask<ListRootsResult>(new ListRootsResult
                    {
                        Roots = [new Root { Uri = "file:///workspace" }, new Root { Uri = "file:///other" }],
                    }),
            },
        });
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "roots-tool" }, ct);

        Assert.NotNull(result);
        Assert.Equal("file:///workspace,file:///other", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task SendTaskStatusNotificationAsync_FromTool_DeliversTypedNotificationE2E()
    {
        // E2E coverage for SendTaskStatusNotificationAsync: the tool emits a Working then a
        // Completed notification with a fixed test taskId, and the client receives them via
        // its notifications/tasks subscription, deserialized to the right concrete subtype.
        var notifications = Channel.CreateUnbounded<TaskStatusNotificationParams>();

        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        await using var registration = client.RegisterNotificationHandler(
            NotificationMethods.TaskStatusNotification,
            (notification, _) =>
            {
                var typed = JsonSerializer.Deserialize<TaskStatusNotificationParams>(
                    notification.Params,
                    McpJsonUtilities.DefaultOptions);
                if (typed is not null)
                {
                    notifications.Writer.TryWrite(typed);
                }

                return default;
            });

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "notifying-tool" }, ct);

        Assert.Equal("notified", Assert.IsType<TextContentBlock>(result.Content[0]).Text);

        // Read both notifications. The server emits them in strict order (each
        // SendTaskStatusNotificationAsync awaits the transport write before the next), but client-side
        // dispatch (McpSessionHandler.ProcessMessageAsync) is fire-and-forget per message, so user
        // handlers may observe them out of order. Reconstruct send order via the server-set
        // LastUpdatedAt timestamp.
        var first = await notifications.Reader.ReadAsync(ct);
        var second = await notifications.Reader.ReadAsync(ct);
        var ordered = new[] { first, second }.OrderBy(n => n.LastUpdatedAt).ToArray();

        var workingTyped = Assert.IsType<WorkingTaskNotificationParams>(ordered[0]);
        Assert.Equal("notify-test-task-id", workingTyped.TaskId);
        Assert.Equal(McpTaskStatus.Working, workingTyped.Status);

        var completedTyped = Assert.IsType<CompletedTaskNotificationParams>(ordered[1]);
        Assert.Equal("notify-test-task-id", completedTyped.TaskId);
        Assert.Equal(McpTaskStatus.Completed, completedTyped.Status);
        Assert.Equal("notify-result", completedTyped.Result.GetString());
    }

    [Fact]
    public async Task SendTaskStatusNotificationAsync_Failed_DeliversTypedNotificationE2E()
    {
        // Companion to the Working/Completed test above, covering the Failed branch which
        // carries the required JsonElement Error payload.
        var notifications = Channel.CreateUnbounded<TaskStatusNotificationParams>();

        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        await using var registration = client.RegisterNotificationHandler(
            NotificationMethods.TaskStatusNotification,
            (notification, _) =>
            {
                var typed = JsonSerializer.Deserialize<TaskStatusNotificationParams>(
                    notification.Params,
                    McpJsonUtilities.DefaultOptions);
                if (typed is FailedTaskNotificationParams)
                {
                    notifications.Writer.TryWrite(typed);
                }

                return default;
            });

        // The tool emits a Failed notification then returns a normal result, so we isolate the
        // notification round-trip from the task-store's own failure handling.
        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "failing-notify-tool" }, ct);

        Assert.Equal("emitted-failed", Assert.IsType<TextContentBlock>(result.Content[0]).Text);

        var failed = await notifications.Reader.ReadAsync(ct);
        var typed = Assert.IsType<FailedTaskNotificationParams>(failed);
        Assert.Equal("failing-notify-task-id", typed.TaskId);
        Assert.Equal(McpTaskStatus.Failed, typed.Status);
        Assert.Equal(-32000, typed.Error.GetProperty("code").GetInt32());
        Assert.Equal("boom", typed.Error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ElicitTool_ViaTask_ClientDedups_InputRequests()
    {
        // This test verifies that the client doesn't re-resolve an input request
        // that it has already responded to in a previous poll cycle.
        int elicitCallCount = 0;

        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    Interlocked.Increment(ref elicitCallCount);
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
                }
            }
        });
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "elicit-tool" }, ct);

        // The handler should be called exactly once despite potential multiple polls
        Assert.Equal(1, elicitCallCount);
        Assert.Equal("accepted", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CallToolRawAsync_ElicitTool_ReturnsTask_ThenPollShowsInputRequired()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                    new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" })
            }
        });
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "elicit-tool" }, ct);

        Assert.True(augmented.IsTask);
        var taskId = augmented.TaskCreated!.TaskId;

        // Poll — eventually the task should be input_required (elicit-tool calls ElicitAsync)
        GetTaskResult? taskResult = null;
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is InputRequiredTaskResult)
            {
                break;
            }
        }

        Assert.IsType<InputRequiredTaskResult>(taskResult);
    }

    [Fact]
    public async Task CancelTaskAsync_AlreadyCompleted_AcknowledgesIdempotently_AndDoesNotResurrect()
    {
        // Exercises the SDK's default tasks/cancel handler against the real InMemoryMcpTaskStore.
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        // Run a fast tool to completion via the task store.
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "fast-tool" }, ct);
        var taskId = augmented.TaskCreated!.TaskId;

        GetTaskResult? taskResult = null;
        for (int i = 0; i < 40 && taskResult is not CompletedTaskResult; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
        }
        Assert.IsType<CompletedTaskResult>(taskResult);

        // SEP-2663: tasks/cancel must be acknowledged idempotently even after the task has completed.
        var cancelResult = await client.CancelTaskAsync(taskId, ct);
        Assert.NotNull(cancelResult);

        // The task must remain Completed and the result must not be lost.
        var verifyResult = await client.GetTaskAsync(taskId, ct);
        var stillCompleted = Assert.IsType<CompletedTaskResult>(verifyResult);
        Assert.NotEqual(default(JsonElement), stillCompleted.Result);
    }

    [Fact]
    public async Task CallToolAsync_ElicitHandlerThrows_PropagatesAndDoesNotLeaveClientStuck()
    {
        // Verifies bug fix: when the client-side input handler throws while resolving an
        // InputRequired task, the exception propagates promptly (instead of the poll loop
        // hanging) and the client issues a best-effort tasks/cancel to release the server.
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                    throw new InvalidOperationException("handler-failed"),
            }
        });
        var ct = TestContext.Current.CancellationToken;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CallToolAsync(new CallToolRequestParams { Name = "elicit-tool" }, ct));
        sw.Stop();

        Assert.Equal("handler-failed", ex.Message);

        // Must fail fast: without the fix this would keep polling until the test cancellation token fires.
        // Allow generous slack for CI but well under the test timeout.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"CallToolAsync should propagate input handler exceptions promptly but took {sw.Elapsed}.");
    }

    [Fact]
    public async Task CallTool_WithoutTaskExtensionMeta_ReturnsCallToolResultImmediately()
    {
        // SEP-2663: "A server MUST NOT return a CreateTaskResult to a client that did not include the
        // extension capability." We bypass CallToolRawAsync (which injects the marker) and send a raw
        // tools/call request without the io.modelcontextprotocol/tasks key in _meta.
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var result = await client.SendRequestAsync<CallToolRequestParams, CallToolResult>(
            RequestMethods.ToolsCall,
            new CallToolRequestParams { Name = "fast-tool" },
            serializerOptions: McpJsonUtilities.DefaultOptions,
            cancellationToken: ct);

        // Server should return a regular CallToolResult, never escalate to a task.
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.Equal("fast result", Assert.IsType<TextContentBlock>(result.Content[0]).Text);

        // resultType is reserved and must not be the "task" discriminator for plain results.
        Assert.NotEqual("task", result.ResultType);
    }

    [Fact]
    public async Task ToolReturnsCallToolResultWithIsError_AsTask_StoresAsCompleted_NotFailed()
    {
        // SEP-2663: "An MCP server MUST NOT use [Failed] for errors that would have been signaled
        // by setting `CallToolResult.isError` to true ... Such errors are domain-level errors, and
        // their result MUST be returned by the server in the same way that any standard call-tool
        // result is returned." So a tool that returns isError:true MUST end up as a Completed task.
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "iserror-tool" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        GetTaskResult? taskResult = null;
        for (int i = 0; i < 40 && taskResult is not CompletedTaskResult; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
        }

        var completed = Assert.IsType<CompletedTaskResult>(taskResult);
        Assert.Equal(McpTaskStatus.Completed, completed.Status);
        Assert.True(completed.Result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task MultiElicit_ViaTask_HandlerCalledExactlyOncePerUniqueKey_AcrossPolls()
    {
        // SEP-2663: "Each entry [in inputRequests] MUST be treated as if it were an equivalent
        // standalone server-to-client request" and "clients SHOULD use [keys] to deduplicate".
        // A tool that fires two concurrent server->client requests produces two unique keys; the
        // client must dispatch the handler exactly twice in total, even across multiple polls.
        int elicitCount = 0;
        var observedMessages = new System.Collections.Concurrent.ConcurrentBag<string>();

        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    Interlocked.Increment(ref elicitCount);
                    observedMessages.Add(request?.Message ?? string.Empty);
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
                }
            }
        });
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "multi-elicit-tool" }, ct);

        // Exactly two handler invocations — one per unique input request key.
        Assert.Equal(2, elicitCount);
        Assert.Contains("first", observedMessages);
        Assert.Contains("second", observedMessages);
        Assert.Equal("accept|accept", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [McpServerToolType]
    private sealed class TaskStoreTestTools
    {
        [McpServerTool(Name = "slow-tool"), System.ComponentModel.Description("A tool that takes time")]
        public static async Task<string> SlowTool(CancellationToken cancellationToken)
        {
            await Task.Delay(200, cancellationToken);
            return "slow result";
        }

        [McpServerTool(Name = "fast-tool"), System.ComponentModel.Description("A fast tool")]
        public static string FastTool() => "fast result";

        [McpServerTool(Name = "failing-tool"), System.ComponentModel.Description("A tool that fails")]
        public static string FailingTool() => throw new InvalidOperationException("intentional failure");

        [McpServerTool(Name = "throws-mcp-protocol"), System.ComponentModel.Description("A tool that throws McpProtocolException")]
        public static string ThrowsMcpProtocol() =>
            throw new McpProtocolException("custom-protocol-message", McpErrorCode.InvalidParams);

        [McpServerTool(Name = "mrtr-tool"), System.ComponentModel.Description("A tool that throws InputRequiredException (MRTR)")]
        public static string MrtrTool() =>
            throw new InputRequiredException(requestState: "test-state");

        [McpServerTool(Name = "elicit-tool"), System.ComponentModel.Description("A tool that elicits")]
        public static async Task<string> ElicitTool(McpServer server, CancellationToken cancellationToken)
        {
            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = "What is your name?",
                RequestedSchema = new(),
            }, cancellationToken);

            return result.Action == "accept" ? "accepted" : "declined";
        }

        [McpServerTool(Name = "sample-tool"), System.ComponentModel.Description("A tool that samples")]
        public static async Task<string> SampleTool(McpServer server, CancellationToken cancellationToken)
        {
            var result = await server.SampleAsync(new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "hello" }] }],
                MaxTokens = 100,
            }, cancellationToken);

            return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "no response";
        }

        [McpServerTool(Name = "roots-tool"), System.ComponentModel.Description("A tool that lists roots")]
        public static async Task<string> RootsTool(McpServer server, CancellationToken cancellationToken)
        {
            var result = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken);
            return string.Join(",", result.Roots.Select(r => r.Uri));
        }

        [McpServerTool(Name = "notifying-tool"), System.ComponentModel.Description("A tool that emits SendTaskStatusNotificationAsync from inside the task wrapper")]
        public static async Task<string> NotifyingTool(McpServer server, CancellationToken cancellationToken)
        {
            var createdAt = DateTimeOffset.UtcNow;

            // Emit working then completed notifications using the public SendTaskStatusNotificationAsync API,
            // so the test asserts the wire round-trip end-to-end (server → transport → client handler).
            // Use distinct LastUpdatedAt values so the test can reconstruct send order on the receive side
            // (client-side dispatch via McpSessionHandler.ProcessMessageAsync is fire-and-forget per message
            // and may surface notifications to user handlers out of receipt order).
            await server.SendTaskStatusNotificationAsync(new WorkingTaskNotificationParams
            {
                TaskId = "notify-test-task-id",
                CreatedAt = createdAt,
                LastUpdatedAt = createdAt,
            }, cancellationToken);

            await server.SendTaskStatusNotificationAsync(new CompletedTaskNotificationParams
            {
                TaskId = "notify-test-task-id",
                CreatedAt = createdAt,
                LastUpdatedAt = createdAt.AddTicks(1),
                Result = JsonElement.Parse("\"notify-result\""),
            }, cancellationToken);

            return "notified";
        }

        [McpServerTool(Name = "failing-notify-tool"), System.ComponentModel.Description("A tool that emits a FailedTaskNotificationParams via SendTaskStatusNotificationAsync")]
        public static async Task<string> FailingNotifyTool(McpServer server, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var errorJson = JsonElement.Parse("""{"code":-32000,"message":"boom"}""");

            await server.SendTaskStatusNotificationAsync(new FailedTaskNotificationParams
            {
                TaskId = "failing-notify-task-id",
                CreatedAt = now,
                LastUpdatedAt = now,
                Error = errorJson,
            }, cancellationToken);

            return "emitted-failed";
        }

        [McpServerTool(Name = "iserror-tool"), System.ComponentModel.Description("A tool that returns IsError=true without throwing")]
        public static CallToolResult IsErrorTool() => new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "domain-error" }],
        };

        [McpServerTool(Name = "multi-elicit-tool"), System.ComponentModel.Description("A tool that issues two parallel elicitations")]
        public static async Task<string> MultiElicitTool(McpServer server, CancellationToken cancellationToken)
        {
            var first = server.ElicitAsync(new ElicitRequestParams
            {
                Message = "first",
                RequestedSchema = new(),
            }, cancellationToken);

            var second = server.ElicitAsync(new ElicitRequestParams
            {
                Message = "second",
                RequestedSchema = new(),
            }, cancellationToken);

            await Task.WhenAll(first.AsTask(), second.AsTask());

            return $"{first.Result.Action}|{second.Result.Action}";
        }
    }
}
