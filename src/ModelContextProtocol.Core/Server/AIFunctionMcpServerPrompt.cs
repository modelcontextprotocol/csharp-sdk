using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerPrompt"/> that's implemented via an <see cref="AIFunction"/>.</summary>
internal sealed class AIFunctionMcpServerPrompt : McpServerPrompt
{
    /// <summary>
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerPrompt Create(
        Delegate method,
        McpServerPromptCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method.Method, options);

        return Create(method.Method, method.Target, options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerPrompt Create(
        MethodInfo method,
        object? target,
        McpServerPromptCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method, options);

        return Create(
            AIFunctionFactory.Create(method, target, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerPrompt Create(
        MethodInfo method,
        Func<RequestContext<GetPromptRequestParams>, object> createTargetFunc,
        McpServerPromptCreateOptions? options)
    {
        Throw.IfNull(method);
        Throw.IfNull(createTargetFunc);

        options = DeriveOptions(method, options);

        return Create(
            AIFunctionFactory.Create(method, args =>
            {
                var request = (RequestContext<GetPromptRequestParams>)args.Context![typeof(RequestContext<GetPromptRequestParams>)]!;
                return createTargetFunc(request);
            }, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    private static AIFunctionFactoryOptions CreateAIFunctionFactoryOptions(
        MethodInfo method, McpServerPromptCreateOptions? options) =>
        new()
        {
            Name = options?.Name ?? method.GetCustomAttribute<McpServerPromptAttribute>()?.Name,
            Description = options?.Description,
            MarshalResult = static (result, _, cancellationToken) => new ValueTask<object?>(result),
            SerializerOptions = options?.SerializerOptions ?? McpJsonUtilities.DefaultOptions,
            JsonSchemaCreateOptions = options?.SchemaCreateOptions,
            ConfigureParameterBinding = pi =>
            {
                if (pi.ParameterType == typeof(RequestContext<GetPromptRequestParams>))
                {
                    return new()
                    {
                        ExcludeFromSchema = true,
                        BindParameter = (pi, args) => GetRequestContext(args),
                    };
                }

                if (pi.ParameterType == typeof(IMcpServer))
                {
                    return new()
                    {
                        ExcludeFromSchema = true,
                        BindParameter = (pi, args) => GetRequestContext(args)?.Server,
                    };
                }

                if (pi.ParameterType == typeof(IProgress<ProgressNotificationValue>))
                {
                    // Bind IProgress<ProgressNotificationValue> to the progress token in the request,
                    // if there is one. If we can't get one, return a nop progress.
                    return new()
                    {
                        ExcludeFromSchema = true,
                        BindParameter = (pi, args) =>
                        {
                            var requestContent = GetRequestContext(args);
                            if (requestContent?.Server is { } server &&
                                requestContent?.Params?.ProgressToken is { } progressToken)
                            {
                                return new TokenProgress(server, progressToken);
                            }

                            return NullProgress.Instance;
                        },
                    };
                }

                if (options?.Services is { } services &&
                    services.GetService<IServiceProviderIsService>() is { } ispis &&
                    ispis.IsService(pi.ParameterType))
                {
                    return new()
                    {
                        ExcludeFromSchema = true,
                        BindParameter = (pi, args) =>
                            GetRequestContext(args)?.Services?.GetService(pi.ParameterType) ??
                            (pi.HasDefaultValue ? null :
                             throw new ArgumentException("No service of the requested type was found.")),
                    };
                }

                if (pi.GetCustomAttribute<FromKeyedServicesAttribute>() is { } keyedAttr)
                {
                    return new()
                    {
                        ExcludeFromSchema = true,
                        BindParameter = (pi, args) =>
                            (GetRequestContext(args)?.Services as IKeyedServiceProvider)?.GetKeyedService(pi.ParameterType, keyedAttr.Key) ??
                            (pi.HasDefaultValue ? null :
                             throw new ArgumentException("No service of the requested type was found.")),
                    };
                }

                return default;

                static RequestContext<GetPromptRequestParams>? GetRequestContext(AIFunctionArguments args)
                {
                    if (args.Context?.TryGetValue(typeof(RequestContext<GetPromptRequestParams>), out var orc) is true &&
                        orc is RequestContext<GetPromptRequestParams> requestContext)
                    {
                        return requestContext;
                    }

                    return null;
                }
            },
        };

    /// <summary>Creates an <see cref="McpServerPrompt"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    public static new AIFunctionMcpServerPrompt Create(AIFunction function, McpServerPromptCreateOptions? options)
    {
        Throw.IfNull(function);

        List<PromptArgument> args = [];
        HashSet<string>? requiredProps = function.JsonSchema.TryGetProperty("required", out JsonElement required)
            ? new(required.EnumerateArray().Select(p => p.GetString()!), StringComparer.Ordinal)
            : null;

        if (function.JsonSchema.TryGetProperty("properties", out JsonElement properties))
        {
            foreach (var param in properties.EnumerateObject())
            {
                args.Add(new()
                {
                    Name = param.Name,
                    Description = param.Value.TryGetProperty("description", out JsonElement description) ? description.GetString() : null,
                    Required = requiredProps?.Contains(param.Name) ?? false,
                });
            }
        }

        Prompt prompt = new()
        {
            Name = options?.Name ?? function.Name,
            Title = options?.Title,
            Description = options?.Description ?? function.Description,
            Arguments = args,
        };

        return new AIFunctionMcpServerPrompt(function, prompt);
    }

    private static McpServerPromptCreateOptions DeriveOptions(MethodInfo method, McpServerPromptCreateOptions? options)
    {
        McpServerPromptCreateOptions newOptions = options?.Clone() ?? new();

        if (method.GetCustomAttribute<McpServerPromptAttribute>() is { } promptAttr)
        {
            newOptions.Name ??= promptAttr.Name;
            newOptions.Title ??= promptAttr.Title;
        }

        if (method.GetCustomAttribute<DescriptionAttribute>() is { } descAttr)
        {
            newOptions.Description ??= descAttr.Description;
        }

        return newOptions;
    }

    /// <summary>Gets the <see cref="AIFunction"/> wrapped by this prompt.</summary>
    internal AIFunction AIFunction { get; }

    /// <summary>Initializes a new instance of the <see cref="McpServerPrompt"/> class.</summary>
    private AIFunctionMcpServerPrompt(AIFunction function, Prompt prompt)
    {
        AIFunction = function;
        ProtocolPrompt = prompt;
    }

    /// <inheritdoc />
    public override Prompt ProtocolPrompt { get; }

    /// <inheritdoc />
    public override async ValueTask<GetPromptResult> GetAsync(
        RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        AIFunctionArguments arguments = new()
        {
            Services = request.Services,
            Context = new Dictionary<object, object?>() { [typeof(RequestContext<GetPromptRequestParams>)] = request }
        };

        var argDict = request.Params?.Arguments;
        if (argDict is not null)
        {
            foreach (var kvp in argDict)
            {
                arguments[kvp.Key] = kvp.Value;
            }
        }

        object? result = await AIFunction.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            GetPromptResult getPromptResult => getPromptResult,

            string text => new()
            {
                Description = ProtocolPrompt.Description,
                Messages = [new() { Role = Role.User, Content = new TextContentBlock() { Text = text } }],
            },

            PromptMessage promptMessage => new()
            {
                Description = ProtocolPrompt.Description,
                Messages = [promptMessage],
            },

            IEnumerable<PromptMessage> promptMessages => new()
            {
                Description = ProtocolPrompt.Description,
                Messages = [.. promptMessages],
            },

            ChatMessage chatMessage => new()
            {
                Description = ProtocolPrompt.Description,
                Messages = [.. chatMessage.ToPromptMessages()],
            },

            IEnumerable<ChatMessage> chatMessages => new()
            {
                Description = ProtocolPrompt.Description,
                Messages = [.. chatMessages.SelectMany(chatMessage => chatMessage.ToPromptMessages())],
            },

            null => throw new InvalidOperationException("Null result returned from prompt function."),

            _ => throw new InvalidOperationException($"Unknown result type '{result.GetType()}' returned from prompt function."),
        };
    }
}