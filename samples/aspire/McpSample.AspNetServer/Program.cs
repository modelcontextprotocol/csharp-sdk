using McpSample.AspNetServer.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<EchoTool>()
    .WithTools<WeatherTool>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapMcp();

app.Run();
