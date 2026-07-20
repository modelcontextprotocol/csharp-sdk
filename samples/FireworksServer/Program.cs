using FireworksServer;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

string transport = builder.Configuration["McpTransport"]?.ToLowerInvariant() ?? "http";
if (transport is not ("stdio" or "http"))
{
    throw new InvalidOperationException("McpTransport must be either 'stdio' or 'http'.");
}

string listenUrl = (builder.Configuration["ListenUrl"] ?? "http://127.0.0.1:5399").TrimEnd('/');
string dashboardUrl = (builder.Configuration["DashboardUrl"] ?? listenUrl).TrimEnd('/');
builder.WebHost.UseUrls(listenUrl);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSignalR();
builder.Services.AddSingleton(new FireworksSettings(dashboardUrl));
builder.Services.AddSingleton<ShowState>();

IMcpServerBuilder mcpServer = builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "fireworks-show-control", Version = "1.0.0" };
        options.ServerInstructions =
            "You control a synchronized fireworks stage. Use the choreograph_fireworks prompt to design a show, " +
            "then call launch_fireworks. Each launch renders as an MCP App and on the audience dashboard.";
    })
    .WithTools<FireworksTools>()
    .WithPrompts<FireworksPrompts>()
    .WithResources<FireworksResources>()
    .WithMcpApps();

if (transport == "stdio")
{
    mcpServer.WithStdioServerTransport();
}
else
{
    mcpServer.WithHttpTransport(options => options.Stateless = true);
}

var app = builder.Build();

app.MapGet("/", () =>
    Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"), "text/html"));
app.MapHub<FireworksHub>("/show");

if (transport == "http")
{
    app.MapMcp("/mcp");
}

await app.RunAsync();
