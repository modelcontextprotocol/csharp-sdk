using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerPrompt"/> that's implemented via an <see cref="AIFunction"/>.</summary>
internal sealed class AIFunctionMcpServerPrompt : McpServerPrompt
{
    /// <summary>Key used temporarily for flowing request context into an AIFunction.</summary>
    /// <remarks>This will be replaced with use of AIFunctionArguments.Context.</remarks>
    internal const string RequestContextKey = "__temporary_RequestContext";

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
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerPrompt Create(
        MethodInfo method,
        object? target,
        McpServerPromptCreateOptions? options)
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
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerPrompt Create(
        MethodInfo method,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type targetType,
        McpServerPromptCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method, options);

        return Create(
            TemporaryAIFunctionFactory.Create(method, targetType, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    private static TemporaryAIFunctionFactoryOptions CreateAIFunctionFactoryOptions(
        MethodInfo method, McpServerPromptCreateOptions? options) =>
        new()
        {
            Name = options?.Name ?? method.GetCustomAttribute<McpServerPromptAttribute>()?.Name,
            Description = options?.Description,
            MarshalResult = static (result, _, cancellationToken) => Task.FromResult(result),
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

                // We assume that if the services used to create the prompt support a particular type,
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

                static RequestContext<GetPromptRequestParams>? GetRequestContext(IReadOnlyDictionary<string, object?> args)
                {
                    if (args.TryGetValue(RequestContextKey, out var orc) &&
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
        if (function.JsonSchema.TryGetProperty("properties", out JsonElement properties))
        {
            foreach (var param in properties.EnumerateObject())
            {
                args.Add(new()
                {
                    Name = param.Name,
                    Description = param.Value.TryGetProperty("description", out JsonElement description) ? description.GetString() : null,
                    Required = param.Value.TryGetProperty("required", out JsonElement required) && required.GetBoolean(),
                });
            }
        }

        Prompt prompt = new()
        {
            Name = options?.Name ?? function.Name,
            Description = options?.Description ?? function.Description,
            Arguments = args,
        };

        return new AIFunctionMcpServerPrompt(function, prompt);
    }

    private static McpServerPromptCreateOptions? DeriveOptions(MethodInfo method, McpServerPromptCreateOptions? options)
    {
        McpServerPromptCreateOptions newOptions = options?.Clone() ?? new();

        if (method.GetCustomAttribute<McpServerPromptAttribute>() is { } attr)
        {
            newOptions.Name ??= attr.Name;
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
    public override string ToString() => AIFunction.ToString();

    /// <inheritdoc />
    public override Prompt ProtocolPrompt { get; }

    /// <inheritdoc />
    public override async Task<GetPromptResult> GetAsync(
        RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        // TODO: Once we shift to the real AIFunctionFactory, the request should be passed via AIFunctionArguments.Context.
        Dictionary<string, object?> arguments = request.Params?.Arguments is IDictionary<string, object?> existingArgs ?
            new(existingArgs) :
            [];
        arguments[RequestContextKey] = request;

        object? result = await AIFunction.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            GetPromptResult getPromptResult => getPromptResult,

            string text => new()
            {
                Description = ProtocolPrompt.Description,
                Messages = [new() { Role = Role.User, Content = new() { Text = text, Type = "text" } }],
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

            null => throw new InvalidOperationException($"Null result returned from prompt function."),

            _ => throw new InvalidOperationException($"Unknown result type '{result.GetType()}' returned from prompt function."),
        };
    }
}