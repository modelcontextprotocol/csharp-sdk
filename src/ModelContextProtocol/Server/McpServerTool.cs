using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Reflection;
using System.Text.Json;

namespace ModelContextProtocol;

/// <summary>Represents an invocable tool used by Model Context Protocol clients and servers.</summary>
public sealed class McpServerTool
{
    /// <summary>Key used temporarily for flowing request context into an AIFunction.</summary>
    /// <remarks>This will be replaced with use of AIFunctionArguments.Context.</remarks>
    private const string RequestContextKey = "__temporary_RequestContext";

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerTool"/>.</param>
    /// <param name="services">
    /// Optional services used in the construction of the <see cref="McpServerTool"/>. These services will be
    /// used to determine which parameters should be satisifed from dependency injection, and so what services
    /// are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </param>
    /// <returns>The created <see cref="McpServerTool"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(Delegate method, IServiceProvider? services = null)
    {
        Throw.IfNull(method);

        return Create(method.Method, method.Target, services);
    }

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerTool"/>.</param>
    /// <param name="target">The instance if <paramref name="method"/> is an instance method; otherwise, <see langword="null"/>.</param>
    /// <param name="services">
    /// Optional services used in the construction of the <see cref="McpServerTool"/>. These services will be
    /// used to determine which parameters should be satisifed from dependency injection, and so what services
    /// are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </param>
    /// <returns>The created <see cref="McpServerTool"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> is an instance method but <paramref name="target"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(MethodInfo method, object? target = null, IServiceProvider? services = null)
    {
        Throw.IfNull(method);

        // TODO: Once this repo consumes a new build of Microsoft.Extensions.AI containing
        // https://github.com/dotnet/extensions/pull/6158,
        // https://github.com/dotnet/extensions/pull/6162, and
        // https://github.com/dotnet/extensions/pull/6175, switch over to using the real
        // AIFunctionFactory, delete the TemporaryXx types, and fix-up the mechanism by
        // which the arguments are passed.

        return Create(TemporaryAIFunctionFactory.Create(method, target, new TemporaryAIFunctionFactoryOptions()
        {
            Name = method.GetCustomAttribute<McpServerToolAttribute>()?.Name,
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
                if (services is not null &&
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
        }));
    }

    /// <summary>Creates an <see cref="McpServerTool"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    /// <param name="function">The function to wrap.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike the other overloads of Create, the <see cref="McpServerTool"/> created by <see cref="Create(AIFunction)"/>
    /// does not provide all of the special parameter handling for MCP-specific concepts, like <see cref="IMcpServer"/>.
    /// </remarks>
    public static McpServerTool Create(AIFunction function)
    {
        Throw.IfNull(function);

        return new(function);
    }

    /// <summary>Initializes a new instance of the <see cref="McpServerTool"/> class.</summary>
    private McpServerTool(AIFunction function)
    {
        AIFunction = function;
        ProtocolTool = new()
        {
            Name = function.Name,
            Description = function.Description,
            InputSchema = function.JsonSchema,
        };
    }

    /// <summary>Gets the <see cref="AIFunction"/> wrapped by this tool.</summary>
    internal AIFunction AIFunction { get; }

    /// <summary>Gets the protocol <see cref="Tool"/> type for this instance.</summary>
    public Tool ProtocolTool { get; }

    /// <summary>Invokes the <see cref="McpServerTool"/>.</summary>
    /// <param name="request">The request information resulting in the invocation of this tool.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The call response from invoking the tool.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public async Task<CallToolResponse> InvokeAsync(
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

        switch (result)
        {
            case null:
                return new() 
                {
                    Content = []
                };

            case string text:
                return new()
                {
                    Content = [new() { Text = text, Type = "text" }] 
                };

            case TextContent textContent:
                return new() 
                {
                    Content = [new() { Text = textContent.Text, Type = "text" }] 
                };

            case DataContent dataContent:
                return new()
                {
                    Content = [new()
                    {
                        Data = dataContent.GetBase64Data(),
                        MimeType = dataContent.MediaType,
                        Type = dataContent.HasTopLevelMediaType("image") ? "image" : "resource",
                    }]
                };

            case string[] texts:
                return new() 
                {
                    Content = texts
                        .Select(x => new Content() { Type = "text" , Text = x ?? string.Empty })
                        .ToList() 
                };

            // TODO https://github.com/modelcontextprotocol/csharp-sdk/issues/69:
            // Add specialization for annotations.

            default:
                return new() 
                {
                    Content = [new() 
                    {
                        Text = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object))),
                        Type = "text"
                    }]
                };
        }
    }
}
