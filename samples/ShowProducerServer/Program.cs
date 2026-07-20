#pragma warning disable MCPEXP001 // Tasks (SEP-2663) are experimental.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ShowProducerServer;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var taskStore = new InMemoryMcpTaskStore { DefaultPollIntervalMs = 250 };

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "task-aware-show-producer", Version = "1.0.0" };
        options.ServerInstructions =
            "Use produce_show_package for a production-ready run sheet. With a normal client the tool runs inline. " +
            "A 2026-07-28 client that opts into io.modelcontextprotocol/tasks gets a pollable, cancellable task.";
    })
    .WithStdioServerTransport()
    .WithTools<ShowProducerTools>()
    .WithTasks(taskStore);

await builder.Build().RunAsync();
