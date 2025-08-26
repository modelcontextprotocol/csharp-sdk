using Progress.Tools;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<LongRunningTools>();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Information;
});

var app = builder.Build();

app.UseHttpsRedirection();

app.MapMcp();

app.Run();
