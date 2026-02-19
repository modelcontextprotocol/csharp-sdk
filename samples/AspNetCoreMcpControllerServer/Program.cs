using AspNetCoreMcpControllerServer.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<EchoTool>();

var app = builder.Build();

app.MapControllers();

app.Run();
