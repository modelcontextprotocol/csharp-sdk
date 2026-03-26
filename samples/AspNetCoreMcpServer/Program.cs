using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using AspNetCoreMcpServer.Tools;
using AspNetCoreMcpServer.Resources;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
// Note: This sample uses SampleLlmTool which calls server.AsSamplingChatClient() to send
// a server-to-client sampling request. This requires stateful (session-based) mode, which
// is the default. See https://csharp.sdk.modelcontextprotocol.io/concepts/sessions for details
// on when to prefer stateless mode instead.
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<EchoTool>()
    .WithTools<SampleLlmTool>()
    .WithTools<WeatherTools>()
    .WithResources<SimpleResourceType>();

builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(b => b.AddMeter("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithLogging()
    .UseOtlpExporter();

// Configure HttpClientFactory for weather.gov API
builder.Services.AddHttpClient("WeatherApi", client =>
{
    client.BaseAddress = new Uri("https://api.weather.gov");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
});

var app = builder.Build();

app.MapMcp();

app.Run();
