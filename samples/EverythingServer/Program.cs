using ModelContextProtocol;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol.Types;
using EverythingServer.Tools;
using EverythingServer;
using ModelContextProtocol.Server;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

HashSet<string> subscriptions = [];

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithListPromptsHandler((ctx, ct) =>
    {
        return Task.FromResult(new ListPromptsResult
        {
            Prompts =
            [
                new Prompt { Name= "simple_prompt", Description = "A prompt without arguments" },
                new Prompt { Name= "complex_prompt", Description = "A prompt with arguments", Arguments = [
                    new PromptArgument { Name = "temperature", Description = "Temperature setting", Required = true },
                    new PromptArgument { Name = "style", Description = "Output style", Required = false }
                ]
                }
            ]
        });
    })
    .WithGetPromptHandler((args, ct) =>
    {
        List<PromptMessage> messages = args.Params?.Name switch
        {
            "simple_prompt" => [new PromptMessage { Role = Role.User, Content = new Content { Type = "text", Text = "This is a simple prompt without arguments" } }],
            "complex_prompt" => [
                new PromptMessage { Role = Role.User, Content = new Content { Type = "text", Text = $"This is a complex prompt with arguments: temperature={args.Params?.Arguments?["temperature"]}, style={(args.Params?.Arguments?.ContainsKey("style") == true ? args.Params?.Arguments?["style"] : "")}" } },
                new PromptMessage { Role = Role.Assistant, Content = new Content { Type = "text", Text = "I understand. You've provided a complex prompt with temperature and style arguments. How would you like me to proceed?" } },
                new PromptMessage { Role = Role.User, Content = new Content { Type = "image", Data = TinyImageTool.MCP_TINY_IMAGE.Split(",").Last(), MimeType = "image/png" } }
                ]
            ,
            _ => throw new NotSupportedException($"Unknown prompt name: {args.Params?.Name}")
        };

        return Task.FromResult(new GetPromptResult
        {
            Messages = messages
        });
    })
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
    ;

builder.Services.AddHostedService(sp =>
{
    var server = sp.GetRequiredService<IMcpServer>();
    return new SubscriptionMessageSender(server, subscriptions);
});

await builder.Build().RunAsync();
