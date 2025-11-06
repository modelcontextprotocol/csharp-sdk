using AspNetCoreMcpServer.EventStore;
using AspNetCoreMcpServer.Resources;
using AspNetCoreMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<EchoTool>()
    .WithTools<CollectUserInformationTool>() // this tool collect user information through elicitation
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

// adding InMemoryEventStore to support stream resumability and background cleanup service
builder.Services.TryAddSingleton<IEventStore, InMemoryEventStore>();
builder.Services.TryAddSingleton<IEventStoreCleaner, InMemoryEventStore>();
builder.Services.AddHostedService<EventStoreCleanupService>();

var app = builder.Build();

app.MapMcp();

app.Run();
