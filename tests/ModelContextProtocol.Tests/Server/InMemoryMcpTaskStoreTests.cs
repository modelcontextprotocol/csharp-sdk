using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Unit tests for <see cref="InMemoryMcpTaskStore"/>.
/// </summary>
public class InMemoryMcpTaskStoreTests
{
    private CancellationToken CT => TestContext.Current.CancellationToken;

    private static InputRequest MakeRequest(string payload) =>
        new() { Method = "test/method", Params = JsonSerializer.SerializeToElement(payload, McpJsonUtilities.DefaultOptions) };

    private static InputResponse MakeResponse(string payload) =>
        new() { RawValue = JsonSerializer.SerializeToElement(payload, McpJsonUtilities.DefaultOptions) };

    [Fact]
    public async Task CreateTaskAsync_ReturnsWorkingTaskWithUniqueId()
    {
        var store = new InMemoryMcpTaskStore();

        var result = await store.CreateTaskAsync(CT);

        Assert.NotNull(result);
        Assert.NotEmpty(result.TaskId);
        Assert.Equal(McpTaskStatus.Working, result.Status);
        Assert.NotEqual(default, result.CreatedAt);
        Assert.NotEqual(default, result.LastUpdatedAt);
    }

    [Fact]
    public async Task CreateTaskAsync_GeneratesUniqueIds()
    {
        var store = new InMemoryMcpTaskStore();

        var task1 = await store.CreateTaskAsync(CT);
        var task2 = await store.CreateTaskAsync(CT);

        Assert.NotEqual(task1.TaskId, task2.TaskId);
    }

    [Fact]
    public async Task CreateTaskAsync_UsesDefaultPollInterval()
    {
        var store = new InMemoryMcpTaskStore { DefaultPollIntervalMs = 500 };

        var result = await store.CreateTaskAsync(CT);

        Assert.Equal(500, result.PollIntervalMs);
    }

    [Fact]
    public async Task CreateTaskAsync_UsesDefaultTimeToLive()
    {
        var store = new InMemoryMcpTaskStore { DefaultTimeToLive = TimeSpan.FromSeconds(30) };

        var result = await store.CreateTaskAsync(CT);

        Assert.Equal(TimeSpan.FromSeconds(30), result.TimeToLive);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsWorkingTask()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        var result = await store.GetTaskAsync(created.TaskId, CT);

        Assert.NotNull(result);
        Assert.Equal(McpTaskStatus.Working, result.Status);
        Assert.Equal(created.TaskId, result.TaskId);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsNullForUnknownId()
    {
        var store = new InMemoryMcpTaskStore();

        var result = await store.GetTaskAsync("nonexistent", CT);

        Assert.Null(result);
    }

    [Fact]
    public async Task SetCompletedAsync_TransitionsToCompleted()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        var resultPayload = JsonDocument.Parse("""{"answer":42}""").RootElement.Clone();

        await store.SetCompletedAsync(created.TaskId, resultPayload, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Completed, task.Status);
        Assert.Equal(42, task.Result!.Value.GetProperty("answer").GetInt32());
    }

    [Fact]
    public async Task SetFailedAsync_TransitionsToFailed()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        var errorPayload = JsonDocument.Parse("""{"message":"boom"}""").RootElement.Clone();

