using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Integration tests verifying that tools report correct ToolTaskSupport values
/// based on server configuration and method signatures.
/// </summary>
public class ToolTaskSupportTests : LoggedTest
{
    public ToolTaskSupportTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task Tools_WithoutTaskStore_ReportForbiddenTaskSupport()
    {
        // Arrange - Server without a task store
        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([
                    McpServerTool.Create(async (string input, CancellationToken ct) =>
                    {
                        await Task.Delay(10, ct);
                        return $"Async: {input}";
                    },
                    new McpServerToolCreateOptions { Name = "async-tool", Description = "An async tool" }),

                    McpServerTool.Create((string input) => $"Sync: {input}",
                    new McpServerToolCreateOptions { Name = "sync-tool", Description = "A sync tool" })
                ]);
            });

        // Act
        var tools = await fixture.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Both tools should have Forbidden task support when no task store is configured
        Assert.Equal(2, tools.Count);

        var asyncTool = tools.Single(t => t.Name == "async-tool");
        var syncTool = tools.Single(t => t.Name == "sync-tool");

        // Without a task store, async tools should still report Optional (their intrinsic capability)
        // but the server won't have tasks in capabilities. The tool itself declares its support.
        Assert.Equal(ToolTaskSupport.Optional, asyncTool.ProtocolTool.Execution?.TaskSupport);

        // Sync tools should have null Execution or Forbidden task support
        Assert.True(
            syncTool.ProtocolTool.Execution is null || 
            syncTool.ProtocolTool.Execution.TaskSupport is null ||
            syncTool.ProtocolTool.Execution.TaskSupport == ToolTaskSupport.Forbidden,
            "Sync tools should not support task execution");
    }

    [Fact]
    public async Task Tools_WithTaskStore_AsyncToolsReportOptionalTaskSupport()
    {
        // Arrange - Server with a task store
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([
                    McpServerTool.Create(async (string input, CancellationToken ct) =>
                    {
                        await Task.Delay(10, ct);
                        return $"Async: {input}";
                    },
                    new McpServerToolCreateOptions { Name = "async-tool", Description = "An async tool" }),

                    McpServerTool.Create((string input) => $"Sync: {input}",
                    new McpServerToolCreateOptions { Name = "sync-tool", Description = "A sync tool" })
                ]);
            },
            configureServices: services =>
            {
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            });

        // Act
        var tools = await fixture.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, tools.Count);

        var asyncTool = tools.Single(t => t.Name == "async-tool");
        var syncTool = tools.Single(t => t.Name == "sync-tool");

        // Async tools should report Optional task support
        Assert.Equal(ToolTaskSupport.Optional, asyncTool.ProtocolTool.Execution?.TaskSupport);

        // Sync tools should have null Execution or Forbidden task support
        Assert.True(
            syncTool.ProtocolTool.Execution is null ||
            syncTool.ProtocolTool.Execution.TaskSupport is null ||
            syncTool.ProtocolTool.Execution.TaskSupport == ToolTaskSupport.Forbidden,
            "Sync tools should not support task execution");
    }

    [Fact]
    public async Task Tools_WithExplicitTaskSupport_ReportsConfiguredValue()
    {
        // Arrange - Server with explicit task support configured on tools
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([
                    McpServerTool.Create(async (string input, CancellationToken ct) =>
                    {
                        await Task.Delay(10, ct);
                        return $"Async: {input}";
                    },
                    new McpServerToolCreateOptions 
                    { 
                        Name = "required-async-tool", 
                        Description = "A tool that requires task execution",
                        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Required }
                    }),

                    McpServerTool.Create((string input) => $"Sync: {input}",
                    new McpServerToolCreateOptions 
                    { 
                        Name = "forbidden-sync-tool", 
                        Description = "A tool that forbids task execution",
                        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Forbidden }
                    })
                ]);
            },
            configureServices: services =>
            {
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            });

        // Act
        var tools = await fixture.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, tools.Count);

        var requiredTool = tools.Single(t => t.Name == "required-async-tool");
        var forbiddenTool = tools.Single(t => t.Name == "forbidden-sync-tool");

        Assert.Equal(ToolTaskSupport.Required, requiredTool.ProtocolTool.Execution?.TaskSupport);
        Assert.Equal(ToolTaskSupport.Forbidden, forbiddenTool.ProtocolTool.Execution?.TaskSupport);
    }

    [Fact]
    public async Task ServerCapabilities_WithoutTaskStore_DoNotIncludeTasksCapability()
    {
        // Arrange - Server without a task store
        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([
                    McpServerTool.Create((string input) => $"Result: {input}",
                    new McpServerToolCreateOptions { Name = "test-tool" })
                ]);
            });

        // Assert - Server capabilities should not include tasks
        Assert.Null(fixture.Client.ServerCapabilities?.Tasks);
    }

    [Fact]
    public async Task ServerCapabilities_WithTaskStore_IncludeTasksCapability()
    {
        // Arrange - Server with a task store
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([
                    McpServerTool.Create((string input) => $"Result: {input}",
                    new McpServerToolCreateOptions { Name = "test-tool" })
                ]);
            },
            configureServices: services =>
            {
                services.Configure<McpServerOptions>(options =>
                {
                    options.TaskStore = taskStore;
                });
            });

        // Assert - Server capabilities should include tasks
        Assert.NotNull(fixture.Client.ServerCapabilities?.Tasks);
        Assert.NotNull(fixture.Client.ServerCapabilities.Tasks.List);
        Assert.NotNull(fixture.Client.ServerCapabilities.Tasks.Cancel);
        Assert.NotNull(fixture.Client.ServerCapabilities.Tasks.Requests?.Tools?.Call);
    }

