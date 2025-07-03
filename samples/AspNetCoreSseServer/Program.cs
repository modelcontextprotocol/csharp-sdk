using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TestServerWithHosting.Tools;
using TestServerWithHosting.Resources;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<EchoTool>()
    .WithTools<SampleLlmTool>()
    .WithResources<SimpleResourceType>()
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
