using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerTool"/> that's implemented via an <see cref="AIFunction"/>.</summary>
internal sealed partial class AIFunctionMcpServerTool : McpServerTool
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerTool Create(
        Delegate method,
        McpServerToolCreateOptions? options)
    {
        Throw.IfNull(method);
        
        options = DeriveOptions(method.Method, options);

        return Create(method.Method, method.Target, options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerTool Create(
        MethodInfo method,
        object? target,
        McpServerToolCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method, options);

        return Create(
            AIFunctionFactory.Create(method, target, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerTool Create(
        MethodInfo method,
        Func<RequestContext<CallToolRequestParams>, object> createTargetFunc,
        McpServerToolCreateOptions? options)
    {
        Throw.IfNull(method);
        Throw.IfNull(createTargetFunc);

        options = DeriveOptions(method, options);

        return Create(
            AIFunctionFactory.Create(method, args =>
            {
                var request = (RequestContext<CallToolRequestParams>)args.Context![typeof(RequestContext<CallToolRequestParams>)]!;
                return createTargetFunc(request);
            }, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    // TODO: Fix the need for this suppression.
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2111:ReflectionToDynamicallyAccessedMembers",
        Justification = "AIFunctionFactory ensures that the Type passed to AIFunctionFactoryOptions.CreateInstance has public constructors preserved")]
    internal static Func<Type, AIFunctionArguments, object> GetCreateInstanceFunc() =>
        static ([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] type, args) => args.Services is { } services ?
            ActivatorUtilities.CreateInstance(services, type) :
            Activator.CreateInstance(type)!;

    private static AIFunctionFactoryOptions CreateAIFunctionFactoryOptions(
        MethodInfo method, McpServerToolCreateOptions? options) =>
        new()
        {
            Name = options?.Name ?? method.GetCustomAttribute<McpServerToolAttribute>()?.Name,
            Description = options?.Description,
            MarshalResult = static (result, _, cancellationToken) => new ValueTask<object?>(result),
            SerializerOptions = options?.SerializerOptions ?? McpJsonUtilities.DefaultOptions,
            ConfigureParameterBinding = pi =>
            {
                if (pi.ParameterType == typeof(RequestContext<CallToolRequestParams>))
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
                                requestContent?.Params?.Meta?.ProgressToken is { } progressToken)
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

                static RequestContext<CallToolRequestParams>? GetRequestContext(AIFunctionArguments args)
                {
                    if (args.Context?.TryGetValue(typeof(RequestContext<CallToolRequestParams>), out var orc) is true &&
                        orc is RequestContext<CallToolRequestParams> requestContext)
                    {
                        return requestContext;
                    }

                    return null;
                }
            },
            JsonSchemaCreateOptions = options?.SchemaCreateOptions,
        };

    /// <summary>Creates an <see cref="McpServerTool"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    public static new AIFunctionMcpServerTool Create(AIFunction function, McpServerToolCreateOptions? options)
    {
        Throw.IfNull(function);

        Tool tool = new()
        {
            Name = options?.Name ?? function.Name,
            Description = options?.Description ?? function.Description,
            InputSchema = function.JsonSchema,     
        };

        if (options is not null)
        {
            if (options.Title is not null ||
                options.Idempotent is not null ||
                options.Destructive is not null ||
                options.OpenWorld is not null ||
                options.ReadOnly is not null)
            {
                tool.Annotations = new()
                {
                    Title = options?.Title,
                    IdempotentHint = options?.Idempotent,
                    DestructiveHint = options?.Destructive,
                    OpenWorldHint = options?.OpenWorld,
                    ReadOnlyHint = options?.ReadOnly,
                };
            }
        }

        return new AIFunctionMcpServerTool(function, tool, options?.Services);
    }

    private static McpServerToolCreateOptions DeriveOptions(MethodInfo method, McpServerToolCreateOptions? options)
    {
        McpServerToolCreateOptions newOptions = options?.Clone() ?? new();

        if (method.GetCustomAttribute<McpServerToolAttribute>() is { } toolAttr)
        {
            newOptions.Name ??= toolAttr.Name;
            newOptions.Title ??= toolAttr.Title;

            if (toolAttr._destructive is bool destructive)
            {
                newOptions.Destructive ??= destructive;
            }

            if (toolAttr._idempotent is bool idempotent)
            {
                newOptions.Idempotent ??= idempotent;
            }

            if (toolAttr._openWorld is bool openWorld)
            {
                newOptions.OpenWorld ??= openWorld;
            }

            if (toolAttr._readOnly is bool readOnly)
            {
                newOptions.ReadOnly ??= readOnly;
            }
        }

        if (method.GetCustomAttribute<DescriptionAttribute>() is { } descAttr)
        {
            newOptions.Description ??= descAttr.Description;
        }

        return newOptions;
    }

    /// <summary>Gets the <see cref="AIFunction"/> wrapped by this tool.</summary>
    internal AIFunction AIFunction { get; }

    /// <summary>Initializes a new instance of the <see cref="McpServerTool"/> class.</summary>
    private AIFunctionMcpServerTool(AIFunction function, Tool tool, IServiceProvider? serviceProvider)
    {
        AIFunction = function;
        ProtocolTool = tool;
        _logger = serviceProvider?.GetService<ILoggerFactory>()?.CreateLogger<McpServerTool>() ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public override Tool ProtocolTool { get; }

    /// <inheritdoc />
    public override async ValueTask<CallToolResponse> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        AIFunctionArguments arguments = new()
        {
            Services = request.Services,
            Context = new Dictionary<object, object?>() { [typeof(RequestContext<CallToolRequestParams>)] = request }
        };

        var argDict = request.Params?.Arguments;
        if (argDict is not null)
        {
            foreach (var kvp in argDict)
            {
                arguments[kvp.Key] = kvp.Value;
            }
        }

        object? result;
        try
        {
            result = await AIFunction.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ToolCallError(request.Params?.Name ?? string.Empty, e);

            string errorMessage = e is McpException ?
                $"An error occurred invoking '{request.Params?.Name}': {e.Message}" :
                $"An error occurred invoking '{request.Params?.Name}'.";

            return new()
            {
                IsError = true,
                Content = [new() { Text = errorMessage, Type = "text" }],
            };
        }

        return result switch
        {
            AIContent aiContent => new()
            {
                Content = [aiContent.ToContent()],
                IsError = aiContent is ErrorContent
            },

            null => new()
            {
                Content = []
            },
            
            string text => new()
            {
                Content = [new() { Text = text, Type = "text" }]
            },
            
            Content content => new()
            {
                Content = [content]
            },
            
            IEnumerable<string> texts => new()
            {
                Content = [.. texts.Select(x => new Content() { Type = "text", Text = x ?? string.Empty })]
            },
            
            IEnumerable<AIContent> contentItems => ConvertAIContentEnumerableToCallToolResponse(contentItems),
            
            IEnumerable<Content> contents => new()
            {
                Content = [.. contents]
            },
            
            CallToolResponse callToolResponse => callToolResponse,

            _ => new()
            {
                Content = [new()
                {
                    Text = JsonSerializer.Serialize(result, AIFunction.JsonSerializerOptions.GetTypeInfo(typeof(object))),
                    Type = "text"
                }]
            },
        };
    }

    private static CallToolResponse ConvertAIContentEnumerableToCallToolResponse(IEnumerable<AIContent> contentItems)
    {
        List<Content> contentList = [];
        bool allErrorContent = true;
        bool hasAny = false;

        foreach (var item in contentItems)
        {
            contentList.Add(item.ToContent());
            hasAny = true;

            if (allErrorContent && item is not ErrorContent)
            {
                allErrorContent = false;
            }
        }

        return new()
        {
            Content = contentList,
            IsError = allErrorContent && hasAny
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "\"{ToolName}\" threw an unhandled exception.")]
    private partial void ToolCallError(string toolName, Exception exception);
}