#pragma warning disable MCPEXP001 // Tasks feature is experimental
    [Fact]
    public void McpServerToolAttribute_TaskSupport_CanBeSetOnAttribute()
    {
        // Test that the TaskSupport property can be set via the attribute
        // and is correctly read when creating a tool
        var tool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.RequiredTaskTool))!);
        Assert.NotNull(tool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Required, tool.ProtocolTool.Execution.TaskSupport);

        var optionalTool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.OptionalTaskTool))!);
        Assert.NotNull(optionalTool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Optional, optionalTool.ProtocolTool.Execution.TaskSupport);

        var forbiddenTool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.ForbiddenTaskTool))!);
        Assert.NotNull(forbiddenTool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Forbidden, forbiddenTool.ProtocolTool.Execution.TaskSupport);
    }

    [Fact]
    public void McpServerToolAttribute_TaskSupport_WhenNotSet_AllowsAutoDetection()
    {
        // When TaskSupport is not set on the attribute, async tools should use auto-detection (Optional)
        var asyncTool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.AsyncToolWithoutTaskSupport))!);
        Assert.NotNull(asyncTool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Optional, asyncTool.ProtocolTool.Execution.TaskSupport);

        // Sync tools without TaskSupport set should have null Execution or Forbidden
        var syncTool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.SyncToolWithoutTaskSupport))!);
        Assert.True(
            syncTool.ProtocolTool.Execution is null ||
            syncTool.ProtocolTool.Execution.TaskSupport is null ||
            syncTool.ProtocolTool.Execution.TaskSupport == ToolTaskSupport.Forbidden,
            "Sync tools without explicit TaskSupport should not support tasks");
    }

    [Fact]
    public void McpServerToolAttribute_TaskSupport_ExplicitForbidden_OverridesAutoDetection()
    {
        // Verify that explicitly setting Forbidden overrides auto-detection for async methods
        var forbiddenAsyncTool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.ForbiddenAsyncTool))!);
        Assert.NotNull(forbiddenAsyncTool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Forbidden, forbiddenAsyncTool.ProtocolTool.Execution.TaskSupport);
    }

    [Fact]
    public void McpServerToolAttribute_TaskSupport_OptionalOnSyncMethod_IsAllowed()
    {
        // Setting Optional on a sync method is allowed - the tool will just execute very quickly
        // This tests that the SDK doesn't prevent this configuration at tool creation time
        var tool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.OptionalTaskTool))!);
        Assert.NotNull(tool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Optional, tool.ProtocolTool.Execution.TaskSupport);
    }

    [Fact]
    public void McpServerToolAttribute_TaskSupport_RequiredOnSyncMethod_IsAllowed()
    {
        // Setting Required on a sync method is allowed - the tool will just execute very quickly
        // This tests that the SDK doesn't prevent this configuration at tool creation time
        var tool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.RequiredTaskTool))!);
        Assert.NotNull(tool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Required, tool.ProtocolTool.Execution.TaskSupport);
    }