        await store.SetFailedAsync(created.TaskId, errorPayload, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Failed, task.Status);
        Assert.Equal("boom", task.Error!.Value.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SetCancelledAsync_TransitionsToCancelled()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        var cancelled = await store.SetCancelledAsync(created.TaskId, CT);

        Assert.True(cancelled);
        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Cancelled, task.Status);
    }

    [Fact]
    public async Task SetCancelledAsync_ReturnsFalseForTerminalTask()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        await store.SetCompletedAsync(created.TaskId, JsonSerializer.SerializeToElement("done", McpJsonUtilities.DefaultOptions), CT);

        var cancelled = await store.SetCancelledAsync(created.TaskId, CT);

        Assert.False(cancelled);
        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Completed, task.Status);
    }

    [Fact]
    public async Task SetCancelledAsync_ReturnsFalseForUnknownId()
    {
        var store = new InMemoryMcpTaskStore();

        var cancelled = await store.SetCancelledAsync("nonexistent", CT);

        Assert.False(cancelled);
    }

    [Fact]
    public async Task SetInputRequestsAsync_TransitionsToInputRequired()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        var requests = new Dictionary<string, InputRequest>
        {
            ["req1"] = new InputRequest
            {
                Method = "elicitation/create",
                Params = JsonElement.Parse("""{"message":"hello"}"""),
            },
        };
        await store.SetInputRequestsAsync(created.TaskId, requests, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.InputRequired, task.Status);
        Assert.NotNull(task.InputRequests);
        Assert.Single(task.InputRequests);
        Assert.True(task.InputRequests.ContainsKey("req1"));
    }

    [Fact]
    public async Task SetInputRequestsAsync_MergesMultipleRequests()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["req1"] = MakeRequest("first")
        }, CT);
        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["req2"] = MakeRequest("second")
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.InputRequired, task.Status);
        Assert.NotNull(task.InputRequests);
        Assert.Equal(2, task.InputRequests.Count);
        Assert.True(task.InputRequests.ContainsKey("req1"));
        Assert.True(task.InputRequests.ContainsKey("req2"));
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_RemovesMatchedRequests()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["req1"] = MakeRequest("request1"),
            ["req2"] = MakeRequest("request2"),
        }, CT);

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["req1"] = MakeResponse("response1"),
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.InputRequired, task.Status);
        Assert.NotNull(task.InputRequests);
        Assert.Single(task.InputRequests);
        Assert.True(task.InputRequests.ContainsKey("req2"));
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_TransitionsToWorkingWhenAllSatisfied()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["req1"] = MakeRequest("request1"),
        }, CT);

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["req1"] = MakeResponse("response1"),
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }

    [Fact]
    public async Task SetCompletedAsync_ThrowsForUnknownTask()
    {
        var store = new InMemoryMcpTaskStore();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SetCompletedAsync("nonexistent", JsonSerializer.SerializeToElement("x", McpJsonUtilities.DefaultOptions), CT));
    }

    [Fact]
    public async Task ConcurrentUpdates_DoNotLoseData()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        var tasks = Enumerable.Range(0, 50).Select(i =>
            store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
            {
                [$"req{i}"] = MakeRequest($"value{i}")
            }, CT));

        await Task.WhenAll(tasks);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.InputRequired, task.Status);
        Assert.NotNull(task.InputRequests);
        Assert.Equal(50, task.InputRequests.Count);
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_ForExtraKeys_DoesNotThrow()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["unknown-key"] = MakeResponse("response"),
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_AlreadyResolvedKey_IsNoOp()
    {
        // SEP-2663: "Each entry key SHOULD be unique across the lifetime of a given task" and
        // servers should tolerate clients re-sending an inputResponse for an already-resolved key.
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["a"] = MakeRequest("ask-a"),
            ["b"] = MakeRequest("ask-b"),
        }, CT);

        // First resolve "a" — task should still be InputRequired because "b" remains.
        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["a"] = MakeResponse("answer-a"),
        }, CT);

        var afterFirst = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(afterFirst);
        Assert.Equal(McpTaskStatus.InputRequired, afterFirst.Status);
        Assert.NotNull(afterFirst.InputRequests);
        Assert.Single(afterFirst.InputRequests);
        Assert.Contains("b", afterFirst.InputRequests.Keys);

        // Re-send "a" — should be a no-op (no exception, no state change).
        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["a"] = MakeResponse("answer-a-again"),
        }, CT);

        var afterDup = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(afterDup);
        Assert.Equal(McpTaskStatus.InputRequired, afterDup.Status);
        Assert.NotNull(afterDup.InputRequests);
        Assert.Single(afterDup.InputRequests);
        Assert.Contains("b", afterDup.InputRequests.Keys);

        // Resolve the remaining "b" — task should transition back to Working with an empty inputRequests.
        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["b"] = MakeResponse("answer-b"),
        }, CT);

        var final = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(final);
        Assert.Equal(McpTaskStatus.Working, final.Status);
        Assert.True(final.InputRequests is null || final.InputRequests.Count == 0);
    }

    [Fact]
    public async Task ConcurrentResolveInputRequests_OnDisjointKeys_AllResolveCorrectly()
    {
        // Verifies the optimistic-concurrency loop in InMemoryMcpTaskStore handles parallel
        // tasks/update calls that each resolve a distinct subset of pending input requests.
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        var seed = Enumerable.Range(0, 20).ToDictionary(
            i => $"req{i}",
            i => MakeRequest($"ask{i}"));
        await store.SetInputRequestsAsync(created.TaskId, seed, CT);

        var resolveTasks = Enumerable.Range(0, 20).Select(i =>
            store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
            {
                [$"req{i}"] = MakeResponse($"answer{i}"),
            }, CT));

        await Task.WhenAll(resolveTasks);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
        Assert.True(task.InputRequests is null || task.InputRequests.Count == 0);
    }

    [Fact]
    public async Task SetCompletedAsync_DoesNotOverwriteCancelledTask()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        var cancelled = await store.SetCancelledAsync(created.TaskId, CT);
        Assert.True(cancelled);

        // Background worker finishing after cancellation must not flip the task back to Completed.
        await store.SetCompletedAsync(
            created.TaskId,
            JsonSerializer.SerializeToElement("late-result", McpJsonUtilities.DefaultOptions),
            CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Cancelled, task.Status);
        Assert.Null(task.Result);
    }

    [Fact]
    public async Task SetFailedAsync_DoesNotOverwriteCancelledTask()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        await store.SetCancelledAsync(created.TaskId, CT);

        await store.SetFailedAsync(
            created.TaskId,
            JsonElement.Parse("""{"message":"boom"}"""),
            CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Cancelled, task.Status);
        Assert.Null(task.Error);
    }

    [Fact]
    public async Task SetCompletedAsync_DoesNotOverwriteCompletedTask()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        var first = JsonSerializer.SerializeToElement("first", McpJsonUtilities.DefaultOptions);
        await store.SetCompletedAsync(created.TaskId, first, CT);

        // A second completion attempt must not replace the original result.
        var second = JsonSerializer.SerializeToElement("second", McpJsonUtilities.DefaultOptions);
        await store.SetCompletedAsync(created.TaskId, second, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Completed, task.Status);
        Assert.Equal("first", task.Result!.Value.GetString());
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_OnTerminalTask_DoesNotResurrect()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        await store.SetCompletedAsync(
            created.TaskId,
            JsonSerializer.SerializeToElement("done", McpJsonUtilities.DefaultOptions),
            CT);

        // A client tasks/update against a Completed task must not flip it back to Working.
        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["req1"] = MakeResponse("response"),
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Completed, task.Status);
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_OnTerminalTask_DoesNotFireEvent()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        await store.SetCancelledAsync(created.TaskId, CT);

        int eventCount = 0;
        store.InputResponseReceived += _ => Interlocked.Increment(ref eventCount);

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["req1"] = MakeResponse("response"),
        }, CT);

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public async Task SetInputRequestsAsync_OnTerminalTask_NoOps()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        await store.SetCancelledAsync(created.TaskId, CT);

        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["req1"] = MakeRequest("payload"),
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Cancelled, task.Status);
        Assert.True(task.InputRequests is null || task.InputRequests.Count == 0);
    }
}
