using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ShowPlannerServer;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "stateless-show-planner", Version = "1.0.0" };
        options.ServerInstructions =
            "Use plan_show to collect production details interactively. The handler is stateless: " +
            "all progress crosses each retry in opaque requestState. surprise_me requests an LLM idea, " +
            "and unlock_grand_finale demonstrates live tool-list changes.";
    })
    .WithStdioServerTransport()
    .WithTools<ShowPlannerTools>();

await builder.Build().RunAsync();
