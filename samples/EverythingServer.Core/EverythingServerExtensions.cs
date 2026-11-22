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
        // Configure server implementation details with icons and website
        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "Everything Server",
                Version = "1.0.0",
                Title = "MCP Everything Server",
                Description = "A comprehensive MCP server demonstrating tools, prompts, resources, sampling, and all MCP features",
                WebsiteUrl = "https://github.com/modelcontextprotocol/csharp-sdk",
                Icons = [
                    new Icon
                    {
                        Source = "https://raw.githubusercontent.com/microsoft/fluentui-emoji/62ecdc0d7ca5c6df32148c169556bc8d3782fca4/assets/Gear/Flat/gear_flat.svg",
                        MimeType = "image/svg+xml",
                        Sizes = ["any"],
                        Theme = "light"
                    },
                    new Icon
                    {
                        Source = "https://raw.githubusercontent.com/microsoft/fluentui-emoji/62ecdc0d7ca5c6df32148c169556bc8d3782fca4/assets/Gear/3D/gear_3d.png",
                        MimeType = "image/png",
                        Sizes = ["256x256"]
                    }
                ]
            };
        });

        return builder
            .WithTools<AddTool>()
            .WithTools<AnnotatedMessageTool>()
            .WithTools([
                // EchoTool with complex icon configuration demonstrating multiple icons,
                // MIME types, size specifications, and theme preferences
                McpServerTool.Create(
                    typeof(EchoTool).GetMethod(nameof(EchoTool.Echo))!,
                    options: new McpServerToolCreateOptions
                    {
                        Icons = [
                            // High-resolution PNG icon for light theme
                            new Icon
                            {
                                Source = "https://raw.githubusercontent.com/microsoft/fluentui-emoji/62ecdc0d7ca5c6df32148c169556bc8d3782fca4/assets/Loudspeaker/Flat/loudspeaker_flat.svg",
                                MimeType = "image/svg+xml",
                                Sizes = ["any"],
                                Theme = "light"
                            },
                            // 3D icon for dark theme
                            new Icon
                            {
                                Source = "https://raw.githubusercontent.com/microsoft/fluentui-emoji/62ecdc0d7ca5c6df32148c169556bc8d3782fca4/assets/Loudspeaker/3D/loudspeaker_3d.png",
                                MimeType = "image/png",
                                Sizes = ["256x256"],
                                Theme = "dark"
                            },
                            // WebP format for modern browsers
                            // Demonstrates Data URI representation with the smallest possible valid WebP image (1x1 pixel).
                            // This will appear as a white box when rendered by a browser at 32x32
                            new Icon
                            {
                                Source = "data:image/webp;base64,UklGRiQAAABXRUJQVlA4IBgAAAAwAQCdASoBAAEAAwA0JaQAA3AA/vuUAAA=",
                                MimeType = "image/webp",
                                Sizes = ["32x32"]
                            }
                        ]
                    })
            ])
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
