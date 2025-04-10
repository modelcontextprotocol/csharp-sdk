using TestServerWithHosting.Tools;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using AspNetCoreSseServer.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithTools<EchoTool>()
    .WithTools<SampleLlmTool>()
    .WithTools<LongRunningTool>();

var resource = ResourceBuilder.CreateEmpty().AddService("mcp.server");
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.SetResourceBuilder(resource)
        .AddOtlpExporter()
        .AddSource("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(b => b.SetResourceBuilder(resource)
        .AddMeter("*")
        .AddOtlpExporter()
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithLogging(b => b.SetResourceBuilder(resource)
        .AddOtlpExporter());

var app = builder.Build();

app.MapMcp();

app.Run();
