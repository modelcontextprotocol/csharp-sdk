using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ModelContextProtocol.AspNetCore;
using UrlRoutingSseServer.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransportAndRouting()
    .WithTools<EchoTool>()
    .WithTools<SampleLlmTool>()
    .WithTools<AdminTool>()
    .WithTools<WeatherTool>()
    .WithTools<MathTool>();

builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(b => b.AddMeter("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

app.MapMcpWithRouting("mcp");

app.Run();