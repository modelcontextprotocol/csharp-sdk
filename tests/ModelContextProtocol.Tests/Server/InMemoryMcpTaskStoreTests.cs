using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Unit tests for <see cref="InMemoryMcpTaskStore"/>.
/// </summary>
public class InMemoryMcpTaskStoreTests
{
    private static readonly DateTimeOffset s_start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan s_ttl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_sweepInterval = TimeSpan.FromSeconds(30);

    private CancellationToken CT => TestContext.Current.CancellationToken;

    private static InputRequest MakeRequest(string payload) =>
        new() { Method = "test/method", Params = JsonSerializer.SerializeToElement(payload, McpJsonUtilities.DefaultOptions) };

    private static InputResponse MakeResponse(string payload) =>
        new() { RawValue = JsonSerializer.SerializeToElement(payload, McpJsonUtilities.DefaultOptions) };

    private static JsonElement DonePayload() =>
        JsonSerializer.SerializeToElement("done", McpJsonUtilities.DefaultOptions);

    private static JsonElement ErrorPayload() =>
        JsonElement.Parse("""{"message":"boom"}""");

    private static FakeTimeProvider CreateClock() => new(s_start);

    private static InMemoryMcpTaskStore CreateStore(FakeTimeProvider clock, TimeSpan? ttl = null) =>
        new(clock) { DefaultTimeToLive = ttl };

    private static McpTaskInfo Info(
        McpTaskStatus status = McpTaskStatus.Working,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lastUpdatedAt = null,
        TimeSpan? ttl = null,
        IReadOnlyDictionary<string, InputRequest>? inputRequests = null) =>
        new("task", status, createdAt ?? s_start, lastUpdatedAt ?? createdAt ?? s_start, ttl, InputRequests: inputRequests);

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
    public async Task GetTaskAsync_WithinTimeToLive_ReturnsTask()
    {
        var store = new InMemoryMcpTaskStore { DefaultTimeToLive = TimeSpan.FromMinutes(10) };
        var created = await store.CreateTaskAsync(CT);

        var result = await store.GetTaskAsync(created.TaskId, CT);

        Assert.NotNull(result);
        Assert.Equal(created.TaskId, result.TaskId);
    }

    [Fact]
    public async Task GetTaskAsync_AfterTimeToLiveElapsed_ReturnsNull()
    {
        var store = new InMemoryMcpTaskStore { DefaultTimeToLive = TimeSpan.FromMilliseconds(100) };
        var created = await store.CreateTaskAsync(CT);

        await Task.Delay(TimeSpan.FromMilliseconds(500), CT);

        var result = await store.GetTaskAsync(created.TaskId, CT);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetTaskAsync_WithoutTimeToLive_DoesNotExpire()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        await Task.Delay(TimeSpan.FromMilliseconds(200), CT);

        var result = await store.GetTaskAsync(created.TaskId, CT);

        Assert.NotNull(result);
        Assert.Equal(created.TaskId, result.TaskId);
    }

    [Fact]
    public async Task GetTaskAsync_WithZeroTimeToLive_DoesNotExpire()
    {
        var store = new InMemoryMcpTaskStore { DefaultTimeToLive = TimeSpan.Zero };
        var created = await store.CreateTaskAsync(CT);

        await Task.Delay(TimeSpan.FromMilliseconds(200), CT);

        var result = await store.GetTaskAsync(created.TaskId, CT);

        Assert.NotNull(result);
        Assert.Equal(created.TaskId, result.TaskId);
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
    public async Task SetCompletedAsync_UnknownTask_IsNoOp()
    {
        var store = new InMemoryMcpTaskStore();

        await store.SetCompletedAsync("nonexistent", JsonSerializer.SerializeToElement("x", McpJsonUtilities.DefaultOptions), CT);

        Assert.Null(await store.GetTaskAsync("nonexistent", CT));
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

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>("timeProvider", () => new InMemoryMcpTaskStore(null!));
    }

    [Fact]
    public async Task Constructor_TimeProvider_IsUsedForTimestamps()
    {
        var clock = CreateClock();
        var store = CreateStore(clock);

        var created = await store.CreateTaskAsync(CT);

        Assert.Equal(s_start, created.CreatedAt);
        Assert.Equal(s_start, created.LastUpdatedAt);
    }

    [Fact]
    public async Task GetTaskAsync_AfterOneTtlWhileWorking_StillReturnsTask()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl);

        var result = await store.GetTaskAsync(created.TaskId, CT);

