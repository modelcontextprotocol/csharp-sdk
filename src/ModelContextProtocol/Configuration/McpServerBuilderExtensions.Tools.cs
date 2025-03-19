using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.AI;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace ModelContextProtocol;

/// <summary>
/// Extension to configure the MCP server with tools
/// </summary>
public static partial class McpServerBuilderExtensions
{
    private const string RequiresUnreferencedCodeMessage = "This method requires dynamic lookup of method metadata and might not work in Native AOT.";

    /// <summary>
    /// Adds a tool to the server.
    /// </summary>
    /// <typeparam name="TTool">The tool type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithTool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TTool>(this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);
        List<AIFunction> functions = [];

        PopulateFunctions(typeof(TTool), functions);
        return WithTools(builder, functions);
    }
    /// <summary>
    /// Adds all tools marked with <see cref="McpToolTypeAttribute"/> from the current assembly to the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithTools(this IMcpServerBuilder builder)
    {
        return WithToolsFromAssembly(builder, Assembly.GetCallingAssembly());
    }

    /// <summary>
    /// Adds tools to the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="toolTypes">Types with marked methods to add as tools to the server.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="toolTypes"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithTools(this IMcpServerBuilder builder, params IEnumerable<Type> toolTypes)
    {
        Throw.IfNull(builder);
        Throw.IfNull(toolTypes);

        List<AIFunction> functions = [];

        foreach (var toolType in toolTypes)
        {
            if (toolType is null)
            {
                throw new ArgumentNullException(nameof(toolTypes), $"A tool type provided by the enumerator was null.");
            }

            PopulateFunctions(toolType, functions);
        }

        return WithTools(builder, functions);
    }

    /// <summary>
    /// Adds tools to the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="functions"><see cref="AIFunction"/> instances to use as the tools.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="functions"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithTools(this IMcpServerBuilder builder, params IEnumerable<AIFunction> functions)
    {
        Throw.IfNull(builder);
        Throw.IfNull(functions);

        List<Tool> tools = [];
        Dictionary<string, Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<CallToolResponse>>> callbacks = [];

        foreach (AIFunction function in functions)
        {
            if (function is null)
            {
                throw new ArgumentNullException(nameof(functions), $"A function provided by the enumerator was null.");
            }

            tools.Add(new()
            {
                Name = function.Name,
                Description = function.Description,
                InputSchema = function.JsonSchema,
            });

            callbacks.Add(function.Name, async (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                object? result;
                try
                {
                    result = await function.InvokeAsync((request.Params?.Arguments ?? [])!, cancellationToken).ConfigureAwait(false);
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
                    case JsonElement je when je.ValueKind == JsonValueKind.Null:
                        return new() { Content = [] };

                    case JsonElement je when je.ValueKind == JsonValueKind.Array:
                        return new() { Content = je.EnumerateArray().Select(x => new Content() { Text = x.ToString(), Type = "text" }).ToList() };

                    default:
                        return new() { Content = [new() { Text = result?.ToString(), Type = "text" }] };
                }
            });
        }

        builder.WithListToolsHandler((_, _) => Task.FromResult(new ListToolsResult() { Tools = tools }));

        builder.WithCallToolHandler(async (request, cancellationToken) =>
        {
            if (request.Params is null || !callbacks.TryGetValue(request.Params.Name, out var callback))
            {
                throw new McpServerException($"Unknown tool '{request.Params?.Name}'");
            }

            return await callback(request, cancellationToken).ConfigureAwait(false);
        });

        return builder;
    }

    /// <summary>
    /// Adds types marked with the <see cref="McpToolTypeAttribute"/> attribute from the given assembly as tools to the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="assembly">The assembly to load the types from. Null to get the current assembly</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithToolsFromAssembly(this IMcpServerBuilder builder, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();

        List<Type> toolTypes = [];

        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpToolTypeAttribute>() is null)
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<McpToolAttribute>() is not null)
                {
                    toolTypes.Add(type);
                    break;
                }
            }
        }

        return toolTypes.Count > 0 ?
            WithTools(builder, toolTypes) :
            builder;
    }

    private static void PopulateFunctions(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type toolType, 
        List<AIFunction> functions)
    {
        foreach (var method in toolType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.GetCustomAttribute<McpToolAttribute>() is not { } attribute)
            {
                continue;
            }

            functions.Add(AIFunctionFactory.Create(method, target: null, new()
            {
                Name = attribute.Name ?? method.Name,
            }));
        }
    }
}
