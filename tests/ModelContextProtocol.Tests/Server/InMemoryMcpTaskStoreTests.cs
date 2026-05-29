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
    public async Task CreateTaskAsync_UsesDefaultTtl()
    {
        var store = new InMemoryMcpTaskStore { DefaultTtlMs = 30000 };

        var result = await store.CreateTaskAsync(CT);

        Assert.Equal(30000, result.TtlMs);
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

        var requests = new Dictionary<string, JsonElement>
        {
            ["req1"] = JsonDocument.Parse("""{"method":"elicitation/create","params":{"message":"hello"}}""").RootElement.Clone()
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

        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, JsonElement>
        {
            ["req1"] = JsonSerializer.SerializeToElement("first", McpJsonUtilities.DefaultOptions)
        }, CT);
        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, JsonElement>
        {
            ["req2"] = JsonSerializer.SerializeToElement("second", McpJsonUtilities.DefaultOptions)
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

        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, JsonElement>
        {
            ["req1"] = JsonSerializer.SerializeToElement("request1", McpJsonUtilities.DefaultOptions),
            ["req2"] = JsonSerializer.SerializeToElement("request2", McpJsonUtilities.DefaultOptions),
        }, CT);

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, JsonElement>
        {
            ["req1"] = JsonSerializer.SerializeToElement("response1", McpJsonUtilities.DefaultOptions),
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

        await store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, JsonElement>
        {
            ["req1"] = JsonSerializer.SerializeToElement("request1", McpJsonUtilities.DefaultOptions),
        }, CT);

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, JsonElement>
        {
            ["req1"] = JsonSerializer.SerializeToElement("response1", McpJsonUtilities.DefaultOptions),
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
            store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, JsonElement>
            {
                [$"req{i}"] = JsonSerializer.SerializeToElement($"value{i}", McpJsonUtilities.DefaultOptions)
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

        await store.ResolveInputRequestsAsync(created.TaskId, new Dictionary<string, JsonElement>
        {
            ["unknown-key"] = JsonSerializer.SerializeToElement("response", McpJsonUtilities.DefaultOptions),
        }, CT);

        var task = await store.GetTaskAsync(created.TaskId, CT);
        Assert.NotNull(task);
        Assert.Equal(McpTaskStatus.Working, task.Status);
    }
}
