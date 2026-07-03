using Logging.Tools;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Log streaming requires stateful mode because the server pushes log notifications
        // to clients. Set Stateless = false explicitly for forward compatibility.
        options.Stateless = false;
    })
    .WithTools<LoggingTools>();
    // .WithSetLoggingLevelHandler(async (ctx, ct) => new EmptyResult());

var app = builder.Build();

app.UseHttpsRedirection();

app.MapMcp();

app.Run();