#pragma warning restore MCPEXP001

#pragma warning disable MCPEXP001 // Tasks feature is experimental
    [Fact]
    public void McpServerToolAttribute_TaskSupport_WhenNotSet_DefaultsBasedOnMethodSignature()
    {
        // When TaskSupport is not set on the attribute, async tools should default to Optional
        var asyncTool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.AsyncToolWithoutTaskSupport))!);
        Assert.NotNull(asyncTool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Optional, asyncTool.ProtocolTool.Execution.TaskSupport);

        // Sync tools should have null or no Execution set
        var syncTool = McpServerTool.Create(typeof(TaskSupportAttributeTestTools).GetMethod(nameof(TaskSupportAttributeTestTools.SyncToolWithoutTaskSupport))!);
        Assert.True(
            syncTool.ProtocolTool.Execution is null ||
            syncTool.ProtocolTool.Execution.TaskSupport is null ||
            syncTool.ProtocolTool.Execution.TaskSupport == ToolTaskSupport.Forbidden,
            "Sync tools without explicit TaskSupport should not support tasks");
    }

    [Theory]
    [InlineData(ToolTaskSupport.Forbidden, "\"forbidden\"")]
    [InlineData(ToolTaskSupport.Optional, "\"optional\"")]
    [InlineData(ToolTaskSupport.Required, "\"required\"")]
    public void ToolTaskSupport_SerializesToJsonCorrectly(ToolTaskSupport value, string expectedJson)
    {
        var json = JsonSerializer.Serialize(value, McpJsonUtilities.DefaultOptions);
        Assert.Equal(expectedJson, json);
    }

    [Theory]
    [InlineData("\"forbidden\"", ToolTaskSupport.Forbidden)]
    [InlineData("\"optional\"", ToolTaskSupport.Optional)]
    [InlineData("\"required\"", ToolTaskSupport.Required)]
    public void ToolTaskSupport_DeserializesFromJsonCorrectly(string json, ToolTaskSupport expected)
    {
        var value = JsonSerializer.Deserialize<ToolTaskSupport>(json, McpJsonUtilities.DefaultOptions);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void ToolExecution_TaskSupport_NullByDefault()
    {
        // Verify that ToolExecution.TaskSupport is null by default
        var execution = new ToolExecution();
        Assert.Null(execution.TaskSupport);
        
        // When serialized with a value, it should appear correctly
        var tool = new Tool
        {
            Name = "test",
            Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
        };
        var toolJson = JsonSerializer.Serialize(tool, McpJsonUtilities.DefaultOptions);
        Assert.Contains("\"optional\"", toolJson);
    }

    [Fact]
    public void McpServerToolCreateOptions_Execution_OverridesAutoDetection()
    {
        // When Execution is set via options, it should override auto-detection
        var tool = McpServerTool.Create(
            async (string input, CancellationToken ct) =>
            {
                await Task.Delay(1, ct);
                return input;
            },
            new McpServerToolCreateOptions
            {
                Name = "test",
                Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Forbidden }
            });

        // Even though this is an async method, it should have Forbidden since it was explicitly set
        Assert.NotNull(tool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Forbidden, tool.ProtocolTool.Execution.TaskSupport);
    }

    [Fact]
    public void McpServerToolCreateOptions_Execution_Required_SetsCorrectly()
    {
        var tool = McpServerTool.Create(
            (string input) => input,
            new McpServerToolCreateOptions
            {
                Name = "test",
                Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Required }
            });

        Assert.NotNull(tool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Required, tool.ProtocolTool.Execution.TaskSupport);
    }

    [Fact]
    public void ToolTaskSupport_EnumValues_AreCorrect()
    {
        // Verify enum values are as expected (Forbidden = 0)
        Assert.Equal(0, (int)ToolTaskSupport.Forbidden);
        Assert.Equal(1, (int)ToolTaskSupport.Optional);
        Assert.Equal(2, (int)ToolTaskSupport.Required);
    }

    [Fact]
    public void McpServerToolAttribute_TaskSupport_PublicPropertyDefaultsToForbidden()
    {
        // Verify that the public property returns Forbidden when not set
        var attr = new McpServerToolAttribute();
        Assert.Equal(ToolTaskSupport.Forbidden, attr.TaskSupport);
    }
