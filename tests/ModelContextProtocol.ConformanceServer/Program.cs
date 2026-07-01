using ConformanceServer.Prompts;
using ConformanceServer.Resources;
using ConformanceServer.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Diagnostics;
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

        // Configure the default, stateful MCP server (served at "/").
        ConfigureConformanceMcpServer(builder.Services, subscriptions, stateless: false);

        var app = builder.Build();

        // Also expose a stateless MCP server at "/stateless" so a single conformance server can
        // serve both the legacy stateful lifecycle (at "/") and the SEP-2575 stateless lifecycle
        // (at "/stateless", which the 2026-07-28 "caching" (SEP-2549) and MRTR (SEP-2322)
        // scenarios require) from one Kestrel port.
        HandleStatelessMcp(app, subscriptions);

        app.MapMcp();

        app.MapGet("/health", () => "Healthy");

        await app.RunAsync(cancellationToken);
    }

    // Registers the conformance MCP server (tools, prompts, resources, filters, and handlers)
    // into the given service collection. Shared by the stateful ("/") and stateless ("/stateless")
    // servers so both expose identical behavior and differ only in their lifecycle.
    private static void ConfigureConformanceMcpServer(
        IServiceCollection services,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> subscriptions,
        bool stateless)
    {
        services.AddDistributedMemoryCache();
        services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = stateless)
            .WithDistributedCacheEventStreamStore()
            .WithTools<ConformanceTools>()
            .WithTools<IncompleteResultTools>()
            .WithTools([ConformanceTools.CreateJsonSchema202012Tool()])
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);

                // For the test_reconnection tool, enable polling mode after the tool runs.
                // This stores the result and closes the SSE stream, so the client
                // must reconnect via GET with Last-Event-ID to retrieve the result.
                if (request.Params.Name == "test_reconnection")
                {
                    await request.EnablePollingAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
                }

                return result;
            })
            // SEP-2549: advertise TTL/cacheScope caching hints on cacheable results. The
            // conformance server's tools, prompts, resources, and resource templates are the
            // same for every caller, so they are cacheable with a "public" scope.
            .AddListToolsFilter(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);
                result.TimeToLive = TimeSpan.FromMinutes(5);
                result.CacheScope = CacheScope.Public;
                return result;
            })
            .AddListPromptsFilter(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);
                result.TimeToLive = TimeSpan.FromMinutes(5);
                result.CacheScope = CacheScope.Public;
                return result;
            })
            .AddListResourcesFilter(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);
                result.TimeToLive = TimeSpan.FromMinutes(5);
                result.CacheScope = CacheScope.Public;
                return result;
            })
            .AddListResourceTemplatesFilter(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);
                result.TimeToLive = TimeSpan.FromMinutes(5);
                result.CacheScope = CacheScope.Public;
                return result;
            })
            .AddReadResourceFilter(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);
                result.TimeToLive = TimeSpan.FromMinutes(1);
                result.CacheScope = CacheScope.Public;
                return result;
            }))
            .WithPrompts<ConformancePrompts>()
            .WithPrompts<IncompleteResultPrompts>()
            .WithResources<ConformanceResources>()
            .WithSubscribeToResourcesHandler(async (ctx, ct) =>
            {
                if (ctx.Server.SessionId == null)
                {
                    throw new McpException("Cannot add subscription for server with null SessionId");
                }
                if (ctx.Params.Uri is { } uri)
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
                if (ctx.Params.Uri is { } uri)
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
    }

    // Maps a second MCP server, configured for the stateless lifecycle, at "/stateless". It is
    // built in its own ServiceCollection so its DI (and HttpServerTransportOptions) stays isolated
    // from the stateful server registered on the main host. Adapted from
    // ModelContextProtocol.TestSseServer.Program.HandleStatelessMcp.
    private static void HandleStatelessMcp(
        WebApplication app,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> subscriptions)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(app.Services.GetRequiredService<ILoggerFactory>());
        services.AddSingleton(app.Services.GetRequiredService<IHostApplicationLifetime>());
        services.AddSingleton(app.Services.GetRequiredService<DiagnosticListener>());
        services.AddRoutingCore();

        ConfigureConformanceMcpServer(services, subscriptions, stateless: true);

        var statelessApp = new ApplicationBuilder(services.BuildServiceProvider());
        statelessApp.UseRouting();
        statelessApp.UseEndpoints(endpoints => endpoints.MapMcp("/stateless"));

        // Terminal middleware that serves "/stateless" requests the main host's routing did not
        // match. Registered before app.MapMcp() so the stateful endpoints still win for "/".
        app.Run(statelessApp.Build());
    }

    public static async Task Main(string[] args)
    {
        await MainAsync(args);
    }
}
