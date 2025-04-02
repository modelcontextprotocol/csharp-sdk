using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol.Types;
using EverythingServer;
using ModelContextProtocol.Server;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using EverythingServer.Prompts;
using EverythingServer.Tools;
using ModelContextProtocol.Protocol.Messages;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    // Configure all logs to go to stderr
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

HashSet<string> subscriptions = [];
var _minimumLoggingLevel = LoggingLevel.Debug;

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<AddTool>()
    .WithTools<AnnotatedMessageTool>()
    .WithTools<EchoTool>()
    .WithTools<LongRunningTool>()
    .WithTools<PrintEnvTool>()
    .WithTools<SampleLlmTool>()
    .WithTools<TinyImageTool>()
    .WithPrompts<ComplexPromptType>()
    .WithPrompts<SimplePromptType>()
    .WithListResourceTemplatesHandler((ctx, ct) =>
    {
        return Task.FromResult(new ListResourceTemplatesResult
        {
            ResourceTemplates =
            [
                new ResourceTemplate { Name = "Static Resource", Description = "A static resource with a numeric ID", UriTemplate = "test://static/resource/{id}" }
            ]
        });
    })
    .WithReadResourceHandler((ctx, ct) =>
    {
        var uri = ctx.Params?.Uri;

        if (uri is null || !uri.StartsWith("test://static/resource/"))
        {
            throw new NotSupportedException($"Unknown resource: {uri}");
        }

        int index = int.Parse(uri["test://static/resource/".Length..]) - 1;

        if (index < 0 || index >= ResourceGenerator.Resources.Count)
        {
            throw new NotSupportedException($"Unknown resource: {uri}");
        }

        var resource = ResourceGenerator.Resources[index];

        if (resource.MimeType == "text/plain")
        {
            return Task.FromResult(new ReadResourceResult
            {
                Contents = [new TextResourceContents
                {
                    Text = resource.Description!,
                    MimeType = resource.MimeType,
                    Uri = resource.Uri,
                }]
            });
        }
        else
        {
            return Task.FromResult(new ReadResourceResult
            {
                Contents = [new BlobResourceContents
                {
                    Blob = resource.Description!,
                    MimeType = resource.MimeType,
                    Uri = resource.Uri,
                }]
            });
        }
    })
    .WithSubscribeToResourcesHandler(async (ctx, ct) =>
    {
        var uri = ctx.Params?.Uri;

        if (uri is not null)
        {
            subscriptions.Add(uri);

            await ctx.Server.RequestSamplingAsync([
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
    .WithUnsubscribeFromResourcesHandler((ctx, ct) =>
    {
        var uri = ctx.Params?.Uri;
        if (uri is not null)
        {
            subscriptions.Remove(uri);
        }
        return Task.FromResult(new EmptyResult());
    })
    .WithGetCompletionHandler((ctx, ct) =>
    {
        var exampleCompletions = new Dictionary<string, IEnumerable<string>>
        {
            { "style", ["casual", "formal", "technical", "friendly"] },
            { "temperature", ["0", "0.5", "0.7", "1.0"] },
            { "resourceId", ["1", "2", "3", "4", "5"] }
        };

        var @ref = ctx.Params?.Ref;

        if (@ref is null)
        {
            throw new NotSupportedException($"Reference is required.");
        }

        var argument = ctx.Params!.Argument;

        if (@ref.Type == "ref/resource")
        {
            var resourceId = @ref.Uri?.Split("/").Last();

            if (resourceId is null)
            {
                return Task.FromResult(new CompleteResult());
            }

            var values = exampleCompletions["resourceId"].Where(id => id.StartsWith(argument.Value));

            return Task.FromResult(new CompleteResult
            {
                Completion = new Completion { Values = [..values], HasMore = false, Total = values.Count() }
            });
        }

        if (@ref.Type == "ref/prompt")
        {
            if (!exampleCompletions.TryGetValue(argument.Name, out IEnumerable<string>? value))
            {
                throw new NotSupportedException($"Unknown argument name: {argument.Name}");
            }

            var values = value.Where(value => value.StartsWith(argument.Value));
            return Task.FromResult(new CompleteResult
            {
                Completion = new Completion { Values = [..values], HasMore = false, Total = values.Count() }
            });
        }

        throw new NotSupportedException($"Unknown reference type: {@ref.Type}");
    })
    .WithSetLoggingLevelHandler(async (ctx, ct) =>
    {
        if (ctx.Params?.Level is null)
        {
            throw new McpServerException("Missing required argument 'level'");
        }

        _minimumLoggingLevel = ctx.Params.Level;

        await ctx.Server.SendMessageAsync(new JsonRpcNotification
        {
            Method = "notifications/message",
            Params = new
            {
                Level = "debug",
                Logger = "test-server",
                Data = $"Logging level set to {_minimumLoggingLevel}",
            }
        }, ct);

        return new EmptyResult();
    })
    ;

builder.Services.AddSingleton(subscriptions);
builder.Services.AddHostedService<SubscriptionMessageSender>();
builder.Services.AddHostedService<LoggingUpdateMessageSender>();

builder.Services.AddSingleton<Func<LoggingLevel>>(_ => () => _minimumLoggingLevel);

await builder.Build().RunAsync();
