using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerTool"/> that's implemented via an <see cref="AIFunction"/>.</summary>
internal sealed partial class AIFunctionMcpServerTool : McpServerTool
{
    private readonly bool _structuredOutputRequiresWrapping;
    private readonly IReadOnlyList<object> _metadata;

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
                Debug.Assert(args.Services is RequestServiceProvider<CallToolRequestParams>, $"The service provider should be a {nameof(RequestServiceProvider<>)} for this method to work correctly.");
                return createTargetFunc(((RequestServiceProvider<CallToolRequestParams>)args.Services!).Request);
            }, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    private static AIFunctionFactoryOptions CreateAIFunctionFactoryOptions(
        MethodInfo method, McpServerToolCreateOptions? options) =>
        new()
        {
            Name = options?.Name ?? method.GetCustomAttribute<McpServerToolAttribute>()?.Name ?? DeriveName(method),
            Description = options?.Description,
            MarshalResult = static (result, _, cancellationToken) => new ValueTask<object?>(result),
            SerializerOptions = options?.SerializerOptions ?? McpJsonUtilities.DefaultOptions,
            JsonSchemaCreateOptions = options?.SchemaCreateOptions,
            ConfigureParameterBinding = pi =>
            {
                if (RequestServiceProvider<CallToolRequestParams>.IsAugmentedWith(pi.ParameterType) ||
                    (options?.Services?.GetService<IServiceProviderIsService>() is { } ispis &&
                     ispis.IsService(pi.ParameterType)))
                {
                    return new()
                    {
                        ExcludeFromSchema = true,
                        BindParameter = (pi, args) =>
                            args.Services?.GetService(pi.ParameterType) ??
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
                            (args?.Services as IKeyedServiceProvider)?.GetKeyedService(pi.ParameterType, keyedAttr.Key) ??
                            (pi.HasDefaultValue ? null :
                             throw new ArgumentException("No service of the requested type was found.")),
                    };
                }

                return default;
            },
        };

    /// <summary>Creates an <see cref="McpServerTool"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    public static new AIFunctionMcpServerTool Create(AIFunction function, McpServerToolCreateOptions? options)
    {
        Throw.IfNull(function);

        Tool tool = new()
        {
            Name = options?.Name ?? function.Name,
            Description = GetToolDescription(function, options),
            InputSchema = function.JsonSchema,
            OutputSchema = CreateOutputSchema(function, options, out bool structuredOutputRequiresWrapping),
            Icons = options?.Icons,
        };

        if (options is not null)
        {
            if (options.Title is not null ||
                options.Idempotent is not null ||
                options.Destructive is not null ||
                options.OpenWorld is not null ||
                options.ReadOnly is not null)
            {
                tool.Title = options.Title;

                tool.Annotations = new()
                {
                    Title = options.Title,
                    IdempotentHint = options.Idempotent,
                    DestructiveHint = options.Destructive,
                    OpenWorldHint = options.OpenWorld,
                    ReadOnlyHint = options.ReadOnly,
                };
            }

            // Populate Meta from options and/or McpMetaAttribute instances if a MethodInfo is available
            tool.Meta = function.UnderlyingMethod is not null ?
                CreateMetaFromAttributes(function.UnderlyingMethod, options.Meta) :
                options.Meta;

            // Apply user-specified Execution settings if provided
            if (options.Execution is not null)
            {
                tool.Execution = options.Execution;
            }
        }

        // Auto-detect async methods and mark with taskSupport = "optional" unless explicitly configured.
        // This enables implicit task support for async tools: clients can choose to invoke them
        // synchronously (wait for completion) or as a task (receive taskId, poll for result).
        if (function.UnderlyingMethod is not null && 
            IsAsyncMethod(function.UnderlyingMethod) &&
            tool.Execution?.TaskSupport is null)
        {
            tool.Execution ??= new ToolExecution();
            tool.Execution.TaskSupport = ToolTaskSupport.Optional;
        }

        return new AIFunctionMcpServerTool(function, tool, options?.Services, structuredOutputRequiresWrapping, options?.Metadata ?? []);
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

            if (newOptions.Icons is null && toolAttr.IconSource is { Length: > 0 } iconSource)
            {
                newOptions.Icons = [new() { Source = iconSource }];
            }

            newOptions.UseStructuredContent = toolAttr.UseStructuredContent;

            if (toolAttr._taskSupport is { } taskSupport)
            {
                newOptions.Execution ??= new ToolExecution();
                newOptions.Execution.TaskSupport ??= taskSupport;
            }
        }

        if (method.GetCustomAttribute<DescriptionAttribute>() is { } descAttr)
        {
            newOptions.Description ??= descAttr.Description;
        }

        // Set metadata if not already provided
        newOptions.Metadata ??= CreateMetadata(method);

        return newOptions;
    }

    /// <summary>Gets the <see cref="AIFunction"/> wrapped by this tool.</summary>
    internal AIFunction AIFunction { get; }

    /// <summary>Initializes a new instance of the <see cref="McpServerTool"/> class.</summary>
    private AIFunctionMcpServerTool(AIFunction function, Tool tool, IServiceProvider? serviceProvider, bool structuredOutputRequiresWrapping, IReadOnlyList<object> metadata)
    {
        ValidateToolName(tool.Name);

        AIFunction = function;
        ProtocolTool = tool;

        _structuredOutputRequiresWrapping = structuredOutputRequiresWrapping;
        _metadata = metadata;
    }

    /// <inheritdoc />
    public override Tool ProtocolTool { get; }

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata => _metadata;

    /// <inheritdoc />
    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        request.Services = new RequestServiceProvider<CallToolRequestParams>(request);
        AIFunctionArguments arguments = new() { Services = request.Services };

        if (request.Params?.Arguments is { } argDict)
        {
            foreach (var kvp in argDict)
            {
                arguments[kvp.Key] = kvp.Value;
            }
        }

        object? result;
        result = await AIFunction.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);

        JsonElement? structuredContent = CreateStructuredResponse(result);
        return result switch
        {
            AIContent aiContent => new()
            {
                Content = [aiContent.ToContentBlock()],
                StructuredContent = structuredContent,
                IsError = aiContent is ErrorContent
            },

            null => new()
            {
                Content = [],
                StructuredContent = structuredContent,
            },

            string text => new()
            {
                Content = [new TextContentBlock { Text = text }],
                StructuredContent = structuredContent,
            },

            ContentBlock content => new()
            {
                Content = [content],
                StructuredContent = structuredContent,
            },

            IEnumerable<AIContent> contentItems => ConvertAIContentEnumerableToCallToolResult(contentItems, structuredContent),

            IEnumerable<ContentBlock> contents => new()
            {
                Content = [.. contents],
                StructuredContent = structuredContent,
            },

            CallToolResult callToolResponse => callToolResponse,

            _ => new()
            {
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(result, AIFunction.JsonSerializerOptions.GetTypeInfo(typeof(object))) }],
                StructuredContent = structuredContent,
            },
        };
    }

    /// <summary>Creates a name to use based on the supplied method and naming policy.</summary>
    internal static string DeriveName(MethodInfo method, JsonNamingPolicy? policy = null)
    {
        string name = method.Name;

        // Remove any "Async" suffix if the method is an async method and if the method name isn't just "Async".
        const string AsyncSuffix = "Async";
        if (IsAsyncMethod(method) &&
            name.EndsWith(AsyncSuffix, StringComparison.Ordinal) &&
            name.Length > AsyncSuffix.Length)
        {
            name = name.Substring(0, name.Length - AsyncSuffix.Length);
        }

        // Replace anything other than ASCII letters or digits with underscores, trim off any leading or trailing underscores.
        name = NonAsciiLetterDigitsRegex().Replace(name, "_").Trim('_');

        // If after all our transformations the name is empty, just use the original method name.
        if (name.Length == 0)
        {
            name = method.Name;
        }

        // Case the name based on the provided naming policy.
        return (policy ?? JsonNamingPolicy.SnakeCaseLower).ConvertName(name) ?? name;
    }

    private static bool IsAsyncMethod(MethodInfo method)
    {
        Type t = method.ReturnType;

        if (t == typeof(Task) || t == typeof(ValueTask))
        {
            return true;
        }

        if (t.IsGenericType)
        {
            t = t.GetGenericTypeDefinition();
            if (t == typeof(Task<>) || t == typeof(ValueTask<>) || t == typeof(IAsyncEnumerable<>))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Creates metadata from attributes on the specified method and its declaring class, with the MethodInfo as the first item.</summary>
    internal static IReadOnlyList<object> CreateMetadata(MethodInfo method)
    {
        // Add the MethodInfo to the start of the metadata similar to what RouteEndpointDataSource does for minimal endpoints.
        List<object> metadata = [method];

        // Add class-level attributes first, since those are less specific.
        if (method.DeclaringType is not null)
        {
            metadata.AddRange(method.DeclaringType.GetCustomAttributes());
        }

        // Add method-level attributes second, since those are more specific.
        // When metadata conflicts, later metadata usually takes precedence with exceptions for metadata like
        // IAllowAnonymous which always take precedence over IAuthorizeData no matter the order.
        metadata.AddRange(method.GetCustomAttributes());

        return metadata.AsReadOnly();
    }

    /// <summary>Creates a Meta <see cref="JsonObject"/> from <see cref="McpMetaAttribute"/> instances on the specified method.</summary>
    /// <param name="method">The method to extract <see cref="McpMetaAttribute"/> instances from.</param>
    /// <param name="meta">Optional <see cref="JsonObject"/> to seed the Meta with. Properties from this object take precedence over attributes.</param>
    /// <returns>A <see cref="JsonObject"/> with metadata, or null if no metadata is present.</returns>
    internal static JsonObject? CreateMetaFromAttributes(MethodInfo method, JsonObject? meta = null)
    {
        // Transfer all McpMetaAttribute instances to the Meta JsonObject, ignoring any that would overwrite existing properties.
        foreach (var attr in method.GetCustomAttributes<McpMetaAttribute>())
        {
            if (meta?.ContainsKey(attr.Name) is not true)
            {
                (meta ??= [])[attr.Name] = JsonNode.Parse(attr.JsonValue);
            }
        }

        return meta;
    }

#if NET
    /// <summary>Regex that flags runs of characters other than ASCII digits or letters.</summary>
    [GeneratedRegex("[^0-9A-Za-z]+")]
    private static partial Regex NonAsciiLetterDigitsRegex();

    /// <summary>Regex that validates tool names.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,128}\z")]
    private static partial Regex ValidateToolNameRegex();
#else
    private static Regex NonAsciiLetterDigitsRegex() => _nonAsciiLetterDigits;
    private static readonly Regex _nonAsciiLetterDigits = new("[^0-9A-Za-z]+", RegexOptions.Compiled);

    private static Regex ValidateToolNameRegex() => _validateToolName;
    private static readonly Regex _validateToolName = new(@"^[A-Za-z0-9_.-]{1,128}\z", RegexOptions.Compiled);
#endif

    private static void ValidateToolName(string name)
    {
        if (name is null)
        {
            throw new ArgumentException("Tool name cannot be null.");
        }

        if (!ValidateToolNameRegex().IsMatch(name))
        {
            throw new ArgumentException($"The tool name '{name}' is invalid. Tool names must match the regular expression '{ValidateToolNameRegex()}'");
        }
    }

    /// <summary>
    /// Gets the tool description, synthesizing from both the function description and return description when appropriate.
    /// </summary>
    /// <remarks>
    /// When UseStructuredContent is true, the return description is included in the output schema.
    /// When UseStructuredContent is false (default), if there's a return description in the ReturnJsonSchema,
    /// it will be appended to the tool description so the information is still available to consumers.
    /// </remarks>
    private static string? GetToolDescription(AIFunction function, McpServerToolCreateOptions? options)
    {
        string? description = options?.Description ?? function.Description;

        // If structured content is enabled, the return description will be in the output schema
        if (options?.UseStructuredContent is true)
        {
            return description;
        }

        // When structured content is disabled, try to extract the return description from ReturnJsonSchema
        // and append it to the tool description so the information is available to consumers
        string? returnDescription = GetReturnDescription(function.ReturnJsonSchema);
        if (string.IsNullOrWhiteSpace(returnDescription))
        {
            return description;
        }

        // Synthesize a combined description
        if (string.IsNullOrWhiteSpace(description))
        {
            return $"Returns: {returnDescription}";
        }

        return $"{description}\nReturns: {returnDescription}";
    }

    /// <summary>
    /// Extracts the description property from a ReturnJsonSchema if present.
    /// </summary>
    private static string? GetReturnDescription(JsonElement? returnJsonSchema)
    {
        if (returnJsonSchema is not JsonElement schema ||
            schema.ValueKind is not JsonValueKind.Object ||
            !schema.TryGetProperty("description", out JsonElement descriptionElement) ||
            descriptionElement.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        return descriptionElement.GetString();
    }

    private static JsonElement? CreateOutputSchema(AIFunction function, McpServerToolCreateOptions? toolCreateOptions, out bool structuredOutputRequiresWrapping)
    {
        structuredOutputRequiresWrapping = false;

        if (toolCreateOptions?.UseStructuredContent is not true)
        {
            return null;
        }

        if (function.ReturnJsonSchema is not JsonElement outputSchema)
        {
            return null;
        }

        if (outputSchema.ValueKind is not JsonValueKind.Object ||
            !outputSchema.TryGetProperty("type", out JsonElement typeProperty) ||
            typeProperty.ValueKind is not JsonValueKind.String ||
            typeProperty.GetString() is not "object")
        {
            // If the output schema is not an object, need to modify to be a valid MCP output schema.
            JsonNode? schemaNode = JsonSerializer.SerializeToNode(outputSchema, McpJsonUtilities.JsonContext.Default.JsonElement);

            if (schemaNode is JsonObject objSchema &&
                objSchema.TryGetPropertyValue("type", out JsonNode? typeNode) &&
                typeNode is JsonArray { Count: 2 } typeArray && typeArray.Any(type => (string?)type is "object") && typeArray.Any(type => (string?)type is "null"))
            {
                // For schemas that are of type ["object", "null"], replace with just "object" to be conformant.
                objSchema["type"] = "object";
            }
            else
            {
                // For anything else, wrap the schema in an envelope with a "result" property.
                schemaNode = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["result"] = schemaNode
                    },
                    ["required"] = new JsonArray { (JsonNode)"result" }
                };

                structuredOutputRequiresWrapping = true;
            }

            outputSchema = JsonSerializer.Deserialize(schemaNode, McpJsonUtilities.JsonContext.Default.JsonElement);
        }

        return outputSchema;
    }

    private JsonElement? CreateStructuredResponse(object? aiFunctionResult)
    {
        if (ProtocolTool.OutputSchema is null)
        {
            // Only provide structured responses if the tool has an output schema defined.
            return null;
        }

        JsonElement? elementResult = aiFunctionResult switch
        {
            JsonElement jsonElement => jsonElement,
            JsonNode node => JsonSerializer.SerializeToElement(node, McpJsonUtilities.JsonContext.Default.JsonNode),
            null => null,
            _ => JsonSerializer.SerializeToElement(aiFunctionResult, AIFunction.JsonSerializerOptions.GetTypeInfo(typeof(object))),
        };

        if (_structuredOutputRequiresWrapping)
        {
            JsonNode? resultNode = elementResult is { } je
                ? JsonSerializer.SerializeToNode(je, McpJsonUtilities.JsonContext.Default.JsonElement)
                : null;
            return JsonSerializer.SerializeToElement(new JsonObject
            {
                ["result"] = resultNode
            }, McpJsonUtilities.JsonContext.Default.JsonObject);
        }

        return elementResult;
    }

    private static CallToolResult ConvertAIContentEnumerableToCallToolResult(IEnumerable<AIContent> contentItems, JsonElement? structuredContent)
    {
        List<ContentBlock> contentList = [];
        bool allErrorContent = true;
        bool hasAny = false;

        foreach (var item in contentItems)
        {
            contentList.Add(item.ToContentBlock());
            hasAny = true;

            if (allErrorContent && item is not ErrorContent)
            {
                allErrorContent = false;
            }
        }

        return new()
        {
            Content = contentList,
            StructuredContent = structuredContent,
            IsError = allErrorContent && hasAny
        };
    }
}