#pragma warning restore MCPEXP001

    [McpServerToolType]
    private static class TaskSupportAttributeTestTools
    {
#pragma warning disable MCPEXP001 // Tasks feature is experimental
        [McpServerTool(TaskSupport = ToolTaskSupport.Required)]
        public static string RequiredTaskTool(string input) => $"Required: {input}";

        [McpServerTool(TaskSupport = ToolTaskSupport.Optional)]
        public static string OptionalTaskTool(string input) => $"Optional: {input}";

        [McpServerTool(TaskSupport = ToolTaskSupport.Forbidden)]
        public static string ForbiddenTaskTool(string input) => $"Forbidden: {input}";

        [McpServerTool(TaskSupport = ToolTaskSupport.Forbidden)]
        public static async Task<string> ForbiddenAsyncTool(string input, CancellationToken ct)
        {
            await Task.Delay(1, ct);
            return $"ForbiddenAsync: {input}";
        }
#pragma warning restore MCPEXP001

        [McpServerTool]
        public static async Task<string> AsyncToolWithoutTaskSupport(string input, CancellationToken ct)
        {
            await Task.Delay(1, ct);
            return $"Async: {input}";
        }

        [McpServerTool]
        public static string SyncToolWithoutTaskSupport(string input) => $"Sync: {input}";
    }

    #region Sync Method with Optional/Required TaskSupport Integration Tests