        Assert.NotNull(result);
        Assert.Equal(McpTaskStatus.Working, result.Status);
        Assert.Contains(created.TaskId, store._tasks.Keys);
    }

    [Fact]
    public async Task GetTaskAsync_AfterTwoTtlWhileWorking_ReturnsNull()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + s_ttl);

        var result = await store.GetTaskAsync(created.TaskId, CT);

        Assert.Null(result);
        Assert.False(store._tasks.ContainsKey(created.TaskId));
    }

    [Fact]
    public async Task GetTaskAsync_TerminalSnapshot_ServeAndDrop()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        await store.SetCompletedAsync(created.TaskId, DonePayload(), CT);

        var first = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(first);
        Assert.Equal(McpTaskStatus.Completed, first.Status);
        Assert.Equal("done", first.Result!.Value.GetString());

        var second = await store.GetTaskAsync(created.TaskId, CT);
        Assert.Null(second);
        Assert.False(store._tasks.ContainsKey(created.TaskId));
    }

    [Fact]
    public async Task GetTaskAsync_FailedSnapshot_ServeAndDrop()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        await store.SetFailedAsync(created.TaskId, ErrorPayload(), CT);

        var first = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(first);
        Assert.Equal(McpTaskStatus.Failed, first.Status);

        Assert.Null(await store.GetTaskAsync(created.TaskId, CT));
    }

    [Fact]
    public async Task GetTaskAsync_CancelledSnapshot_ServeAndDrop()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        Assert.True(await store.SetCancelledAsync(created.TaskId, CT));

        var first = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(first);
        Assert.Equal(McpTaskStatus.Cancelled, first.Status);

        Assert.Null(await store.GetTaskAsync(created.TaskId, CT));
    }

    [Fact]
    public async Task GetTaskAsync_InputRequired_DoesNotDrop()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["req1"] = MakeRequest("ask"),
        }, CT);

        var first = await store.GetTaskAsync(created.TaskId, CT);
        var second = await store.GetTaskAsync(created.TaskId, CT);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(McpTaskStatus.InputRequired, second.Status);
    }

    [Fact]
    public async Task GetTaskAsync_ExpiredTerminal_ReturnsNullWithoutServing()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);
        await store.SetCompletedAsync(created.TaskId, DonePayload(), CT);

        clock.Advance(s_ttl);

        var result = await store.GetTaskAsync(created.TaskId, CT);

        Assert.Null(result);
        Assert.False(store._tasks.ContainsKey(created.TaskId));
    }

    [Fact]
    public async Task GetTaskAsync_TerminalWithinLastUpdatedTtl_IsServed()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);
        clock.Advance(TimeSpan.FromSeconds(5));
        await store.SetCompletedAsync(created.TaskId, DonePayload(), CT);
        clock.Advance(TimeSpan.FromSeconds(9));

        var result = await store.GetTaskAsync(created.TaskId, CT);

        Assert.NotNull(result);
        Assert.Equal(McpTaskStatus.Completed, result.Status);
        Assert.Equal("done", result.Result!.Value.GetString());
    }

    [Fact]
    public async Task SetCompletedAsync_InSecondWindow_IsAllowed()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + TimeSpan.FromSeconds(1));
        await store.SetCompletedAsync(created.TaskId, DonePayload(), CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Completed, task.Status);
        Assert.Equal("done", task.Result!.Value.GetString());
    }

    [Fact]
    public async Task SetFailedAsync_InSecondWindow_IsAllowed()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + TimeSpan.FromSeconds(1));
        await store.SetFailedAsync(created.TaskId, ErrorPayload(), CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Failed, task.Status);
    }

    [Fact]
    public async Task SetCancelledAsync_InSecondWindow_IsAllowed()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + TimeSpan.FromSeconds(1));
        var cancelled = await store.SetCancelledAsync(created.TaskId, CT);

        Assert.True(cancelled);
        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Cancelled, task.Status);
    }

    [Fact]
    public async Task SetInputRequestsAsync_InSecondWindow_IsRefused()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + TimeSpan.FromSeconds(1));
        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["req1"] = MakeRequest("late"),
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
        Assert.True(task.InputRequests is null || task.InputRequests.Count == 0);
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_InSecondWindow_IsRefusedAndDoesNotFireEvent()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);
        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["req1"] = MakeRequest("ask"),
        }, CT);

        clock.Advance(s_ttl + TimeSpan.FromSeconds(1));

        int eventCount = 0;
        store.InputResponseReceived += _ => Interlocked.Increment(ref eventCount);

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["req1"] = MakeResponse("late"),
        }, CT);

        Assert.Equal(0, eventCount);
        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.InputRequired, task.Status);
        Assert.True(task.InputRequests!.ContainsKey("req1"));
    }

    [Fact]
    public async Task Mutations_AfterHasExpired_AreNoOps()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + s_ttl);

        await store.SetCompletedAsync(created.TaskId, DonePayload(), CT);
        await store.SetFailedAsync(created.TaskId, ErrorPayload(), CT);
        Assert.False(await store.SetCancelledAsync(created.TaskId, CT));
        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["req1"] = MakeRequest("late"),
        }, CT);

        int eventCount = 0;
        store.InputResponseReceived += _ => Interlocked.Increment(ref eventCount);
        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["req1"] = MakeResponse("late"),
        }, CT);

        Assert.Equal(0, eventCount);
        Assert.Null(await store.GetTaskAsync(created.TaskId, CT));
    }

    [Fact]
    public async Task Mutations_UnknownTask_AreNoOps()
    {
        var store = new InMemoryMcpTaskStore();

        await store.SetFailedAsync("missing", ErrorPayload(), CT);
        await store.SetInputRequestsAsync("missing", new Dictionary<string, InputRequest>
        {
            ["req1"] = MakeRequest("x"),
        }, CT);

        int eventCount = 0;
        store.InputResponseReceived += _ => Interlocked.Increment(ref eventCount);
        await store.ResolveInputRequestsAsync("missing", new Dictionary<string, InputResponse>
        {
            ["req1"] = MakeResponse("x"),
        }, CT);

        Assert.Equal(0, eventCount);
        Assert.Null(await store.GetTaskAsync("missing", CT));
    }

    [Fact]
    public async Task GetTaskAsync_WithNegativeTimeToLive_DoesNotExpire()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, TimeSpan.FromSeconds(-5));
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(TimeSpan.FromDays(1));

        var result = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(result);
        Assert.Equal(created.TaskId, result.TaskId);
    }

    [Fact]
    public async Task DefaultTimeToLive_ChangeAfterCreate_DoesNotAffectExistingTask()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        store.DefaultTimeToLive = TimeSpan.FromMilliseconds(1);
        clock.Advance(TimeSpan.FromSeconds(5));

        var result = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(result);
        Assert.Equal(s_ttl, result.TimeToLive);
    }

    [Fact]
    public async Task SetCompletedAsync_UpdatesLastUpdatedAtFromTimeProvider()
    {
        var clock = CreateClock();
        var store = CreateStore(clock);
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(TimeSpan.FromSeconds(3));
        await store.SetCompletedAsync(created.TaskId, DonePayload(), CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(s_start, task.CreatedAt);
        Assert.Equal(s_start + TimeSpan.FromSeconds(3), task.LastUpdatedAt);
    }

    [Fact]
    public async Task CreateTaskAsync_InsertsBeforeSweep_NewTaskSurvives()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var victim = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + s_ttl + s_sweepInterval);
        var survivor = await store.CreateTaskAsync(CT);

        Assert.False(store._tasks.ContainsKey(victim.TaskId));
        Assert.True(store._tasks.ContainsKey(survivor.TaskId));
        Assert.NotNull(await store.GetTaskAsync(survivor.TaskId, CT));
    }

    [Fact]
    public async Task GetTaskAsync_PulsesSweep_RemovesUnpolledExpiredTask()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var victim = await store.CreateTaskAsync(CT);

        store.DefaultTimeToLive = null;
        var live = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + s_ttl + s_sweepInterval);
        Assert.NotNull(await store.GetTaskAsync(live.TaskId, CT));

        Assert.False(store._tasks.ContainsKey(victim.TaskId));
        Assert.True(store._tasks.ContainsKey(live.TaskId));
    }

    [Fact]
    public async Task Sweep_WithinInterval_DoesNotRemoveExpiredTask()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var victim = await store.CreateTaskAsync(CT);

        store.DefaultTimeToLive = null;
        var live = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + s_ttl);
        Assert.NotNull(await store.GetTaskAsync(live.TaskId, CT));

        Assert.True(store._tasks.ContainsKey(victim.TaskId));
    }

    [Fact]
    public async Task Sweep_DoesNotRemoveSecondWindowWorkingTask()
    {
        var clock = CreateClock();
        var ttl = TimeSpan.FromSeconds(20);
        var store = CreateStore(clock, ttl);
        var created = await store.CreateTaskAsync(CT);

        // Sweep interval is 30s; with TTL=20s that instant is still in the second window (20s..40s).
        clock.Advance(s_sweepInterval);
        _ = await store.CreateTaskAsync(CT);

        Assert.True(store._tasks.ContainsKey(created.TaskId));
        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }

    [Fact]
    public async Task Sweep_RemovesTerminalPastLastUpdatedTtl()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);
        await store.SetCompletedAsync(created.TaskId, DonePayload(), CT);

        clock.Advance(s_ttl + s_sweepInterval);
        _ = await store.CreateTaskAsync(CT);

        Assert.False(store._tasks.ContainsKey(created.TaskId));
    }

    [Fact]
    public async Task Sweep_DoesNotRemoveTerminalWithinLastUpdatedTtl()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, TimeSpan.FromSeconds(60));
        var created = await store.CreateTaskAsync(CT);
        await store.SetCompletedAsync(created.TaskId, DonePayload(), CT);

        clock.Advance(s_sweepInterval);
        _ = await store.CreateTaskAsync(CT);

        Assert.True(store._tasks.ContainsKey(created.TaskId));
    }

    [Fact]
    public async Task Sweep_ConcurrentPulses_OnlyOneWalks()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var ids = new string[8];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = (await store.CreateTaskAsync(CT)).TaskId;
        }

        clock.Advance(s_ttl + s_ttl + s_sweepInterval);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => store.CreateTaskAsync(CT)));

        foreach (var id in ids)
        {
            Assert.False(store._tasks.ContainsKey(id));
        }
    }

    [Fact]
    public async Task SweepExpired_ConcurrentCallers_CompareExchangeLoserReturns()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        _ = await store.CreateTaskAsync(CT);

        int threadCount = Math.Max(16, Environment.ProcessorCount * 2);
        for (int round = 0; round < 20; round++)
        {
            // Reset the throttle so every round's callers all pass the interval check and race the CAS.
            store._lastSweepTicks = clock.GetUtcNow().UtcTicks - InMemoryMcpTaskStore.s_sweepIntervalTicks - 1;
            var now = clock.GetUtcNow();
            var barrier = new Barrier(threadCount);
            var pulses = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                barrier.SignalAndWait(CT);
                store.SweepExpired(now, CancellationToken.None);
            }, CT));

            await Task.WhenAll(pulses);
        }
    }

    [Fact]
    public async Task ConcurrentUpdates_RetryLoop_DoesNotLoseKeys()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var workers = Enumerable.Range(0, 32).Select(async i =>
        {
            await start.Task;
            for (int n = 0; n < 40; n++)
            {
                await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
                {
                    [$"req{i}_{n}"] = MakeRequest($"value{i}_{n}")
                }, CT);
            }
        });

        var running = Task.WhenAll(workers);
        start.SetResult();
        await running;

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(32 * 40, task.InputRequests!.Count);
    }

    [Fact]
    public async Task CreateTaskAsync_CanceledToken_DoesNotInsert()
    {
        var store = new InMemoryMcpTaskStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.CreateTaskAsync(new CancellationToken(canceled: true)));

        Assert.Empty(store._tasks);
    }

    [Fact]
    public async Task PublicMethods_CanceledToken_ThrowWithoutMutating()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        var canceled = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetTaskAsync(created.TaskId, canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SetCompletedAsync(created.TaskId, DonePayload(), canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SetFailedAsync(created.TaskId, ErrorPayload(), canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SetCancelledAsync(created.TaskId, canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest> { ["r"] = MakeRequest("x") }, canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse> { ["r"] = MakeResponse("x") }, canceled));

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }

    [Fact]
    public async Task SweepExpired_CanceledDuringWalk_Throws()
    {
        var inner = CreateClock();
        using var cts = new CancellationTokenSource();
        var clock = new CancelOnNthUtcNowTimeProvider(inner, cts, cancelOnCall: 3);
        var store = new InMemoryMcpTaskStore(clock) { DefaultTimeToLive = s_ttl };

        _ = await store.CreateTaskAsync(CT);
        inner.Advance(s_ttl + s_ttl + s_sweepInterval);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateTaskAsync(cts.Token));
    }

    [Fact]
    public async Task SetCompletedAsync_CanceledAfterStart_DoesNotCommit()
    {
        var inner = CreateClock();
        using var cts = new CancellationTokenSource();
        var clock = new CancelOnNthUtcNowTimeProvider(inner, cts, cancelOnCall: 3);
        var store = new InMemoryMcpTaskStore(clock);

        var created = await store.CreateTaskAsync(CT);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SetCompletedAsync(created.TaskId, DonePayload(), cts.Token));

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
        Assert.Null(task.Result);
    }

    [Fact]
    public async Task SetCancelledAsync_CanceledAfterStart_DoesNotCommit()
    {
        var inner = CreateClock();
        using var cts = new CancellationTokenSource();
        var clock = new CancelOnNthUtcNowTimeProvider(inner, cts, cancelOnCall: 3);
        var store = new InMemoryMcpTaskStore(clock);

        var created = await store.CreateTaskAsync(CT);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SetCancelledAsync(created.TaskId, cts.Token));

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }

    [Fact]
    public async Task SetFailedAsync_CanceledAfterStart_DoesNotCommit()
    {
        var inner = CreateClock();
        using var cts = new CancellationTokenSource();
        var clock = new CancelOnNthUtcNowTimeProvider(inner, cts, cancelOnCall: 3);
        var store = new InMemoryMcpTaskStore(clock);

        var created = await store.CreateTaskAsync(CT);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SetFailedAsync(created.TaskId, ErrorPayload(), cts.Token));

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }

    [Fact]
    public async Task SetInputRequestsAsync_CanceledAfterStart_DoesNotCommit()
    {
        var inner = CreateClock();
        using var cts = new CancellationTokenSource();
        var clock = new CancelOnNthUtcNowTimeProvider(inner, cts, cancelOnCall: 3);
        var store = new InMemoryMcpTaskStore(clock);

        var created = await store.CreateTaskAsync(CT);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SetInputRequestsAsync(
                created.TaskId,
                new Dictionary<string, InputRequest> { ["r"] = MakeRequest("x") },
                cts.Token));

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_CanceledAfterStart_DoesNotCommitOrFire()
    {
        var inner = CreateClock();
        using var cts = new CancellationTokenSource();
        var clock = new CancelOnNthUtcNowTimeProvider(inner, cts, cancelOnCall: 4);
        var store = new InMemoryMcpTaskStore(clock);

        var created = await store.CreateTaskAsync(CT);
        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["r"] = MakeRequest("ask"),
        }, CT);

        int eventCount = 0;
        store.InputResponseReceived += _ => Interlocked.Increment(ref eventCount);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.ResolveInputRequestsAsync(
                created.TaskId,
                new Dictionary<string, InputResponse> { ["r"] = MakeResponse("x") },
                cts.Token));

        Assert.Equal(0, eventCount);
        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.InputRequired, task.Status);
    }

    [Fact]
    public async Task SetCancelledAsync_ConcurrentWithComplete_ReturnValueMatchesWinner()
    {
        var store = new InMemoryMcpTaskStore();

        for (int i = 0; i < 200; i++)
        {
            var created = await store.CreateTaskAsync(CT);
            var complete = store.SetCompletedAsync(created.TaskId, DonePayload(), CT);
            var cancel = store.SetCancelledAsync(created.TaskId, CT);
            await Task.WhenAll(complete, cancel);

            bool cancelled = await cancel;
            var snapshot = store._tasks[created.TaskId];
            if (cancelled)
            {
                Assert.Equal(McpTaskStatus.Cancelled, snapshot.Status);
                Assert.Null(snapshot.Result);
            }
            else
            {
                Assert.Equal(McpTaskStatus.Completed, snapshot.Status);
                Assert.Equal("done", snapshot.Result!.Value.GetString());
            }
        }
    }

    [Fact]
    public async Task ConcurrentTerminalGets_AtLeastOneServesThenRowIsGone()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        await store.SetCompletedAsync(created.TaskId, DonePayload(), CT);

        var results = await Task.WhenAll(
            store.GetTaskAsync(created.TaskId, CT),
            store.GetTaskAsync(created.TaskId, CT),
            store.GetTaskAsync(created.TaskId, CT));

        Assert.Contains(results, r => r is { Status: McpTaskStatus.Completed });
        Assert.Null(await store.GetTaskAsync(created.TaskId, CT));
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_FiresEventPerResponse()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["a"] = MakeRequest("ask-a"),
            ["b"] = MakeRequest("ask-b"),
        }, CT);

        var seen = new ConcurrentBag<string>();
        store.InputResponseReceived += args =>
        {
            Assert.Equal(created.TaskId, args.TaskId);
            seen.Add(args.RequestId);
        };

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["a"] = MakeResponse("a"),
            ["b"] = MakeResponse("b"),
        }, CT);

        Assert.Equal(2, seen.Count);
        Assert.Contains("a", seen);
        Assert.Contains("b", seen);
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_UnsubscribedHandler_DoesNotFire()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        int eventCount = 0;
        Action<InputResponseReceivedEventArgs> handler = _ => Interlocked.Increment(ref eventCount);
        store.InputResponseReceived += handler;
        store.InputResponseReceived -= handler;

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["r"] = MakeResponse("x"),
        }, CT);

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_NonImmutableInputRequests_StillResolves()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        store._tasks[created.TaskId] = created with
        {
            Status = McpTaskStatus.InputRequired,
            InputRequests = new Dictionary<string, InputRequest>
            {
                ["r"] = MakeRequest("ask"),
            },
        };

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["r"] = MakeResponse("x"),
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }

    [Fact]
    public async Task SetInputRequestsAsync_NonImmutableInputRequests_Merges()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        store._tasks[created.TaskId] = created with
        {
            InputRequests = new Dictionary<string, InputRequest>
            {
                ["a"] = MakeRequest("ask-a"),
            },
        };

        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest>
        {
            ["b"] = MakeRequest("ask-b"),
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.InputRequired, task.Status);
        Assert.Equal(2, task.InputRequests!.Count);
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_EmptyResponses_OnWorkingTask_FiresNoEvents()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        int eventCount = 0;
        store.InputResponseReceived += _ => Interlocked.Increment(ref eventCount);

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>(), CT);

        Assert.Equal(0, eventCount);
        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }

    [Fact]
    public async Task GetTaskAsync_EmptyTaskId_ReturnsNull()
    {
        var store = new InMemoryMcpTaskStore();
        Assert.Null(await store.GetTaskAsync(string.Empty, CT));
    }

    [Fact]
    public async Task SetFailedAsync_DoesNotOverwriteFailedTask()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        await store.SetFailedAsync(created.TaskId, JsonElement.Parse("""{"message":"first"}"""), CT);
        await store.SetFailedAsync(created.TaskId, JsonElement.Parse("""{"message":"second"}"""), CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Failed, task.Status);
        Assert.Equal("first", task.Error!.Value.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SetCancelledAsync_SecondCall_ReturnsFalse()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        Assert.True(await store.SetCancelledAsync(created.TaskId, CT));
        Assert.False(await store.SetCancelledAsync(created.TaskId, CT));
    }

    [Fact]
    public async Task CreateTaskAsync_DefaultPollInterval_Is1000()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        Assert.Equal(1000, created.PollIntervalMs);
    }

    [Fact]
    public async Task ResolveInputRequestsAsync_OnWorkingTask_FiresEvenWhenKeyWasNeverPending()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        InputResponseReceivedEventArgs? args = null;
        store.InputResponseReceived += e => args = e;

        var response = MakeResponse("x");
        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, InputResponse>
        {
            ["ghost"] = response,
        }, CT);

        Assert.NotNull(args);
        Assert.Equal(created.TaskId, args.TaskId);
        Assert.Equal("ghost", args.RequestId);
    }

    [Theory]
    [InlineData(McpTaskStatus.Working, false)]
    [InlineData(McpTaskStatus.InputRequired, false)]
    [InlineData(McpTaskStatus.Completed, true)]
    [InlineData(McpTaskStatus.Failed, true)]
    [InlineData(McpTaskStatus.Cancelled, true)]
    public void IsTerminal_KnownStatuses_ReturnExpected(McpTaskStatus status, bool expected)
    {
        Assert.Equal(expected, InMemoryMcpTaskStore.IsTerminal(status));
    }

    [Fact]
    public void IsTerminal_UnknownStatus_ReturnsFalse()
    {
        Assert.False(InMemoryMcpTaskStore.IsTerminal((McpTaskStatus)int.MaxValue));
    }

    [Fact]
    public void TryGetPositiveTtl_Null_ReturnsFalse()
    {
        Assert.False(InMemoryMcpTaskStore.TryGetPositiveTtl(Info(ttl: null), out var ttl));
        Assert.Equal(default, ttl);
    }

    [Fact]
    public void TryGetPositiveTtl_Zero_ReturnsFalse()
    {
        Assert.False(InMemoryMcpTaskStore.TryGetPositiveTtl(Info(ttl: TimeSpan.Zero), out var ttl));
        Assert.Equal(default, ttl);
    }

    [Fact]
    public void TryGetPositiveTtl_Negative_ReturnsFalse()
    {
        Assert.False(InMemoryMcpTaskStore.TryGetPositiveTtl(Info(ttl: TimeSpan.FromSeconds(-1)), out var ttl));
        Assert.Equal(default, ttl);
    }

    [Fact]
    public void TryGetPositiveTtl_Positive_ReturnsValue()
    {
        Assert.True(InMemoryMcpTaskStore.TryGetPositiveTtl(Info(ttl: s_ttl), out var ttl));
        Assert.Equal(s_ttl, ttl);
    }

    [Fact]
    public void IsActive_WorkingWithoutTtl_IsTrue()
    {
        Assert.True(InMemoryMcpTaskStore.IsActive(Info(), s_start + TimeSpan.FromDays(365)));
    }

    [Fact]
    public void IsActive_WorkingWithZeroTtl_IsTrue()
    {
        Assert.True(InMemoryMcpTaskStore.IsActive(Info(ttl: TimeSpan.Zero), s_start + TimeSpan.FromDays(1)));
    }

    [Fact]
    public void IsActive_WorkingWithNegativeTtl_IsTrue()
    {
        Assert.True(InMemoryMcpTaskStore.IsActive(Info(ttl: TimeSpan.FromSeconds(-5)), s_start + TimeSpan.FromDays(1)));
    }

    [Fact]
    public void IsActive_WorkingBeforeTtl_IsTrue()
    {
        Assert.True(InMemoryMcpTaskStore.IsActive(Info(ttl: s_ttl), s_start + TimeSpan.FromSeconds(9)));
    }

    [Fact]
    public void IsActive_WorkingAtTtlBoundary_IsFalse()
    {
        Assert.False(InMemoryMcpTaskStore.IsActive(Info(ttl: s_ttl), s_start + s_ttl));
    }

    [Fact]
    public void IsActive_WorkingInSecondWindow_IsFalse()
    {
        Assert.False(InMemoryMcpTaskStore.IsActive(Info(ttl: s_ttl), s_start + s_ttl + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void IsActive_InputRequiredWithinWindow_IsTrue()
    {
        Assert.True(InMemoryMcpTaskStore.IsActive(Info(McpTaskStatus.InputRequired, ttl: s_ttl), s_start + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void IsActive_InputRequiredAfterTtl_IsFalse()
    {
        Assert.False(InMemoryMcpTaskStore.IsActive(Info(McpTaskStatus.InputRequired, ttl: s_ttl), s_start + s_ttl));
    }

    [Theory]
    [InlineData(McpTaskStatus.Completed)]
    [InlineData(McpTaskStatus.Failed)]
    [InlineData(McpTaskStatus.Cancelled)]
    public void IsActive_Terminal_IsFalseEvenWithoutTtl(McpTaskStatus status)
    {
        Assert.False(InMemoryMcpTaskStore.IsActive(Info(status), s_start));
        Assert.False(InMemoryMcpTaskStore.IsActive(Info(status, ttl: s_ttl), s_start + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void HasExpired_NoTtl_IsFalseForWorkingAndTerminal()
    {
        var far = s_start + TimeSpan.FromDays(365);
        Assert.False(InMemoryMcpTaskStore.HasExpired(Info(), far));
        Assert.False(InMemoryMcpTaskStore.HasExpired(Info(ttl: TimeSpan.Zero), far));
        Assert.False(InMemoryMcpTaskStore.HasExpired(Info(ttl: TimeSpan.FromSeconds(-1)), far));
        Assert.False(InMemoryMcpTaskStore.HasExpired(Info(McpTaskStatus.Completed), far));
    }

    [Fact]
    public void HasExpired_WorkingAtOneTtl_IsFalse()
    {
        Assert.False(InMemoryMcpTaskStore.HasExpired(Info(ttl: s_ttl), s_start + s_ttl));
    }

    [Fact]
    public void HasExpired_WorkingJustBeforeTwoTtl_IsFalse()
    {
        Assert.False(InMemoryMcpTaskStore.HasExpired(Info(ttl: s_ttl), s_start + s_ttl + s_ttl - TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void HasExpired_WorkingAtTwoTtl_IsTrue()
    {
        Assert.True(InMemoryMcpTaskStore.HasExpired(Info(ttl: s_ttl), s_start + s_ttl + s_ttl));
    }

    [Fact]
    public void HasExpired_InputRequiredAtTwoTtl_IsTrue()
    {
        Assert.True(InMemoryMcpTaskStore.HasExpired(Info(McpTaskStatus.InputRequired, ttl: s_ttl), s_start + s_ttl + s_ttl));
    }

    [Fact]
    public void HasExpired_TerminalJustBeforeLastUpdatedPlusTtl_IsFalse()
    {
        var completed = Info(McpTaskStatus.Completed, lastUpdatedAt: s_start + TimeSpan.FromSeconds(5), ttl: s_ttl);
        Assert.False(InMemoryMcpTaskStore.HasExpired(completed, s_start + TimeSpan.FromSeconds(5) + s_ttl - TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void HasExpired_TerminalAtLastUpdatedPlusTtl_IsTrue()
    {
        var completed = Info(McpTaskStatus.Completed, lastUpdatedAt: s_start + TimeSpan.FromSeconds(5), ttl: s_ttl);
        Assert.True(InMemoryMcpTaskStore.HasExpired(completed, s_start + TimeSpan.FromSeconds(5) + s_ttl));
    }

    [Fact]
    public void HasExpired_TerminalClockIsLastUpdatedNotCreated()
    {
        // Completed at T+15s with TTL=10s: created+2×TTL is T+20s, but LastUpdatedAt+TTL is T+25s.
        var completed = Info(
            McpTaskStatus.Completed,
            createdAt: s_start,
            lastUpdatedAt: s_start + TimeSpan.FromSeconds(15),
            ttl: s_ttl);

        Assert.False(InMemoryMcpTaskStore.HasExpired(completed, s_start + TimeSpan.FromSeconds(20)));
        Assert.True(InMemoryMcpTaskStore.HasExpired(completed, s_start + TimeSpan.FromSeconds(25)));
    }

    [Fact]
    public void GetRequests_Null_ReturnsEmpty()
    {
        Assert.Same(ImmutableDictionary<string, InputRequest>.Empty, InMemoryMcpTaskStore.GetRequests(Info()));
    }

    [Fact]
    public void GetRequests_ImmutableDictionary_ReturnsSameInstance()
    {
        var requests = ImmutableDictionary<string, InputRequest>.Empty.Add("a", MakeRequest("x"));
        var result = InMemoryMcpTaskStore.GetRequests(Info(inputRequests: requests));
        Assert.Same(requests, result);
    }

    [Fact]
    public void GetRequests_MutableDictionary_CopiesEntries()
    {
        var requests = new Dictionary<string, InputRequest> { ["a"] = MakeRequest("x") };
        var result = InMemoryMcpTaskStore.GetRequests(Info(inputRequests: requests));
        Assert.False(ReferenceEquals(requests, result));
        Assert.True(result.ContainsKey("a"));
    }

    [Fact]
    public async Task SweepExpired_WithinInterval_IsNoOp()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        store.SweepExpired(clock.GetUtcNow(), CT);

        Assert.True(store._tasks.ContainsKey(created.TaskId));
    }

    [Fact]
    public async Task SweepExpired_AfterInterval_RemovesExpiredWorkingTask()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + s_ttl + s_sweepInterval);
        store.SweepExpired(clock.GetUtcNow(), CT);

        Assert.False(store._tasks.ContainsKey(created.TaskId));
    }

    [Fact]
    public async Task SweepExpired_CanceledToken_ThrowsDuringWalk()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, s_ttl);
        _ = await store.CreateTaskAsync(CT);

        clock.Advance(s_ttl + s_ttl + s_sweepInterval);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            store.SweepExpired(clock.GetUtcNow(), new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task SweepExpired_DoesNotRemoveActiveTask()
    {
        var clock = CreateClock();
        var store = CreateStore(clock, TimeSpan.FromMinutes(10));
        var created = await store.CreateTaskAsync(CT);

        clock.Advance(s_sweepInterval);
        store.SweepExpired(clock.GetUtcNow(), CT);

        Assert.True(store._tasks.ContainsKey(created.TaskId));
    }

    [Fact]
    public void TryUpdate_MissingKey_ReturnsFalse()
    {
        var store = new InMemoryMcpTaskStore();
        Assert.False(store.TryUpdate("missing", entry => entry with { StatusMessage = "x" }, CT));
    }

    [Fact]
    public async Task TryUpdate_SameInstance_ReturnsTrueWithoutWrite()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);
        var before = store._tasks[created.TaskId];

        Assert.True(store.TryUpdate(created.TaskId, entry => entry, CT));
        Assert.Same(before, store._tasks[created.TaskId]);
    }

    [Fact]
    public async Task TryUpdate_NewInstance_ReplacesSnapshot()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        Assert.True(store.TryUpdate(created.TaskId, entry => entry with { StatusMessage = "hi" }, CT));
        Assert.Equal("hi", store._tasks[created.TaskId].StatusMessage);
    }

    [Fact]
    public async Task TryUpdate_CanceledToken_ThrowsWithoutMutating()
    {
        var store = new InMemoryMcpTaskStore();
        var created = await store.CreateTaskAsync(CT);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            store.TryUpdate(created.TaskId, entry => entry with { StatusMessage = "nope" }, new CancellationToken(canceled: true)));

        Assert.Null(store._tasks[created.TaskId].StatusMessage);
    }

    [Fact]
    public void TimeProvider_AndSweepTicks_AreInitializedFromClock()
    {
        var clock = CreateClock();
        var store = CreateStore(clock);

        Assert.Same(clock, store._timeProvider);
        Assert.Equal(s_start.UtcTicks, store._lastSweepTicks);
        Assert.Equal(TimeSpan.FromSeconds(30).Ticks, InMemoryMcpTaskStore.s_sweepIntervalTicks);
    }

    private sealed class CancelOnNthUtcNowTimeProvider : TimeProvider
    {
        private readonly FakeTimeProvider _inner;
        private readonly CancellationTokenSource _cts;
        private readonly int _cancelOnCall;
        private int _calls;

        public CancelOnNthUtcNowTimeProvider(FakeTimeProvider inner, CancellationTokenSource cts, int cancelOnCall)
        {
            _inner = inner;
            _cts = cts;
            _cancelOnCall = cancelOnCall;
        }

        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Increment(ref _calls) >= _cancelOnCall)
            {
                _cts.Cancel();
            }

            return _inner.GetUtcNow();
        }

        public override long TimestampFrequency => _inner.TimestampFrequency;

        public override long GetTimestamp() => _inner.GetTimestamp();
    }
}
