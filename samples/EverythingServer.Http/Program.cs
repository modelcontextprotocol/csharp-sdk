using EverythingServer.Core;
using ModelContextProtocol.Server;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Dictionary of session IDs to a set of resource URIs they are subscribed to
// The value is a ConcurrentDictionary used as a thread-safe HashSet
// because .NET does not have a built-in concurrent HashSet
ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> subscriptions = new();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Add a RunSessionHandler to remove all subscriptions for the session when it ends
        options.RunSessionHandler = async (httpContext, mcpServer, token) =>
        {
            if (mcpServer.SessionId == null)
            {
                // There is no sessionId if the serverOptions.Stateless is true
                await mcpServer.RunAsync(token);
                return;
            }
            try
            {
                subscriptions[mcpServer.SessionId] = new ConcurrentDictionary<string, byte>();
                // Start an instance of SubscriptionMessageSender for this session
                using var subscriptionSender = new SubscriptionMessageSender(mcpServer, subscriptions[mcpServer.SessionId]);
                await subscriptionSender.StartAsync(token);
                // Start an instance of LoggingUpdateMessageSender for this session
                using var loggingSender = new LoggingUpdateMessageSender(mcpServer);
                await loggingSender.StartAsync(token);
                await mcpServer.RunAsync(token);
            }
            finally
            {
                // This code runs when the session ends
                subscriptions.TryRemove(mcpServer.SessionId, out _);
            }
        };
    })
    .AddEverythingMcpHandlers(subscriptions);

ResourceBuilder resource = ResourceBuilder.CreateDefault().AddService("everything-server");
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("*").AddHttpClientInstrumentation().SetResourceBuilder(resource))
    .WithMetrics(b => b.AddMeter("*").AddHttpClientInstrumentation().SetResourceBuilder(resource))
    .WithLogging(b => b.SetResourceBuilder(resource))
    .UseOtlpExporter();

var app = builder.Build();

app.MapMcp();

app.Run();
