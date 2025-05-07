using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerResource"/> that's implemented via an <see cref="AIFunction"/>.</summary>
internal sealed class AIFunctionMcpServerResource : McpServerResource
{
    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerResource Create(
        Delegate method,
        McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method.Method, options);

        return Create(method.Method, method.Target, options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerResource Create(
        MethodInfo method,
        object? target,
        McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method, options);

        return Create(
            AIFunctionFactory.Create(method, target, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a property, specified via a <see cref="PropertyInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerResource Create(
        PropertyInfo property,
        object? target,
        McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(property);

        options = DeriveOptions(property, options);

        MethodInfo? getMethod = property.GetGetMethod();
        if (getMethod is null)
        {
            throw new InvalidOperationException($"Property '{property.Name}' does not have a get method.");
        }

        return Create(
            AIFunctionFactory.Create(getMethod, target, CreateAIFunctionFactoryOptions(getMethod, options)),
            options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerResource Create(
        MethodInfo method,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type targetType,
        McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method, options);

        return Create(
            AIFunctionFactory.Create(method, targetType, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a method, specified via a <see cref="PropertyInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerResource Create(
        PropertyInfo property,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type targetType,
        McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(property);

        options = DeriveOptions(property, options);

        MethodInfo? getMethod = property.GetGetMethod();
        if (getMethod is null)
        {
            throw new InvalidOperationException($"Property '{property.Name}' does not have a get method.");
        }

        return Create(
            AIFunctionFactory.Create(getMethod, targetType, CreateAIFunctionFactoryOptions(getMethod, options)),
            options);
    }

    private static AIFunctionFactoryOptions CreateAIFunctionFactoryOptions(
        MethodInfo method, McpServerResourceCreateOptions? options) =>
        new()
        {
            Name = options?.Name ?? method.GetCustomAttribute<McpServerResourceAttribute>()?.Name,
            Description = options?.Description,
            MarshalResult = static (result, _, cancellationToken) => new ValueTask<object?>(result),
            SerializerOptions = McpJsonUtilities.DefaultOptions,
            Services = options?.Services,
            ConfigureParameterBinding = pi =>
            {
                if (pi.ParameterType == typeof(RequestContext<ReadResourceRequestParams>))
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

                static RequestContext<ReadResourceRequestParams>? GetRequestContext(AIFunctionArguments args)
                {
                    if (args.Context?.TryGetValue(typeof(RequestContext<ReadResourceRequestParams>), out var orc) is true &&
                        orc is RequestContext<ReadResourceRequestParams> requestContext)
                    {
                        return requestContext;
                    }

                    return null;
                }
            },
        };

    /// <summary>Creates an <see cref="McpServerResource"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    public static new AIFunctionMcpServerResource Create(AIFunction function, McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(function);

        string name = options?.Name ?? function.Name;

        Resource resource = new()
        {
            Uri = options?.Uri ?? DeriveUri(name),
            Name = name,
            Description = options?.Description,
            MimeType = options?.MimeType,
        };

        return new(function, resource);
    }

    private static McpServerResourceCreateOptions DeriveOptions(MemberInfo member, McpServerResourceCreateOptions? options)
    {
        McpServerResourceCreateOptions newOptions = options?.Clone() ?? new();

        if (member.GetCustomAttribute<McpServerResourceAttribute>() is { } resourceAttr)
        {
            newOptions.Uri ??= resourceAttr.Uri;
            newOptions.Name ??= resourceAttr.Name;
            newOptions.MimeType ??= resourceAttr.MimeType;
        }

        if (member.GetCustomAttribute<DescriptionAttribute>() is { } descAttr)
        {
            newOptions.Description ??= descAttr.Description;
        }

        return newOptions;
    }

    /// <summary>Derives a name to be used as a resource name.</summary>
    private static string DeriveUri(string name) => $"resource://{Uri.EscapeDataString(name)}";

    /// <summary>Gets the <see cref="AIFunction"/> wrapped by this resource.</summary>
    internal AIFunction AIFunction { get; }

    /// <summary>Initializes a new instance of the <see cref="McpServerResource"/> class.</summary>
    private AIFunctionMcpServerResource(AIFunction function, Resource resource)
    {
        AIFunction = function;
        ProtocolResource = resource;
    }

    /// <inheritdoc />
    public override string ToString() => AIFunction.ToString();

    /// <inheritdoc />
    public override Resource ProtocolResource { get; }

    /// <inheritdoc />
    public override async ValueTask<ReadResourceResult> ReadAsync(
        RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        AIFunctionArguments arguments = new()
        {
            Services = request.Services,
            Context = new Dictionary<object, object?>() { [typeof(RequestContext<ReadResourceRequestParams>)] = request }
        };

        object? result = await AIFunction.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            ReadResourceResult readResourceResult => readResourceResult,

            ResourceContents content => new()
            {
                Contents = [content],
            },

            TextContent tc => new()
            {
                Contents = [new TextResourceContents() { Uri = ProtocolResource.Uri, MimeType = ProtocolResource.MimeType, Text = tc.Text }],
            },

            DataContent dc => new()
            {
                Contents = [new BlobResourceContents() { Uri = ProtocolResource.Uri, MimeType = dc.MediaType, Blob = dc.GetBase64Data() }],
            },

            string text => new()
            {
                Contents = [new TextResourceContents() { Uri = ProtocolResource.Uri, MimeType = ProtocolResource.MimeType, Text = text }],
            },

            IEnumerable<ResourceContents> contents => new()
            {
                Contents = contents.ToList(),
            },

            IEnumerable<AIContent> aiContents => new()
            {
                Contents = aiContents.Select<AIContent, ResourceContents>(
                    ac => ac switch
                    {
                        TextContent tc => new TextResourceContents()
                        {
                            Uri = ProtocolResource.Uri,
                            MimeType = ProtocolResource.MimeType,
                            Text = tc.Text
                        },

                        DataContent dc => new BlobResourceContents()
                        {
                            Uri = ProtocolResource.Uri,
                            MimeType = dc.MediaType,
                            Blob = dc.GetBase64Data()
                        },

                        _ => throw new InvalidOperationException($"Unsupported AIContent type '{ac.GetType()}' returned from resource function."),
                    }).ToList(),
            },

            IEnumerable<string> strings => new()
            {
                Contents = strings.Select<string, ResourceContents>(text => new TextResourceContents() 
                { 
                    Uri = ProtocolResource.Uri, MimeType = ProtocolResource.MimeType, Text = text 
                }).ToList(),
            },

            null => throw new InvalidOperationException("Null result returned from resource function."),

            _ => throw new InvalidOperationException($"Unsupported result type '{result.GetType()}' returned from resource function."),
        };
    }
}