#pragma warning disable MCPEXP001 // Tasks feature is experimental
    [Fact]
    public async Task SyncTool_WithOptionalTaskSupport_CanBeCalledAsTask()
    {
        // Arrange - Server with task store and a sync tool with Optional task support
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([McpServerTool.Create(
                    (string input) => $"Sync result: {input}",
                    new McpServerToolCreateOptions
                    {
                        Name = "optional-sync-tool",
                        Description = "A sync tool with optional task support",
                        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
                    })]);
            },
            configureServices: services =>
            {
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            });

        // Act - Call the sync tool as a task
        var mcpTask = await fixture.Client.CallToolAsTaskAsync(
            "optional-sync-tool",
            arguments: new Dictionary<string, object?> { ["input"] = "test" },
            taskMetadata: new McpTaskMetadata(),
            progress: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Task was created successfully
        Assert.NotNull(mcpTask);
        Assert.NotEmpty(mcpTask.TaskId);
    }

    [Fact]
    public async Task SyncTool_WithRequiredTaskSupport_CanBeCalledAsTask()
    {
        // Arrange - Server with task store and a sync tool with Required task support
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([McpServerTool.Create(
                    (string input) => $"Sync result: {input}",
                    new McpServerToolCreateOptions
                    {
                        Name = "required-sync-tool",
                        Description = "A sync tool with required task support",
                        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Required }
                    })]);
            },
            configureServices: services =>
            {
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            });

        // Act - Call the sync tool as a task
        var mcpTask = await fixture.Client.CallToolAsTaskAsync(
            "required-sync-tool",
            arguments: new Dictionary<string, object?> { ["input"] = "test" },
            taskMetadata: new McpTaskMetadata(),
            progress: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Task was created successfully
        Assert.NotNull(mcpTask);
        Assert.NotEmpty(mcpTask.TaskId);
    }

    [Fact]
    public async Task SyncTool_WithRequiredTaskSupport_CannotBeCalledDirectly()
    {
        // Arrange - Server with task store and a sync tool with Required task support
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([McpServerTool.Create(
                    (string input) => $"Sync result: {input}",
                    new McpServerToolCreateOptions
                    {
                        Name = "required-sync-tool",
                        Description = "A sync tool with required task support",
                        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Required }
                    })]);
            },
            configureServices: services =>
            {
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            });

        // Act & Assert - Calling directly should fail because task execution is required
        var exception = await Assert.ThrowsAsync<McpProtocolException>(() =>
            fixture.Client.CallToolAsync(
                "required-sync-tool",
                arguments: new Dictionary<string, object?> { ["input"] = "test" },
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        // The server returns InvalidParams because direct invocation is not allowed for required-task tools
        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("task", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskPath_Logs_Tool_Name_On_Successful_Call()
    {
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([McpServerTool.Create(
                    (string input) => $"Result: {input}",
                    new McpServerToolCreateOptions
                    {
                        Name = "task-success-tool",
                        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
                    })]);
            },
            configureServices: services =>
            {
                services.AddSingleton<ILoggerProvider>(MockLoggerProvider);
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            });

        var mcpTask = await fixture.Client.CallToolAsTaskAsync(
            "task-success-tool",
            arguments: new Dictionary<string, object?> { ["input"] = "test" },
            taskMetadata: new McpTaskMetadata(),
            progress: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(mcpTask);

        // Wait for the async task execution to complete
        await fixture.Client.GetTaskResultAsync(mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        var infoLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "\"task-success-tool\" completed. IsError = False.");
        Assert.Equal(LogLevel.Information, infoLog.LogLevel);
    }

    [Fact]
    public async Task TaskPath_Logs_Tool_Name_With_IsError_When_Tool_Returns_Error()
    {
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([McpServerTool.Create(
                    () => new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = "Task tool error" }],
                    },
                    new McpServerToolCreateOptions
                    {
                        Name = "task-error-result-tool",
                        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
                    })]);
            },
            configureServices: services =>
            {
                services.AddSingleton<ILoggerProvider>(MockLoggerProvider);
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            });

        var mcpTask = await fixture.Client.CallToolAsTaskAsync(
            "task-error-result-tool",
            taskMetadata: new McpTaskMetadata(),
            progress: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(mcpTask);

        // Wait for the async task execution to complete
        await fixture.Client.GetTaskResultAsync(mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        var infoLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "\"task-error-result-tool\" completed. IsError = True.");
        Assert.Equal(LogLevel.Information, infoLog.LogLevel);
    }

    [Fact]
    public async Task TaskPath_Logs_Error_When_Tool_Throws()
    {
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ClientServerFixture(
            LoggerFactory,
            configureServer: builder =>
            {
                builder.WithTools([McpServerTool.Create(
                    string () => throw new InvalidOperationException("Task tool error"),
                    new McpServerToolCreateOptions
                    {
                        Name = "task-throw-tool",
                        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
                    })]);
            },
            configureServices: services =>
            {
                services.AddSingleton<ILoggerProvider>(MockLoggerProvider);
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            });

        var mcpTask = await fixture.Client.CallToolAsTaskAsync(
            "task-throw-tool",
            taskMetadata: new McpTaskMetadata(),
            progress: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(mcpTask);

        // Wait for the async task execution to complete
        await fixture.Client.GetTaskResultAsync(mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        var errorLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.LogLevel == LogLevel.Error);
        Assert.Equal("\"task-throw-tool\" threw an unhandled exception.", errorLog.Message);
        Assert.IsType<InvalidOperationException>(errorLog.Exception);
    }
#pragma warning restore MCPEXP001

    #endregion

    /// <summary>
    /// A fixture that creates a connected MCP client-server pair for testing.
    /// </summary>
    private sealed class ClientServerFixture : IAsyncDisposable
    {
        private readonly System.IO.Pipelines.Pipe _clientToServerPipe = new();
        private readonly System.IO.Pipelines.Pipe _serverToClientPipe = new();
        private readonly CancellationTokenSource _cts;
        private readonly Task _serverTask;
        private readonly IServiceProvider _serviceProvider;

        public McpClient Client { get; }
        public McpServer Server { get; }

        public ClientServerFixture(
            ILoggerFactory loggerFactory,
            Action<IMcpServerBuilder>? configureServer,
            Action<IServiceCollection>? configureServices = null)
        {
            ServiceCollection sc = new();
            sc.AddLogging();

            var builder = sc
                .AddMcpServer()
                .WithStreamServerTransport(_clientToServerPipe.Reader.AsStream(), _serverToClientPipe.Writer.AsStream());

            configureServer?.Invoke(builder);
            configureServices?.Invoke(sc);

            _serviceProvider = sc.BuildServiceProvider(validateScopes: true);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

            Server = _serviceProvider.GetRequiredService<McpServer>();
            _serverTask = Server.RunAsync(_cts.Token);

            // Create client synchronously by blocking - this is test code
            Client = McpClient.CreateAsync(
                new StreamClientTransport(
                    serverInput: _clientToServerPipe.Writer.AsStream(),
                    _serverToClientPipe.Reader.AsStream(),
                    loggerFactory),
                loggerFactory: loggerFactory,
                cancellationToken: TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _cts.CancelAsync();

            _clientToServerPipe.Writer.Complete();
            _serverToClientPipe.Writer.Complete();

            await _serverTask;

            if (_serviceProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _cts.Dispose();
        }
    }
}
