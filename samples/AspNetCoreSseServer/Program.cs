using AspNetCoreSseServer.Tools;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TestServerWithHosting.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<EchoTool>()
    .WithTools<SampleLlmTool>()
    .WithTools<WeatherTools>();

builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(b => b.AddMeter("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithLogging()
    .UseOtlpExporter();
builder.Services.AddHttpClient();
var app = builder.Build();

app.MapMcp();

app.Run();
