using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(_ =>
{
    var client = new HttpClient { BaseAddress = new Uri("https://api.weather.gov") };
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WeatherAppServer", "1.0"));
    return client;
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "weather-app-server", Version = "1.0.0" };
        options.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Resources = new ResourcesCapability(),
        };
    })
    .WithHttpTransport()
    .WithTools<WeatherTools>()
    .WithResources<WeatherResources>()
    .WithMcpApps();

var app = builder.Build();
app.MapMcp("/mcp");
app.Run();
