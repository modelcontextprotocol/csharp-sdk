using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

public static class ExperimentalJsonConverterTests
{
    [Fact]
    public static void Tool_WithExecution_RoundTrips()
    {
        var original = new Tool
        {
            Name = "test-tool",
            Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Tool>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("test-tool", deserialized.Name);
        Assert.NotNull(deserialized.Execution);
        Assert.Equal(ToolTaskSupport.Optional, deserialized.Execution.TaskSupport);
    }

    [Fact]
    public static void Tool_WithNullExecution_RoundTrips()
    {
        var original = new Tool { Name = "simple-tool" };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Tool>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Execution);
        Assert.DoesNotContain("execution", json);
    }

    [Fact]
    public static void ServerCapabilities_WithTasks_RoundTrips()
    {
        var original = new ServerCapabilities
        {
            Tasks = new McpTasksCapability()
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Tasks);
    }

    [Fact]
    public static void ServerCapabilities_WithNullTasks_OmitsProperty()
    {
        var original = new ServerCapabilities();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);

        Assert.DoesNotContain("tasks", json);
    }

    [Fact]
    public static void ClientCapabilities_WithTasks_RoundTrips()
    {
        var original = new ClientCapabilities
        {
            Tasks = new McpTasksCapability()
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Tasks);
    }

    [Fact]
    public static void CallToolResult_WithTask_RoundTrips()
    {
        var original = new CallToolResult
        {
            Task = new McpTask
            {
                TaskId = "task-123",
                Status = McpTaskStatus.Working,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
            }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CallToolResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Task);
        Assert.Equal("task-123", deserialized.Task.TaskId);
        Assert.Equal(McpTaskStatus.Working, deserialized.Task.Status);
    }

    [Fact]
    public static void CallToolRequestParams_WithTask_RoundTrips()
    {
        var original = new CallToolRequestParams
        {
            Name = "my-tool",
            Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(5) }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CallToolRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("my-tool", deserialized.Name);
        Assert.NotNull(deserialized.Task);
        Assert.Equal(TimeSpan.FromMinutes(5), deserialized.Task.TimeToLive);
    }

    [Fact]
    public static void CreateMessageRequestParams_WithTask_RoundTrips()
    {
        var original = new CreateMessageRequestParams
        {
            Messages = [],
            MaxTokens = 100,
            Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(10) }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Task);
        Assert.Equal(TimeSpan.FromMinutes(10), deserialized.Task.TimeToLive);
    }

    [Fact]
    public static void ElicitRequestParams_WithTask_RoundTrips()
    {
        var original = new ElicitRequestParams
        {
            Message = "test prompt",
            Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(15) }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Task);
        Assert.Equal(TimeSpan.FromMinutes(15), deserialized.Task.TimeToLive);
    }

    [Fact]
    public static void Tool_WithExecution_JsonPropertyNameIsCorrect()
    {
        var tool = new Tool
        {
            Name = "test",
            Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Required }
        };

        string json = JsonSerializer.Serialize(tool, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"execution\"", json);
        Assert.Contains("\"taskSupport\"", json);
    }

    [Fact]
    public static void Tool_WriteIndented_IsRespected()
    {
        var tool = new Tool
        {
            Name = "test",
            Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
        };

        // Use caller options with WriteIndented = true
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions) { WriteIndented = true };
        string json = JsonSerializer.Serialize(tool, options);

        // The output should be indented because WriteIndented is controlled by the writer
        Assert.Contains("\n", json);
    }
}
