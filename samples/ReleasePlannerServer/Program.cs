using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ReleasePlannerServer;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "stateless-release-planner", Version = "1.0.0" };
        options.ServerInstructions =
            "Use plan_release to collect deployment details and approval interactively. The handler is stateless: " +
            "all progress crosses each retry in opaque requestState. unlock_production_deploy demonstrates " +
            "live tool-list changes without touching a real environment.";
    })
    .WithStdioServerTransport()
    .WithTools<ReleasePlannerTools>();

await builder.Build().RunAsync();
