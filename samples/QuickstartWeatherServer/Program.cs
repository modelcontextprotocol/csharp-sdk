using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

//builder.Logging.ClearProviders();
//builder.Logging.AddFilter("Microsoft", LogLevel.Warning); // Adjust the log level as needed

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

await app.RunAsync();
