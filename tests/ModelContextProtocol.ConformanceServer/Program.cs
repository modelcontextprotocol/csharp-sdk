using ConformanceServer.Prompts;
using ConformanceServer.Resources;
using ConformanceServer.Tools;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ModelContextProtocol.ConformanceServer;

public class Program
{
    public static async Task MainAsync(string[] args, ILoggerProvider? loggerProvider = null, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (loggerProvider != null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(loggerProvider);
        }

        // Dictionary of session IDs to a set of resource URIs they are subscribed to
        // The value is a ConcurrentDictionary used as a thread-safe HashSet
        // because .NET does not have a built-in concurrent HashSet
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> subscriptions = new();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<ConformanceTools>()
            .WithPrompts<ConformancePrompts>()
            .WithResources<ConformanceResources>()
            .WithSubscribeToResourcesHandler(async (ctx, ct) =>
            {
                if (ctx.Server.SessionId == null)
                {
                    throw new McpException("Cannot add subscription for server with null SessionId");
                }
                if (ctx.Params?.Uri is { } uri)
                {
                    var sessionSubscriptions = subscriptions.GetOrAdd(ctx.Server.SessionId, _ => new());
                    sessionSubscriptions.TryAdd(uri, 0);
                }

                return new EmptyResult();
            })
            .WithUnsubscribeFromResourcesHandler(async (ctx, ct) =>
            {
                if (ctx.Server.SessionId == null)
                {
                    throw new McpException("Cannot remove subscription for server with null SessionId");
                }
                if (ctx.Params?.Uri is { } uri)
                {
                    subscriptions[ctx.Server.SessionId].TryRemove(uri, out _);
                }

                return new EmptyResult();
            })
            .WithCompleteHandler(async (ctx, ct) =>
            {
                // Basic completion support - returns empty array for conformance
                // Real implementations would provide contextual suggestions
                return new CompleteResult
                {
                    Completion = new Completion
                    {
                        Values = [],
                        HasMore = false,
                        Total = 0
                    }
                };
            })
            .WithSetLoggingLevelHandler(async (ctx, ct) =>
            {
                if (ctx.Params?.Level is null)
                {
                    throw new McpProtocolException("Missing required argument 'level'", McpErrorCode.InvalidParams);
                }

                // The SDK updates the LoggingLevel field of the McpServer
                // Send a log notification to confirm the level was set
                await ctx.Server.SendNotificationAsync("notifications/message", new LoggingMessageNotificationParams
                {
                    Level = LoggingLevel.Info,
                    Logger = "conformance-test-server",
                    Data = JsonElement.Parse($"\"Log level set to: {ctx.Params.Level}\""),
                }, cancellationToken: ct);

                return new EmptyResult();
            });

        var app = builder.Build();

        app.MapMcp();

        // Return plain string to avoid JSON serialization issues when reflection is disabled
        app.MapGet("/health", () => "Healthy");

        await app.RunAsync(cancellationToken);
    }

    public static async Task Main(string[] args)
    {
        await MainAsync(args);
    }
}
