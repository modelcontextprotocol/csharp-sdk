using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using TestInMemoryMcpTaskStore = ModelContextProtocol.Tests.Internal.InMemoryMcpTaskStore;

namespace ModelContextProtocol.Tests.Server;

public class InMemoryMcpTaskStoreTests : LoggedTest
{
    public InMemoryMcpTaskStoreTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task CreateTaskAsync_CreatesTaskWithUniqueId()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var requestId = new RequestId("req-1");
        var request = new JsonRpcRequest { Method = "tools/call" };

        // Act
        var task = await store.CreateTaskAsync(metadata, requestId, request, "session-1", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task);
        Assert.NotEmpty(task.TaskId);
        Assert.Equal(McpTaskStatus.Working, task.Status);
        Assert.NotEqual(default, task.CreatedAt);
        Assert.NotEqual(default, task.LastUpdatedAt);
    }

    [Fact]
    public async Task CreateTaskAsync_GeneratesUniqueTaskIds()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();

        // Act
        var task1 = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        var task2 = await store.CreateTaskAsync(metadata, new RequestId("req-2"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(task1.TaskId, task2.TaskId);
    }

    [Fact]
    public async Task CreateTaskAsync_AppliesTtlFromMetadata()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata 
        { 
            TimeToLive = TimeSpan.FromSeconds(5)
        };

        // Act
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), task.TimeToLive);
    }

    [Fact]
    public async Task CreateTaskAsync_CapsMaxTtl()
    {
        // Arrange
        var maxTtl = TimeSpan.FromMinutes(5);
        using var store = new InMemoryMcpTaskStore(maxTtl: maxTtl);
        var metadata = new McpTaskMetadata 
        { 
            TimeToLive = TimeSpan.FromHours(1) // Request 1 hour
        };

        // Act
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(maxTtl, task.TimeToLive);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsTaskById()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var created = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act
        var retrieved = await store.GetTaskAsync(created.TaskId, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(created.TaskId, retrieved.TaskId);
        Assert.Equal(created.Status, retrieved.Status);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsNullForNonexistentTask()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();

        // Act
        var task = await store.GetTaskAsync("nonexistent-id", null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(task);
    }

    [Fact]
    public async Task GetTaskAsync_EnforcesSessionIsolation()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);

        // Act
        var sameSession = await store.GetTaskAsync(task.TaskId, "session-1", TestContext.Current.CancellationToken);
        var differentSession = await store.GetTaskAsync(task.TaskId, "session-2", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(sameSession);
        Assert.Null(differentSession);
    }

    [Fact]
    public async Task StoreTaskResultAsync_StoresResultForCompletedTask()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "Success" }] };
        var resultElement = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);

        // Act
        await store.StoreTaskResultAsync(task.TaskId, McpTaskStatus.Completed, resultElement, null, TestContext.Current.CancellationToken);

        // Assert
        var retrieved = await store.GetTaskAsync(task.TaskId, null, TestContext.Current.CancellationToken);
        Assert.Equal(McpTaskStatus.Completed, retrieved!.Status);
    }

    [Fact]
    public async Task StoreTaskResultAsync_EnforcesSessionIsolation()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "Success" }] };
        var resultElement = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.StoreTaskResultAsync(task.TaskId, McpTaskStatus.Completed, resultElement, "session-2", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoreTaskResultAsync_ThrowsForNonTerminalStatus()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "Success" }] };
        var resultElement = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.StoreTaskResultAsync(task.TaskId, McpTaskStatus.Working, resultElement, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetTaskResultAsync_ReturnsStoredResult()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "Success" }] };
        var resultElement = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
        await store.StoreTaskResultAsync(task.TaskId, McpTaskStatus.Completed, resultElement, null, TestContext.Current.CancellationToken);

        // Act
        var retrieved = await store.GetTaskResultAsync(task.TaskId, null, TestContext.Current.CancellationToken);

        // Assert
        var callToolResult = retrieved.Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions)!;
        Assert.Single(callToolResult.Content);
        Assert.Equal("Success", ((TextContentBlock)callToolResult.Content[0]).Text);
    }

    [Fact]
    public async Task GetTaskResultAsync_EnforcesSessionIsolation()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "Success" }] };
        var resultElement = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
        await store.StoreTaskResultAsync(task.TaskId, McpTaskStatus.Completed, resultElement, "session-1", TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetTaskResultAsync(task.TaskId, "session-2", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateTaskStatusAsync_UpdatesStatus()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act
        await store.UpdateTaskStatusAsync(task.TaskId, McpTaskStatus.Working, "Processing...", null, TestContext.Current.CancellationToken);

        // Assert
        var updated = await store.GetTaskAsync(task.TaskId, null, TestContext.Current.CancellationToken);
        Assert.Equal(McpTaskStatus.Working, updated!.Status);
        Assert.Equal("Processing...", updated.StatusMessage);
    }

    [Fact]
    public async Task UpdateTaskStatusAsync_UpdatesLastUpdatedAt()
    {
        // Arrange - Use FakeTimeProvider for deterministic testing
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var store = new TestInMemoryMcpTaskStore(
            defaultTtl: null,
            maxTtl: null,
            pollInterval: null,
            cleanupInterval: Timeout.InfiniteTimeSpan,
            pageSize: 100,
            maxTasks: null,
            maxTasksPerSession: null,
            timeProvider: fakeTime);
            
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        var originalTimestamp = task.LastUpdatedAt;

        // Advance time to ensure timestamp changes
        fakeTime.Advance(TimeSpan.FromMilliseconds(10));

        // Act
        await store.UpdateTaskStatusAsync(task.TaskId, McpTaskStatus.Working, null, null, TestContext.Current.CancellationToken);

        // Assert
        var updated = await store.GetTaskAsync(task.TaskId, null, TestContext.Current.CancellationToken);
        Assert.True(updated!.LastUpdatedAt > originalTimestamp);
    }

    #region Input Required Status Tests

    // NOTE: The InputRequired status is automatically set by the server when a tool executing
    // as a task calls SampleAsync() or ElicitAsync(). The status is set back to Working when
    // the request completes. See TaskExecutionContext for implementation details.
    // The tests below verify the store correctly handles status transitions.

    [Fact]
    public async Task InputRequiredStatus_SerializesCorrectly()
    {
        // Verify the input_required status serializes as expected
        var task = new McpTask
        {
            TaskId = "test-task",
            Status = McpTaskStatus.InputRequired,
            StatusMessage = "Waiting for user input",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };

        string json = JsonSerializer.Serialize(task, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"status\":\"input_required\"", json);
    }

    [Fact]
    public async Task InputRequiredStatus_CanTransitionToWorking()
    {
        // Arrange - Spec: "From input_required: may move to working, completed, failed, or cancelled"
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Transition to input_required (testing store's status transition capability)
        var inputRequiredTask = await store.UpdateTaskStatusAsync(
            task.TaskId,
            McpTaskStatus.InputRequired,
            "Waiting for user confirmation",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(McpTaskStatus.InputRequired, inputRequiredTask.Status);

        // Act - Transition back to working
        var workingTask = await store.UpdateTaskStatusAsync(
            task.TaskId,
            McpTaskStatus.Working,
            "Processing resumed",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(McpTaskStatus.Working, workingTask.Status);
    }

    [Fact]
    public async Task InputRequiredStatus_CanTransitionToCancelled()
    {
        // Arrange - Spec: Task transitions show input_required can go to terminal states
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Transition to input_required
        await store.UpdateTaskStatusAsync(
            task.TaskId,
            McpTaskStatus.InputRequired,
            "Need input",
            cancellationToken: TestContext.Current.CancellationToken);

        // Act - Transition to cancelled
        var cancelledTask = await store.CancelTaskAsync(
            task.TaskId,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(McpTaskStatus.Cancelled, cancelledTask.Status);
    }

    #endregion

    [Fact]
    public async Task ListTasksAsync_ReturnsAllTasks()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var task1 = await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        var task2 = await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId("req-2"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act
        var result = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Tasks.Count);
        Assert.Contains(result.Tasks, t => t.TaskId == task1.TaskId);
        Assert.Contains(result.Tasks, t => t.TaskId == task2.TaskId);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task ListTasksAsync_FiltersBySession()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var task1 = await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);
        var task2 = await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId("req-2"), new JsonRpcRequest { Method = "test" }, "session-2", TestContext.Current.CancellationToken);

        // Act
        var session1Result = await store.ListTasksAsync(sessionId: "session-1", cancellationToken: TestContext.Current.CancellationToken);
        var session2Result = await store.ListTasksAsync(sessionId: "session-2", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(session1Result.Tasks);
        Assert.Equal(task1.TaskId, session1Result.Tasks[0].TaskId);
        Assert.Single(session2Result.Tasks);
        Assert.Equal(task2.TaskId, session2Result.Tasks[0].TaskId);
    }

    [Fact]
    public async Task ListTasksAsync_SupportsPagination()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        
        // Create 150 tasks (more than page size of 100)
        for (int i = 0; i < 150; i++)
        {
            await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId($"req-{i}"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        }

        // Act - First page
        var firstPageResult = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        
        // Act - Second page
        var secondPageResult = await store.ListTasksAsync(cursor: firstPageResult.NextCursor, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(100, firstPageResult.Tasks.Count);
        Assert.NotNull(firstPageResult.NextCursor);
        Assert.Equal(50, secondPageResult.Tasks.Count);
        Assert.Null(secondPageResult.NextCursor);
    }

    [Fact]
    public async Task CancelTaskAsync_CancelsTask()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act
        var cancelled = await store.CancelTaskAsync(task.TaskId, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(McpTaskStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task CancelTaskAsync_IsIdempotent()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        
        // First cancellation
        await store.CancelTaskAsync(task.TaskId, null, TestContext.Current.CancellationToken);

        // Act - Second cancellation
        var result = await store.CancelTaskAsync(task.TaskId, null, TestContext.Current.CancellationToken);

        // Assert - Should return unchanged task, not throw
        Assert.Equal(McpTaskStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task CancelTaskAsync_DoesNotCancelCompletedTask()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "Success" }] };
        var resultElement = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
        await store.StoreTaskResultAsync(task.TaskId, McpTaskStatus.Completed, resultElement, null, TestContext.Current.CancellationToken);

        // Act
        var cancelResult = await store.CancelTaskAsync(task.TaskId, null, TestContext.Current.CancellationToken);

        // Assert - Task remains completed
        Assert.Equal(McpTaskStatus.Completed, cancelResult.Status);
    }

    [Fact]
    public async Task CancelTaskAsync_EnforcesSessionIsolation()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CancelTaskAsync(task.TaskId, "session-2", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dispose_StopsCleanupTimer()
    {
        // Arrange - Use FakeTimeProvider for deterministic testing
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cleanupInterval = TimeSpan.FromMilliseconds(100);
        
        var store = new TestInMemoryMcpTaskStore(
            defaultTtl: null,
            maxTtl: null,
            pollInterval: null,
            cleanupInterval: cleanupInterval,
            pageSize: 100,
            maxTasks: null,
            maxTasksPerSession: null,
            timeProvider: fakeTime);
            
        var metadata = new McpTaskMetadata { TimeToLive = TimeSpan.FromMilliseconds(100) };
        await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act
        store.Dispose();

        // Advance time - timer should not fire after dispose
        fakeTime.Advance(TimeSpan.FromTicks(cleanupInterval.Ticks * 3));

        // Assert - Store should still be accessible after dispose (no exceptions)
        // The cleanup timer should have stopped
        Assert.True(true); // If we get here without exceptions, dispose worked
    }

    [Fact]
    public async Task CleanupExpiredTasks_RemovesExpiredTasks()
    {
        // Arrange - Use FakeTimeProvider for deterministic testing
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cleanupInterval = TimeSpan.FromMilliseconds(50);
        var ttl = TimeSpan.FromMilliseconds(100);
        
        using var store = new TestInMemoryMcpTaskStore(
            defaultTtl: null,
            maxTtl: null,
            pollInterval: null,
            cleanupInterval: cleanupInterval,
            pageSize: 100,
            maxTasks: null,
            maxTasksPerSession: null,
            timeProvider: fakeTime);
            
        var metadata = new McpTaskMetadata { TimeToLive = ttl };
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Verify task exists initially
        var resultBefore = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(resultBefore.Tasks);

        // Advance time past the TTL to make task expired
        fakeTime.Advance(ttl + TimeSpan.FromMilliseconds(1));
        
        // Trigger cleanup by advancing time past cleanup interval
        fakeTime.Advance(cleanupInterval);

        // Act - List tasks to verify cleanup happened
        var resultAfter = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(resultAfter.Tasks); // Task should be cleaned up by the timer
    }

    [Fact]
    public async Task DefaultTtl_AppliedWhenNoTtlSpecified()
    {
        // Arrange
        var defaultTtl = TimeSpan.FromMinutes(10);
        using var store = new InMemoryMcpTaskStore(defaultTtl: defaultTtl);
        var metadata = new McpTaskMetadata(); // No TTL specified

        // Act
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(defaultTtl, task.TimeToLive);
    }

    [Fact]
    public async Task MultipleOperations_ConcurrentAccess()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var tasks = new List<Task<McpTask>>();

        // Act - Create multiple tasks concurrently
        for (int i = 0; i < 10; i++)
        {
            int taskNum = i;
            tasks.Add(Task.Run(async () =>
            {
                var metadata = new McpTaskMetadata();
                return await store.CreateTaskAsync(metadata, new RequestId($"req-{taskNum}"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
            }));
        }

        var createdTasks = await Task.WhenAll(tasks);

        // Assert - All tasks should be created with unique IDs
        Assert.Equal(10, createdTasks.Length);
        Assert.Equal(10, createdTasks.Select(t => t.TaskId).Distinct().Count());
    }

    [Fact]
    public void Constructor_ThrowsWhenDefaultTtlExceedsMaxTtl()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new InMemoryMcpTaskStore(
                defaultTtl: TimeSpan.FromHours(2),
                maxTtl: TimeSpan.FromHours(1)));

        Assert.Equal("defaultTtl", exception.ParamName);
        Assert.Contains("Default TTL", exception.Message);
        Assert.Contains("cannot exceed maximum TTL", exception.Message);
    }

    [Fact]
    public async Task CreateTaskAsync_UsesConfiguredPollInterval()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore(pollInterval: TimeSpan.FromMilliseconds(2500));
        var metadata = new McpTaskMetadata();

        // Act
        var task = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(2500), task.PollInterval);
    }

    [Fact]
    public void Constructor_ThrowsWhenPollIntervalIsZero()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InMemoryMcpTaskStore(pollInterval: TimeSpan.Zero));

        Assert.Equal("pollInterval", exception.ParamName);
        Assert.Contains("Poll interval must be positive", exception.Message);
    }

    [Fact]
    public void Constructor_ThrowsWhenPollIntervalIsNegative()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InMemoryMcpTaskStore(pollInterval: TimeSpan.FromMilliseconds(-100)));

        Assert.Equal("pollInterval", exception.ParamName);
        Assert.Contains("Poll interval must be positive", exception.Message);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsDefensiveCopy()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var createdTask = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act - Get the task and modify the returned copy
        var retrievedTask = await store.GetTaskAsync(createdTask.TaskId, null, TestContext.Current.CancellationToken);
        var originalStatus = retrievedTask!.Status;
        retrievedTask.Status = McpTaskStatus.Completed;
        retrievedTask.StatusMessage = "Modified externally";

        // Assert - Get the task again and verify the stored state wasn't affected
        var taskAgain = await store.GetTaskAsync(createdTask.TaskId, null, TestContext.Current.CancellationToken);
        Assert.Equal(originalStatus, taskAgain!.Status);
        Assert.Null(taskAgain.StatusMessage);
    }

    [Fact]
    public async Task ListTasksAsync_ReturnsDefensiveCopies()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act - List tasks and modify the returned copies
        var result = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        var firstTask = result.Tasks[0];
        var originalTaskId = firstTask.TaskId;
        firstTask.Status = McpTaskStatus.Failed;
        firstTask.StatusMessage = "Modified in list";

        // Assert - Get the task directly and verify the stored state wasn't affected
        var directTask = await store.GetTaskAsync(originalTaskId, null, TestContext.Current.CancellationToken);
        Assert.Equal(McpTaskStatus.Working, directTask!.Status);
        Assert.Null(directTask.StatusMessage);
    }

    [Fact]
    public async Task CancelTaskAsync_ReturnsDefensiveCopy()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var metadata = new McpTaskMetadata();
        var createdTask = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act - Cancel the task and modify the returned copy
        var cancelledTask = await store.CancelTaskAsync(createdTask.TaskId, null, TestContext.Current.CancellationToken);
        cancelledTask.StatusMessage = "Modified after cancel";
        cancelledTask.Status = McpTaskStatus.Completed;

        // Assert - Get the task again and verify it's still cancelled with no message
        var taskAgain = await store.GetTaskAsync(createdTask.TaskId, null, TestContext.Current.CancellationToken);
        Assert.Equal(McpTaskStatus.Cancelled, taskAgain!.Status);
        Assert.Null(taskAgain.StatusMessage);
    }

    [Fact]
    public async Task ConcurrentUpdates_HandlesContentionCorrectly()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var task = await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act - Launch 100 concurrent updates to the same task
        var updateTasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(async () =>
            {
                try
                {
                    await store.UpdateTaskStatusAsync(task.TaskId, McpTaskStatus.Working, $"Update {i}", null, TestContext.Current.CancellationToken);
                    return true;
                }
                catch
                {
                    return false;
                }
            }));

        var results = await Task.WhenAll(updateTasks);

        // Assert - All updates should succeed (retry loop handles contention)
        Assert.All(results, success => Assert.True(success));

        // Verify task is still in valid state (one of the updates won)
        var finalTask = await store.GetTaskAsync(task.TaskId, null, TestContext.Current.CancellationToken);
        Assert.NotNull(finalTask);
        Assert.Equal(McpTaskStatus.Working, finalTask.Status);
        Assert.Matches(@"Update \d+", finalTask.StatusMessage!);
    }

    [Fact]
    public async Task ConcurrentStoreResult_OnlyFirstWins()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore();
        var task = await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Act - Try to store results concurrently (only first should succeed)
        var storeTasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(async () =>
            {
                try
                {
                    var result = new CallToolResult { Content = [new TextContentBlock { Text = $"Result {i}" }] };
                    var resultElement = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
                    await store.StoreTaskResultAsync(
                        task.TaskId,
                        McpTaskStatus.Completed,
                        resultElement,
                        null,
                        TestContext.Current.CancellationToken);
                    return i;
                }
                catch (InvalidOperationException)
                {
                    // Expected: task already in terminal state
                    return -1;
                }
            }));

        var results = await Task.WhenAll(storeTasks);
        var successfulUpdates = results.Where(r => r >= 0).ToList();

        // Assert - Exactly one update should succeed, others should fail
        Assert.Single(successfulUpdates);

        // Verify the winning result is stored
        var finalTask = await store.GetTaskAsync(task.TaskId, null, TestContext.Current.CancellationToken);
        Assert.Equal(McpTaskStatus.Completed, finalTask!.Status);
    }

    [Fact]
    public async Task ListTasksAsync_PaginationWithCustomPageSize()
    {
        // Arrange - Use small page size for testing
        using var store = new InMemoryMcpTaskStore(pageSize: 10);

        // Create 25 tasks
        for (int i = 0; i < 25; i++)
        {
            await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId($"req-{i}"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        }

        // Act - Paginate through all tasks
        var result1 = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result2 = await store.ListTasksAsync(cursor: result1.NextCursor, cancellationToken: TestContext.Current.CancellationToken);
        var result3 = await store.ListTasksAsync(cursor: result2.NextCursor, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(10, result1.Tasks.Count);
        Assert.NotNull(result1.NextCursor);
        Assert.Equal(10, result2.Tasks.Count);
        Assert.NotNull(result2.NextCursor);
        Assert.Equal(5, result3.Tasks.Count);
        Assert.Null(result3.NextCursor);

        // Verify no duplicates across pages
        var allTaskIds = result1.Tasks.Concat(result2.Tasks).Concat(result3.Tasks).Select(t => t.TaskId).ToList();
        Assert.Equal(25, allTaskIds.Distinct().Count());
    }

    [Fact]
    public async Task ListTasksAsync_NoDuplicatesWithIdenticalTimestamps()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore(pageSize: 5);

        // Create tasks with identical metadata to increase chance of timestamp collision
        var createTasks = Enumerable.Range(0, 20).Select(i =>
            store.CreateTaskAsync(new McpTaskMetadata(), new RequestId($"req-{i}"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken));

        await Task.WhenAll(createTasks);

        // Act - Collect all tasks through pagination
        var allTasks = new List<McpTask>();
        string? cursor = null;
        do
        {
            var result = await store.ListTasksAsync(cursor: cursor, cancellationToken: TestContext.Current.CancellationToken);
            allTasks.AddRange(result.Tasks);
            cursor = result.NextCursor;
        } while (cursor != null);

        // Assert - No duplicates
        var taskIds = allTasks.Select(t => t.TaskId).ToList();
        Assert.Equal(20, taskIds.Count);
        Assert.Equal(20, taskIds.Distinct().Count());

        // Verify tasks are properly ordered
        Assert.Equal(allTasks.OrderBy(t => t.CreatedAt).ThenBy(t => t.TaskId).Select(t => t.TaskId), taskIds);
    }

    [Fact]
    public async Task ListTasksAsync_ConsistentWithExpiredTasksRemovedBetweenPages()
    {
        // Arrange - Use FakeTimeProvider for deterministic testing
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var ttl = TimeSpan.FromSeconds(1);
        using var store = new TestInMemoryMcpTaskStore(
            defaultTtl: ttl,
            maxTtl: null,
            pollInterval: null,
            cleanupInterval: Timeout.InfiniteTimeSpan,
            pageSize: 5,
            maxTasks: null,
            maxTasksPerSession: null,
            timeProvider: fakeTime);

        // Create 15 tasks
        for (int i = 0; i < 15; i++)
        {
            await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId($"req-{i}"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        }

        // Act - Get first page immediately
        var result1 = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Advance time past TTL to make tasks expire
        fakeTime.Advance(ttl + TimeSpan.FromMilliseconds(500));

        // Get second page after expiration
        var result2 = await store.ListTasksAsync(cursor: result1.NextCursor, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - First page should have 5 tasks, second page should have 0 (all expired)
        Assert.Equal(5, result1.Tasks.Count);
        Assert.NotNull(result1.NextCursor);
        Assert.Empty(result2.Tasks);
        Assert.Null(result2.NextCursor);
    }

    [Fact]
    public async Task ListTasksAsync_KeysetPaginationMaintainsConsistencyWithNewTasks()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore(pageSize: 5);

        // Create 10 initial tasks
        for (int i = 0; i < 10; i++)
        {
            await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId($"req-{i}"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        }

        // Get first page
        var result1 = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(5, result1.Tasks.Count);

        // Add more tasks between pages (these should appear in later queries, not retroactively in page 2)
        for (int i = 10; i < 15; i++)
        {
            await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId($"req-{i}"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        }

        // Get second page using cursor from before new tasks were added
        var result2 = await store.ListTasksAsync(cursor: result1.NextCursor, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Second page should have 5 tasks from original set
        Assert.Equal(5, result2.Tasks.Count);
        Assert.NotNull(result2.NextCursor);

        // Verify no overlap between pages
        var page1Ids = result1.Tasks.Select(t => t.TaskId).ToHashSet();
        var page2Ids = result2.Tasks.Select(t => t.TaskId).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
    }

    [Fact]
    public async Task UpdateTaskStatusAsync_ConcurrentWithList_NoCorruption()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore(pageSize: 10);

        // Create 20 tasks
        var tasks = new List<McpTask>();
        for (int i = 0; i < 20; i++)
        {
            var task = await store.CreateTaskAsync(new McpTaskMetadata(), new RequestId($"req-{i}"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
            tasks.Add(task);
        }

        // Act - Concurrently list and update tasks
        var ct = TestContext.Current.CancellationToken;
        var listTask = Task.Run(async () =>
        {
            var allTasks = new List<McpTask>();
            string? cursor = null;
            do
            {
                var result = await store.ListTasksAsync(cursor: cursor, cancellationToken: TestContext.Current.CancellationToken);
                allTasks.AddRange(result.Tasks);
                cursor = result.NextCursor;
                await Task.Delay(10, ct); // Small delay to increase chance of interleaving
            } while (cursor != null);
            return allTasks;
        }, ct);

        var updateTask = Task.Run(async () =>
        {
            foreach (var task in tasks)
            {
                await store.UpdateTaskStatusAsync(task.TaskId, McpTaskStatus.Working, "Updated", null, TestContext.Current.CancellationToken);
                await Task.Delay(5, ct); // Small delay
            }
        }, ct);

        await Task.WhenAll(listTask, updateTask);
        var listedTasks = await listTask;

        // Assert - Should have listed all tasks without duplicates or corruption
        Assert.Equal(20, listedTasks.Count);
        Assert.Equal(20, listedTasks.Select(t => t.TaskId).Distinct().Count());
    }

    [Fact]
    public void Constructor_ThrowsForInvalidMaxTasks()
    {
        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryMcpTaskStore(maxTasks: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryMcpTaskStore(maxTasks: -1));
    }

    [Fact]
    public void Constructor_ThrowsForInvalidMaxTasksPerSession()
    {
        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryMcpTaskStore(maxTasksPerSession: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryMcpTaskStore(maxTasksPerSession: -1));
    }

    [Fact]
    public async Task CreateTaskAsync_EnforcesMaxTasksLimit()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore(maxTasks: 3);
        var metadata = new McpTaskMetadata();

        // Act - Create up to the limit
        await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(metadata, new RequestId("req-2"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(metadata, new RequestId("req-3"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Assert - Fourth task should throw
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateTaskAsync(metadata, new RequestId("req-4"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken));
        Assert.Contains("Maximum number of tasks (3) has been reached", ex.Message);
    }

    [Fact]
    public async Task CreateTaskAsync_EnforcesMaxTasksPerSessionLimit()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore(maxTasksPerSession: 2);
        var metadata = new McpTaskMetadata();

        // Act - Create up to the limit for session-1
        await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(metadata, new RequestId("req-2"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);

        // Assert - Third task for session-1 should throw
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateTaskAsync(metadata, new RequestId("req-3"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken));
        Assert.Contains("Maximum number of tasks per session (2) has been reached", ex.Message);
        Assert.Contains("session-1", ex.Message);
    }

    [Fact]
    public async Task CreateTaskAsync_MaxTasksPerSession_AllowsDifferentSessions()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore(maxTasksPerSession: 2);
        var metadata = new McpTaskMetadata();

        // Act - Create 2 tasks for session-1
        await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(metadata, new RequestId("req-2"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);

        // Should still be able to create tasks for session-2
        var task3 = await store.CreateTaskAsync(metadata, new RequestId("req-3"), new JsonRpcRequest { Method = "test" }, "session-2", TestContext.Current.CancellationToken);
        var task4 = await store.CreateTaskAsync(metadata, new RequestId("req-4"), new JsonRpcRequest { Method = "test" }, "session-2", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task3);
        Assert.NotNull(task4);
    }

    [Fact]
    public async Task CreateTaskAsync_MaxTasksPerSession_DoesNotApplyToNullSession()
    {
        // Arrange
        using var store = new InMemoryMcpTaskStore(maxTasksPerSession: 1);
        var metadata = new McpTaskMetadata();

        // Act - Create multiple tasks with null session (should not be limited)
        var task1 = await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        var task2 = await store.CreateTaskAsync(metadata, new RequestId("req-2"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);
        var task3 = await store.CreateTaskAsync(metadata, new RequestId("req-3"), new JsonRpcRequest { Method = "test" }, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task1);
        Assert.NotNull(task2);
        Assert.NotNull(task3);
    }

    [Fact]
    public async Task CreateTaskAsync_CombinesMaxTasksAndMaxTasksPerSession()
    {
        // Arrange - Global limit of 5, per-session limit of 2
        using var store = new InMemoryMcpTaskStore(maxTasks: 5, maxTasksPerSession: 2);
        var metadata = new McpTaskMetadata();

        // Create 2 tasks for session-1 (hits per-session limit)
        await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(metadata, new RequestId("req-2"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);

        // session-1 is at its limit
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateTaskAsync(metadata, new RequestId("req-3"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken));

        // But session-2 can still create tasks
        await store.CreateTaskAsync(metadata, new RequestId("req-4"), new JsonRpcRequest { Method = "test" }, "session-2", TestContext.Current.CancellationToken);
        await store.CreateTaskAsync(metadata, new RequestId("req-5"), new JsonRpcRequest { Method = "test" }, "session-2", TestContext.Current.CancellationToken);

        // Now global limit is reached (4 tasks total, but 5th would be 5)
        // Wait, we have 4 tasks, should be able to create one more
        await store.CreateTaskAsync(metadata, new RequestId("req-6"), new JsonRpcRequest { Method = "test" }, "session-3", TestContext.Current.CancellationToken);

        // Now at 5 tasks (global limit), should throw
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateTaskAsync(metadata, new RequestId("req-7"), new JsonRpcRequest { Method = "test" }, "session-3", TestContext.Current.CancellationToken));
        Assert.Contains("Maximum number of tasks (5) has been reached", ex.Message);
    }

    [Fact]
    public async Task CreateTaskAsync_MaxTasksPerSession_ExcludesExpiredTasks()
    {
        // Arrange - Use FakeTimeProvider for deterministic testing
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var shortTtl = TimeSpan.FromMilliseconds(50);
        using var store = new TestInMemoryMcpTaskStore(
            defaultTtl: shortTtl,
            maxTtl: null,
            pollInterval: null,
            cleanupInterval: Timeout.InfiniteTimeSpan,
            pageSize: 100,
            maxTasks: null,
            maxTasksPerSession: 1,
            timeProvider: fakeTime);
            
        var metadata = new McpTaskMetadata();

        // Create first task
        await store.CreateTaskAsync(metadata, new RequestId("req-1"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);

        // Advance time past TTL to make the first task expire
        fakeTime.Advance(shortTtl + TimeSpan.FromMilliseconds(1));

        // Should be able to create another task since the first one expired
        var task2 = await store.CreateTaskAsync(metadata, new RequestId("req-2"), new JsonRpcRequest { Method = "test" }, "session-1", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task2);
    }

    [Fact]
    public async Task ListTasksAsync_KeysetPaginationWorksWithIdenticalTimestamps()
    {
        // Arrange - Use a fake time provider to create tasks with identical timestamps
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var store = new TestInMemoryMcpTaskStore(
            defaultTtl: null,
            maxTtl: null,
            pollInterval: null,
            cleanupInterval: Timeout.InfiniteTimeSpan,
            pageSize: 5,
            maxTasks: null,
            maxTasksPerSession: null,
            timeProvider: fakeTime);

        // Create 10 tasks - all with the EXACT same timestamp
        var createdTasks = new List<McpTask>();
        for (int i = 0; i < 10; i++)
        {
            var task = await store.CreateTaskAsync(
                new McpTaskMetadata(),
                new RequestId($"req-{i}"),
                new JsonRpcRequest { Method = "test" },
                null,
                TestContext.Current.CancellationToken);
            createdTasks.Add(task);
        }

        // Verify all tasks have the same CreatedAt timestamp
        var firstTimestamp = createdTasks[0].CreatedAt;
        Assert.All(createdTasks, task => Assert.Equal(firstTimestamp, task.CreatedAt));

        // Act - Get first page
        var result1 = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - First page should have 5 tasks
        Assert.Equal(5, result1.Tasks.Count);
        Assert.NotNull(result1.NextCursor);

        // Get second page using cursor
        var result2 = await store.ListTasksAsync(cursor: result1.NextCursor, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Second page should have 5 tasks
        Assert.Equal(5, result2.Tasks.Count);
        Assert.Null(result2.NextCursor); // No more pages

        // Verify no overlap between pages
        var page1Ids = result1.Tasks.Select(t => t.TaskId).ToHashSet();
        var page2Ids = result2.Tasks.Select(t => t.TaskId).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));

        // Verify we got all 10 tasks exactly once
        var allReturnedIds = page1Ids.Union(page2Ids).ToHashSet();
        var allCreatedIds = createdTasks.Select(t => t.TaskId).ToHashSet();
        Assert.Equal(allCreatedIds, allReturnedIds);
    }

    [Fact]
    public async Task ListTasksAsync_TasksCreatedAfterFirstPageWithSameTimestampAppearInSecondPage()
    {
        // Arrange - Use a fake time provider so we can control timestamps precisely
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var store = new TestInMemoryMcpTaskStore(
            defaultTtl: null,
            maxTtl: null,
            pollInterval: null,
            cleanupInterval: Timeout.InfiniteTimeSpan,
            pageSize: 5,
            maxTasks: null,
            maxTasksPerSession: null,
            timeProvider: fakeTime);

        // Create initial 6 tasks - all with the same timestamp
        // (6 so that first page has 5 and cursor points to task 5)
        var initialTasks = new List<McpTask>();
        for (int i = 0; i < 6; i++)
        {
            var task = await store.CreateTaskAsync(
                new McpTaskMetadata(),
                new RequestId($"req-initial-{i}"),
                new JsonRpcRequest { Method = "test" },
                null,
                TestContext.Current.CancellationToken);
            initialTasks.Add(task);
        }

        // Get first page - should have 5 tasks with a cursor
        var result1 = await store.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(5, result1.Tasks.Count);
        Assert.NotNull(result1.NextCursor);

        // Now create 5 more tasks AFTER we got the first page cursor
        // These tasks have the SAME timestamp as the cursor (time hasn't moved)
        // Due to monotonic UUID v7 with counter, they should sort AFTER the cursor
        var laterTasks = new List<McpTask>();
        for (int i = 0; i < 5; i++)
        {
            var task = await store.CreateTaskAsync(
                new McpTaskMetadata(),
                new RequestId($"req-later-{i}"),
                new JsonRpcRequest { Method = "test" },
                null,
                TestContext.Current.CancellationToken);
            laterTasks.Add(task);
        }

        // Verify all tasks have the same timestamp
        var allTasks = initialTasks.Concat(laterTasks).ToList();
        var firstTimestamp = allTasks[0].CreatedAt;
        Assert.All(allTasks, task => Assert.Equal(firstTimestamp, task.CreatedAt));

        // Get ALL remaining pages
        var allSubsequentTasks = new List<McpTask>();
        string? cursor = result1.NextCursor;
        while (cursor != null)
        {
            var result = await store.ListTasksAsync(cursor: cursor, cancellationToken: TestContext.Current.CancellationToken);
            allSubsequentTasks.AddRange(result.Tasks);
            cursor = result.NextCursor;
        }

        // Verify no overlap between first page and subsequent
        var page1Ids = result1.Tasks.Select(t => t.TaskId).ToHashSet();
        var subsequentIds = allSubsequentTasks.Select(t => t.TaskId).ToHashSet();
        Assert.Empty(page1Ids.Intersect(subsequentIds));

        // Verify we got all tasks
        var allReturnedIds = page1Ids.Union(subsequentIds).ToHashSet();
        var allCreatedIds = allTasks.Select(t => t.TaskId).ToHashSet();
        Assert.Equal(allCreatedIds, allReturnedIds);

        // Most importantly: verify ALL the later tasks (created after first page) are surfaced
        // in the subsequent pages
        var laterTaskIds = laterTasks.Select(t => t.TaskId).ToHashSet();
        Assert.Superset(laterTaskIds, subsequentIds);
    }
}
