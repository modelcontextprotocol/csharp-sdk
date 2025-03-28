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
    /// <summary>Key used temporarily for flowing request context into an AIFunction.</summary>
    /// <remarks>This will be replaced with use of AIFunctionArguments.Context.</remarks>
    internal const string RequestContextKey = "__temporary_RequestContext";

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

        // TODO: Once this repo consumes a new build of Microsoft.Extensions.AI containing
        // https://github.com/dotnet/extensions/pull/6158,
        // https://github.com/dotnet/extensions/pull/6162, and
        // https://github.com/dotnet/extensions/pull/6175, switch over to using the real
        // AIFunctionFactory, delete the TemporaryXx types, and fix-up the mechanism by
        // which the arguments are passed.

        options = DeriveOptions(method, options);

        return Create(
            TemporaryAIFunctionFactory.Create(method, target, CreateAIFunctionFactoryOptions(method, options)),
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
            TemporaryAIFunctionFactory.Create(method, targetType, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    private static TemporaryAIFunctionFactoryOptions CreateAIFunctionFactoryOptions(
        MethodInfo method, McpServerToolCreateOptions? options) =>
        new()
        {
            Name = options?.Name ?? method.GetCustomAttribute<McpServerToolAttribute>()?.Name,
            Description = options?.Description,
            MarshalResult = static (result, _, cancellationToken) => Task.FromResult(result),
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

                // We assume that if the services used to create the tool support a particular type,
                // so too do the services associated with the server. This is the same basic assumption
                // made in ASP.NET.
                if (options?.Services is { } services &&
                    services.GetService<IServiceProviderIsService>() is { } ispis &&
                    ispis.IsService(pi.ParameterType))
                {
                    return new()
                    {
                        ExcludeFromSchema = true,
                        BindParameter = (pi, args) =>
                            GetRequestContext(args)?.Server?.Services?.GetService(pi.ParameterType) ??
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
                            (GetRequestContext(args)?.Server?.Services as IKeyedServiceProvider)?.GetKeyedService(pi.ParameterType, keyedAttr.Key) ??
                            (pi.HasDefaultValue ? null :
                             throw new ArgumentException("No service of the requested type was found.")),
                    };
                }

                return default;

                static RequestContext<CallToolRequestParams>? GetRequestContext(IReadOnlyDictionary<string, object?> args)
                {
                    if (args.TryGetValue(RequestContextKey, out var orc) &&
                        orc is RequestContext<CallToolRequestParams> requestContext)
                    {
                        return requestContext;
                    }

                    return null;
                }
            },
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
    public override async Task<CallToolResponse> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        // TODO: Once we shift to the real AIFunctionFactory, the request should be passed via AIFunctionArguments.Context.
        Dictionary<string, object?> arguments = request.Params?.Arguments is IDictionary<string, object?> existingArgs ?
            new(existingArgs) :
            [];
        arguments[RequestContextKey] = request;

        object? result;
        try
        {
            result = await AIFunction.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new CallToolResponse()
            {
                IsError = true,
                Content = [new() { Text = e.Message, Type = "text" }],
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

            // TODO https://github.com/modelcontextprotocol/csharp-sdk/issues/69:
            // Add specialization for annotations.
            _ => new()
            {
                Content = [new()
                {
                    Text = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object))),
                    Type = "text"
                }]
            },
        };
    }

}