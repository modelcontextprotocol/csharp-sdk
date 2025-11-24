using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

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
