using Elicitation.Tools;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Elicitation requires stateful mode because it sends server-to-client requests.
        // Set SessionMode = HttpServerSessionMode.Stateful since it's required.
        options.SessionMode = HttpServerSessionMode.Stateful;
    })
    .WithTools<InteractiveTools>();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Information;
});

var app = builder.Build();

app.UseHttpsRedirection();

app.MapMcp();

app.Run();
