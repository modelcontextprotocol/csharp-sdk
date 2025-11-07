using EverythingServer.Core.Prompts;
using EverythingServer.Core.Resources;
using EverythingServer.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;

namespace EverythingServer.Core;

/// <summary>
/// Extension methods for configuring the EverythingServer MCP handlers.
/// </summary>
public static class EverythingServerExtensions
{
    /// <summary>
    /// Adds all the tools, prompts, resources, and handlers for the EverythingServer.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <param name="subscriptions">The subscriptions dictionary to use for managing resource subscriptions.</param>
    /// <returns>The MCP server builder for chaining.</returns>
    public static IMcpServerBuilder AddEverythingMcpHandlers(
        this IMcpServerBuilder builder,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> subscriptions)
    {
        return builder
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
                var sessionId = ctx.Server.SessionId ?? "stdio";
                
                if (!subscriptions.TryGetValue(sessionId, out var sessionSubscriptions))
                {
                    sessionSubscriptions = new ConcurrentDictionary<string, byte>();
                    subscriptions[sessionId] = sessionSubscriptions;
                }
                
                if (ctx.Params?.Uri is { } uri)
                {
                    sessionSubscriptions.TryAdd(uri, 0);

                    await ctx.Server.SampleAsync([
                        new ChatMessage(ChatRole.System, "You are a helpful test server"),
                        new ChatMessage(ChatRole.User, $"Resource {uri}, context: A new subscription was started"),
                    ],
                    options: new ChatOptions
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
                var sessionId = ctx.Server.SessionId ?? "stdio";
                
                if (subscriptions.TryGetValue(sessionId, out var sessionSubscriptions))
                {
                    if (ctx.Params?.Uri is { } uri)
                    {
                        sessionSubscriptions.TryRemove(uri, out _);
                    }
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
    }
}
