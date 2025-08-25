using Logging.Tools;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
        options.IdleTimeout = Timeout.InfiniteTimeSpan // Never timeout
    )
    .WithTools<LoggingTools>();
    // .WithSetLoggingLevelHandler(async (ctx, ct) => new EmptyResult());

var app = builder.Build();

app.UseHttpsRedirection();

app.MapMcp();

app.Run();
