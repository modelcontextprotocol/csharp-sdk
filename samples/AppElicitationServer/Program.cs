using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;

Console.WriteLine("Configuring the stateless MCP app-elicitation server...");
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer(options =>
    {
        options.ProtocolVersion = "2026-07-28";
        options.ServerInfo = new Implementation { Name = "app-elicitation-server", Version = "0.1.0" };
        options.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Resources = new ResourcesCapability(),
        };
    })
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<PortfolioTools>()
    .WithResources<PortfolioResources>()
    .WithMcpApps();

var app = builder.Build();
app.MapMcp("/mcp");
Console.WriteLine("Listening on http://localhost:5100/mcp");
app.Run("http://localhost:5100");
