using EverythingServer;
using EverythingServer.Prompts;
using EverythingServer.Resources;
using EverythingServer.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
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
    .WithTools<AddTool>()
    .WithTools<AnnotatedMessageTool>()
    .WithTools<EchoTool>()
    .WithTools<LongRunningTool>()
    .WithTools<PrintEnvTool>()
    .WithTools<SampleLlmTool>()
    .WithTools<TinyImageTool>()
    .WithPrompts<ComplexPromptType>()
    .WithPrompts<SimplePromptType>()
    .WithResources<SimpleResourceType>()
    .WithSubscribeToResourcesHandler(async (ctx, ct) =>
    {
        if (ctx.Server.SessionId == null)
        {
            throw new McpException("Cannot add subscription for server with null SessionId");
        }
        if (ctx.Params?.Uri is { } uri)
        {
            subscriptions[ctx.Server.SessionId].TryAdd(uri, 0);

            await ctx.Server.SampleAsync([
                new ChatMessage(ChatRole.System, "You are a helpful test server"),
                new ChatMessage(ChatRole.User, $"Resource {uri}, context: A new subscription was started"),
            ],
            chatOptions: new ChatOptions
            {
                MaxOutputTokens = 100,
                Temperature = 0.7f,
            },
            cancellationToken: ct);
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
        var exampleCompletions = new Dictionary<string, IEnumerable<string>>
        {
            { "style", ["casual", "formal", "technical", "friendly"] },
            { "temperature", ["0", "0.5", "0.7", "1.0"] },
            { "resourceId", ["1", "2", "3", "4", "5"] }
        };

        if (ctx.Params is not { } @params)
        {
            throw new NotSupportedException($"Params are required.");
        }

        var @ref = @params.Ref;
        var argument = @params.Argument;

        if (@ref is ResourceTemplateReference rtr)
        {
            var resourceId = rtr.Uri?.Split("/").Last();

            if (resourceId is null)
            {
                return new CompleteResult();
            }

            var values = exampleCompletions["resourceId"].Where(id => id.StartsWith(argument.Value));

            return new CompleteResult
            {
                Completion = new Completion { Values = [.. values], HasMore = false, Total = values.Count() }
            };
        }

        if (@ref is PromptReference pr)
        {
            if (!exampleCompletions.TryGetValue(argument.Name, out IEnumerable<string>? value))
            {
                throw new NotSupportedException($"Unknown argument name: {argument.Name}");
            }

            var values = value.Where(value => value.StartsWith(argument.Value));
            return new CompleteResult
            {
                Completion = new Completion { Values = [.. values], HasMore = false, Total = values.Count() }
            };
        }

        throw new NotSupportedException($"Unknown reference type: {@ref.Type}");
    })
    .WithSetLoggingLevelHandler(async (ctx, ct) =>
    {
        if (ctx.Params?.Level is null)
        {
            throw new McpProtocolException("Missing required argument 'level'", McpErrorCode.InvalidParams);
        }

        // The SDK updates the LoggingLevel field of the IMcpServer

        await ctx.Server.SendNotificationAsync("notifications/message", new
        {
            Level = "debug",
            Logger = "test-server",
            Data = $"Logging level set to {ctx.Params.Level}",
        }, cancellationToken: ct);

        return new EmptyResult();
    });

ResourceBuilder resource = ResourceBuilder.CreateDefault().AddService("everything-server");
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("*").AddHttpClientInstrumentation().SetResourceBuilder(resource))
    .WithMetrics(b => b.AddMeter("*").AddHttpClientInstrumentation().SetResourceBuilder(resource))
    .WithLogging(b => b.SetResourceBuilder(resource))
    .UseOtlpExporter();

var app = builder.Build();

app.MapMcp();

app.Run();
