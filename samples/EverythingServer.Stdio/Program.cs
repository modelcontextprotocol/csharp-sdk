using EverythingServer.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Collections.Concurrent;

var builder = Host.CreateApplicationBuilder(args);

// Dictionary of session IDs to a set of resource URIs they are subscribed to
// The value is a ConcurrentDictionary used as a thread-safe HashSet
// because .NET does not have a built-in concurrent HashSet
// For stdio mode, we use a single "stdio" key since there's only one session
ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> subscriptions = new();
var stdioSubscriptions = new ConcurrentDictionary<string, byte>();
subscriptions["stdio"] = stdioSubscriptions;

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .AddEverythingMcpHandlers(subscriptions);

// Register background services for stdio mode
builder.Services.AddHostedService(sp => new SubscriptionMessageSender(
    sp.GetRequiredService<McpServer>(),
    stdioSubscriptions));
builder.Services.AddHostedService(sp => new LoggingUpdateMessageSender(
    sp.GetRequiredService<McpServer>()));

// Configure logging to write to stderr to avoid interfering with MCP protocol on stdout
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

ResourceBuilder resource = ResourceBuilder.CreateDefault().AddService("everything-server");
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("*").AddHttpClientInstrumentation().SetResourceBuilder(resource))
    .WithMetrics(b => b.AddMeter("*").AddHttpClientInstrumentation().SetResourceBuilder(resource))
    .WithLogging(b => b.SetResourceBuilder(resource))
    .UseOtlpExporter();

var app = builder.Build();

await app.RunAsync();
