using ConformanceServer.Prompts;
using ConformanceServer.Resources;
using ConformanceServer.Tools;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
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
            .WithHttpTransport(options =>
            {
                // Enable resumability for SSE polling conformance test
                options.EventStreamStore = new DistributedCacheEventStreamStore(
                    new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

                // Configure allowed origins for DNS rebinding protection.
                // For local servers, allow requests from localhost origins.
                options.AllowedOrigins = GetAllowedOrigins(args);
            })
            .WithTools<ConformanceTools>()
            .WithTools([ConformanceTools.CreateJsonSchema202012Tool()])
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);

                // For the test_reconnection tool, enable polling mode after the tool runs.
                // This stores the result and closes the SSE stream, so the client
                // must reconnect via GET with Last-Event-ID to retrieve the result.
                if (request.Params?.Name == "test_reconnection")
                {
                    await request.EnablePollingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
                }

                return result;
            }))
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

        app.MapGet("/health", () => "Healthy");

        await app.RunAsync(cancellationToken);
    }

    public static async Task Main(string[] args)
    {
        await MainAsync(args);
    }

    /// <summary>
    /// Builds a set of allowed origins for DNS rebinding protection based on the server's configured URLs.
    /// For local servers, allows localhost, 127.0.0.1, and [::1] origins with all configured ports.
    /// </summary>
    private static HashSet<string> GetAllowedOrigins(string[] args)
    {
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Parse --urls from command-line args to determine the server's listening addresses.
        string? urls = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--urls", StringComparison.OrdinalIgnoreCase))
            {
                urls = args[i + 1];
                break;
            }
        }

        urls ??= Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5000";

        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                var port = uri.Port;
                var scheme = uri.Scheme;

                origins.Add($"{scheme}://localhost:{port}");
                origins.Add($"{scheme}://127.0.0.1:{port}");
                origins.Add($"{scheme}://[::1]:{port}");
            }
        }

        return origins;
    }
}
