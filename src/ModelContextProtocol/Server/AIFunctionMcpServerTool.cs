using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerTool"/> that's implemented via an <see cref="AIFunction"/>.</summary>
internal sealed class AIFunctionMcpServerTool : McpServerTool
{
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
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="Delegate"/> instance.
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
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerTool Create(
        MethodInfo method,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type targetType,
        McpServerToolCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method, options);

        return Create(
            AIFunctionFactory.Create(method, targetType, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    private static AIFunctionFactoryOptions CreateAIFunctionFactoryOptions(
        MethodInfo method, McpServerToolCreateOptions? options) =>
        new()
        {
            Name = options?.Name ?? method.GetCustomAttribute<McpServerToolAttribute>()?.Name,
            Description = options?.Description,
            MarshalResult = static (result, _, cancellationToken) => new ValueTask<object?>(result),
            SerializerOptions = options?.SerializerOptions ?? McpJsonUtilities.DefaultOptions,
            Services = options?.Services,
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

        return new AIFunctionMcpServerTool(function, tool);
    }

    private static McpServerToolCreateOptions? DeriveOptions(MethodInfo method, McpServerToolCreateOptions? options)
    {
        McpServerToolCreateOptions newOptions = options?.Clone() ?? new();

        if (method.GetCustomAttribute<McpServerToolAttribute>() is { } attr)
        {
            newOptions.Name ??= attr.Name;
            newOptions.Title ??= attr.Title;

            if (attr._destructive is bool destructive)
            {
                newOptions.Destructive ??= destructive;
            }

            if (attr._idempotent is bool idempotent)
            {
                newOptions.Idempotent ??= idempotent;
            }

            if (attr._openWorld is bool openWorld)
            {
                newOptions.OpenWorld ??= openWorld;
            }

            if (attr._readOnly is bool readOnly)
            {
                newOptions.ReadOnly ??= readOnly;
            }
        }

        return newOptions;
    }

    /// <summary>Gets the <see cref="AIFunction"/> wrapped by this tool.</summary>
    internal AIFunction AIFunction { get; }

    /// <summary>Initializes a new instance of the <see cref="McpServerTool"/> class.</summary>
    private AIFunctionMcpServerTool(AIFunction function, Tool tool)
    {
        AIFunction = function;
        ProtocolTool = tool;
    }

    /// <inheritdoc />
    public override string ToString() => AIFunction.ToString();

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
                Content = [aiContent.ToContent()]
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
            
            IEnumerable<AIContent> contentItems => new()
            {
                Content = [.. contentItems.Select(static item => item.ToContent())]
            },
            
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

}