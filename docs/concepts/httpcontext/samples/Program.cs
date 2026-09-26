using HttpContext.Tools;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.SessionMode = HttpServerSessionMode.Stateless;
    })
    .WithTools<ContextTools>();

// <snippet_AddHttpContextAccessor>
builder.Services.AddHttpContextAccessor();
// </snippet_AddHttpContextAccessor>

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Information;
});

var app = builder.Build();

app.UseHttpsRedirection();

app.MapMcp();

app.Run();
