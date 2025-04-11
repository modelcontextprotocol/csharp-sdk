using TestServerWithHosting.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<EchoTool>()
    .WithTools<SampleLlmTool>();

var app = builder.Build();

app.MapMcp();

app.Run();
