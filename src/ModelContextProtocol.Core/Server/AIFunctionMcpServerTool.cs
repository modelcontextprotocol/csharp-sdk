using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerTool"/> that's implemented via an <see cref="AIFunction"/>.</summary>
internal sealed partial class AIFunctionMcpServerTool : McpServerTool
{
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
            OutputSchema = CreateOutputSchema(function, options),
            Icons = options?.Icons,
        };

        // Add x-mcp-header extensions to the input schema based on McpHeaderAttribute on parameters
        if (function.UnderlyingMethod is { } method)
        {
            tool.InputSchema = AddMcpHeaderExtensions(tool.InputSchema, method);
        }

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
        }

        return new AIFunctionMcpServerTool(function, tool, options?.Services, options?.Metadata ?? []);
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

            if (toolAttr.OutputSchemaType is Type outputSchemaType)
            {
                newOptions.OutputSchema ??= AIJsonUtilities.CreateJsonSchema(outputSchemaType,
                    serializerOptions: newOptions.SerializerOptions ?? McpJsonUtilities.DefaultOptions,
                    inferenceOptions: newOptions.SchemaCreateOptions);
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
    private AIFunctionMcpServerTool(AIFunction function, Tool tool, IServiceProvider? serviceProvider, IReadOnlyList<object> metadata)
    {
        ValidateToolName(tool.Name);

        AIFunction = function;
        ProtocolTool = tool;

        _metadata = metadata;
    }

    /// <inheritdoc />
    public override Tool ProtocolTool { get; }

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata => _metadata;

    /// <summary>
    /// Returns a <see cref="Tool"/> clone whose <see cref="Tool.OutputSchema"/> is rewritten
    /// into the wire shape required by clients on protocol versions older than
    /// <c>"2026-07-28"</c>. Those versions require <c>outputSchema.type == "object"</c>;
    /// SEP-2106 (negotiated at <c>"2026-07-28"</c> and later) widens that to any JSON
    /// Schema 2020-12 document. To stay compatible, non-object schemas are wrapped in
    /// <c>{"type":"object","properties":{"result":&lt;schema&gt;}}</c> and the
    /// <c>type:["object","null"]</c> array form is normalized to plain <c>"object"</c>
    /// before emission. Returns <see cref="ProtocolTool"/> unchanged when there is no
    /// output schema. Callers must gate the call on the negotiated version — this method
    /// is unconditional; the gate lives at the emission site.
    /// </summary>
    internal Tool BuildLegacyWireProtocolTool()
    {
        if (ProtocolTool.OutputSchema is not { } natural)
        {
            return ProtocolTool;
        }

        JsonElement legacyOutputSchema = TransformOutputSchemaForLegacyWire(natural);

        return new Tool
        {
            Name = ProtocolTool.Name,
            Title = ProtocolTool.Title,
            Description = ProtocolTool.Description,
            InputSchema = ProtocolTool.InputSchema,
            OutputSchema = legacyOutputSchema,
            Annotations = ProtocolTool.Annotations,
            Icons = ProtocolTool.Icons,
            Meta = ProtocolTool.Meta,
        };
    }

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

        JsonElement? structuredContent = CreateStructuredResponse(result, request.Server.NegotiatedProtocolVersion);
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

        static bool IsAsyncMethod(MethodInfo method)
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

    private static JsonElement? CreateOutputSchema(AIFunction function, McpServerToolCreateOptions? toolCreateOptions)
    {
        if (toolCreateOptions?.UseStructuredContent is not true)
        {
            return null;
        }

        // Per SEP-2106, any valid JSON Schema document is acceptable for outputSchema —
        // arrays, primitives, compositions, and nullable types pass through unchanged.
        // Explicit OutputSchema takes precedence over AIFunction's return schema.
        // Back-compat for pre-2026-07-28 clients is applied at the wire emission sites
        // (CreateStructuredResponse for tools/call, listToolsHandler for tools/list).
        if (toolCreateOptions.OutputSchema is { } explicitSchema)
        {
            return explicitSchema;
        }

        if (function.ReturnJsonSchema is { } returnSchema)
        {
            return returnSchema;
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> iff the structured-content value must be wrapped in
    /// the <c>{"result": &lt;value&gt;}</c> envelope on the wire — i.e., the output schema
    /// is neither plain object-typed (<c>type:"object"</c>) nor the
    /// <c>type:["object","null"]</c> array form. Used by <see cref="CreateStructuredResponse"/>
    /// to decide whether to apply the envelope when emitting to a client that negotiated a
    /// protocol version older than <c>"2026-07-28"</c> (those versions pre-date SEP-2106's
    /// allowance of non-object output schemas). The inner <c>type:["object","null"]</c>
    /// check is hoisted into a named bool to keep the surrounding control flow free of
    /// empty branches.
    /// </summary>
    internal static bool ShouldWrapValueForLegacyWire(JsonElement schema)
    {
        bool structuredOutputRequiresWrapping = false;

        if (schema.ValueKind is not JsonValueKind.Object ||
            !schema.TryGetProperty("type", out JsonElement typeProperty) ||
            typeProperty.ValueKind is not JsonValueKind.String ||
            typeProperty.GetString() is not "object")
        {
            JsonNode? schemaNode = JsonSerializer.SerializeToNode(schema, McpJsonUtilities.JsonContext.Default.JsonElement);

            bool isNullableObjectArray =
                schemaNode is JsonObject objSchema &&
                objSchema.TryGetPropertyValue("type", out JsonNode? typeNode) &&
                typeNode is JsonArray { Count: 2 } typeArray &&
                typeArray.Any(type => (string?)type is "object") &&
                typeArray.Any(type => (string?)type is "null");

            if (!isNullableObjectArray)
            {
                structuredOutputRequiresWrapping = true;
            }
        }

        return structuredOutputRequiresWrapping;
    }

    /// <summary>
    /// Transforms <paramref name="naturalSchema"/> into the wire shape required by clients
    /// on protocol versions older than <c>"2026-07-28"</c>: non-object schemas are wrapped
    /// in <c>{"type":"object","properties":{"result":&lt;schema&gt;},"required":["result"]}</c>,
    /// the <c>type:["object","null"]</c> array form is normalized to plain <c>"object"</c>,
    /// and plain object-typed schemas pass through unchanged. SEP-2106 clients
    /// (<c>"2026-07-28"</c>+) see the natural schema and never need this transform.
    /// Dispatches on <see cref="ShouldWrapValueForLegacyWire"/> so the wrap decision lives
    /// in one place.
    /// </summary>
    /// <param name="naturalSchema">The natural JSON Schema 2020-12 document.</param>
    internal static JsonElement TransformOutputSchemaForLegacyWire(JsonElement naturalSchema)
    {
        if (naturalSchema.ValueKind is not JsonValueKind.Object ||
            !naturalSchema.TryGetProperty("type", out JsonElement typeProperty) ||
            typeProperty.ValueKind is not JsonValueKind.String ||
            typeProperty.GetString() is not "object")
        {
            JsonNode? schemaNode = JsonSerializer.SerializeToNode(naturalSchema, McpJsonUtilities.JsonContext.Default.JsonElement);

            if (schemaNode is JsonObject objSchema &&
                objSchema.TryGetPropertyValue("type", out JsonNode? typeNode) &&
                typeNode is JsonArray { Count: 2 } typeArray &&
                typeArray.Any(type => (string?)type is "object") &&
                typeArray.Any(type => (string?)type is "null"))
            {
                // type:["object","null"] → normalize to plain "object". No envelope.
                objSchema["type"] = "object";
            }
            else
            {
                // Anything else (string, integer, array, boolean schemas, missing type,
                // compositions). Wrap in the {"result": <schema>} envelope.
                schemaNode = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["result"] = schemaNode
                    },
                    ["required"] = new JsonArray { (JsonNode)"result" }
                };

                // After wrapping, any internal $ref pointers that used absolute JSON Pointer
                // paths (e.g., "#/items/..." or "#") are now invalid because the original schema
                // has moved under "#/properties/result". Rewrite them to account for the new location.
                RewriteRefPointers(schemaNode["properties"]!["result"]);
            }

            return JsonSerializer.Deserialize(schemaNode, McpJsonUtilities.JsonContext.Default.JsonElement);
        }

        return naturalSchema;
    }

    /// <summary>
    /// Recursively rewrites all <c>$ref</c> JSON Pointer values in the given node
    /// to account for the schema having been wrapped under <c>properties.result</c>.
    /// </summary>
    /// <remarks>
    /// <c>System.Text.Json</c>'s <see cref="System.Text.Json.Schema.JsonSchemaExporter"/> uses absolute
    /// JSON Pointer paths (e.g., <c>#/items/properties/foo</c>) to deduplicate types that appear at
    /// multiple locations in the schema. When the original schema is moved under
    /// <c>#/properties/result</c> by the wrapping logic above, these pointers become unresolvable.
    /// This method prepends <c>/properties/result</c> to every <c>$ref</c> that starts with <c>#/</c>,
    /// and rewrites bare <c>#</c> (root self-references from recursive types) to <c>#/properties/result</c>,
    /// so the pointers remain valid after wrapping.
    /// </remarks>
    private static void RewriteRefPointers(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out JsonNode? refNode) &&
                refNode?.GetValue<string>() is string refValue)
            {
                if (refValue == "#")
                {
                    obj["$ref"] = "#/properties/result";
                }
                else if (refValue.StartsWith("#/", StringComparison.Ordinal))
                {
                    obj["$ref"] = "#/properties/result" + refValue.Substring(1);
                }
            }

            // Safe to iterate without snapshot: the $ref assignment above completes before
            // this enumerator is created, and recursive calls only mutate descendant objects.
            foreach (var property in obj)
            {
                RewriteRefPointers(property.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                RewriteRefPointers(item);
            }
        }
    }

    private JsonElement? CreateStructuredResponse(object? aiFunctionResult, string? negotiatedProtocolVersion)
    {
        if (ProtocolTool.OutputSchema is null)
        {
            // Only provide structured responses if the tool has an output schema defined.
            return null;
        }

        JsonElement? elementResult;
        JsonElement? elementResultIncludingDefaults;
        switch (aiFunctionResult)
        {
            case JsonElement jsonElement:
                elementResult = elementResultIncludingDefaults = jsonElement;
                break;
            case JsonNode node:
                elementResult = elementResultIncludingDefaults = JsonSerializer.SerializeToElement(node, McpJsonUtilities.JsonContext.Default.JsonNode);
                break;
            case null:
                elementResult = elementResultIncludingDefaults = null;
                break;
            default:
                JsonSerializerOptions serializerOptions = AIFunction.JsonSerializerOptions;
                JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(typeof(object));
                elementResult = JsonSerializer.SerializeToElement(aiFunctionResult, typeInfo);

                if (serializerOptions.DefaultIgnoreCondition is JsonIgnoreCondition.Never)
                {
                    elementResultIncludingDefaults = elementResult;
                }
                else
                {
                    JsonSerializerOptions includeDefaultsOptions = new(serializerOptions)
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                    };
                    elementResultIncludingDefaults = JsonSerializer.SerializeToElement(
                        aiFunctionResult, includeDefaultsOptions.GetTypeInfo(typeof(object)));
                }
                break;
        }

        elementResult = AddMissingRequiredNullableProperties(elementResult, elementResultIncludingDefaults);

        // Pre-SEP-2106 clients expect the {"result": <value>} envelope for non-object
        // schemas. SEP-2106 clients see the natural shape. The classification is decided
        // fresh per request from the stored natural schema.
        if (!McpSessionHandler.SupportsNaturalOutputSchemas(negotiatedProtocolVersion) &&
            ShouldWrapValueForLegacyWire(ProtocolTool.OutputSchema.Value))
        {
            JsonNode? resultNode = elementResult is { } v
                ? JsonSerializer.SerializeToNode(v, McpJsonUtilities.JsonContext.Default.JsonElement)
                : null;
            return JsonSerializer.SerializeToElement(new JsonObject
            {
                ["result"] = resultNode
            }, McpJsonUtilities.JsonContext.Default.JsonObject);
        }

        return elementResult;
    }

    private JsonElement? AddMissingRequiredNullableProperties(JsonElement? elementResult, JsonElement? elementResultIncludingDefaults)
    {
        if (elementResult is not { } result ||
            ProtocolTool.OutputSchema is not { } outputSchema ||
            result.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return elementResult;
        }

        JsonNode? resultNode = JsonSerializer.SerializeToNode(result, McpJsonUtilities.JsonContext.Default.JsonElement);
        JsonNode? resultNodeIncludingDefaults = elementResultIncludingDefaults is { } completeResult
            ? JsonSerializer.SerializeToNode(completeResult, McpJsonUtilities.JsonContext.Default.JsonElement)
            : null;
        JsonNode? schemaNode = JsonSerializer.SerializeToNode(outputSchema, McpJsonUtilities.JsonContext.Default.JsonElement);
        if (resultNode is null || schemaNode is null || !AddMissingRequiredNullableProperties(
                resultNode,
                resultNodeIncludingDefaults,
                schemaNode,
                schemaNode,
                new HashSet<string>(StringComparer.Ordinal)))
        {
            return elementResult;
        }

        return JsonSerializer.SerializeToElement(resultNode, McpJsonUtilities.JsonContext.Default.JsonNode);
    }

    private static bool AddMissingRequiredNullableProperties(
        JsonNode resultNode,
        JsonNode? resultNodeIncludingDefaults,
        JsonNode? schemaNode,
        JsonNode schemaRoot,
        HashSet<string> activeReferences)
    {
        if (schemaNode is JsonValue schemaValue && schemaValue.TryGetValue<bool>(out _))
        {
            return false;
        }

        if (schemaNode is not JsonObject schemaObject)
        {
            return false;
        }

        bool changed = false;
        if (schemaObject["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out string? refText) &&
            refText is not null && activeReferences.Add(refText))
        {
            if (TryResolveLocalReference(schemaRoot, refText, out JsonNode? referencedSchema))
            {
                changed |= AddMissingRequiredNullableProperties(resultNode, resultNodeIncludingDefaults, referencedSchema, schemaRoot, activeReferences);
            }
            activeReferences.Remove(refText);
        }

        if (resultNode is JsonObject resultObject)
        {
            JsonObject? resultObjectIncludingDefaults = resultNodeIncludingDefaults as JsonObject;
            JsonObject? properties = schemaObject["properties"] as JsonObject;
            JsonObject? patternProperties = schemaObject["patternProperties"] as JsonObject;
            foreach (string propertyName in resultObject.Select(static property => property.Key).ToArray())
            {
                if (resultObject[propertyName] is not { } propertyValue)
                {
                    continue;
                }

                List<JsonNode> propertySchemas = [];
                JsonNode? propertySchema = null;
                bool matchedPropertySchema = properties?.TryGetPropertyValue(propertyName, out propertySchema) is true;
                if (matchedPropertySchema && propertySchema is not null)
                {
                    propertySchemas.Add(propertySchema);
                }

                if (patternProperties is not null)
                {
                    foreach (KeyValuePair<string, JsonNode?> patternProperty in patternProperties)
                    {
                        if (patternProperty.Value is not null && Regex.IsMatch(propertyName, patternProperty.Key) &&
                            !propertySchemas.Contains(patternProperty.Value))
                        {
                            propertySchemas.Add(patternProperty.Value);
                            matchedPropertySchema = true;
                        }
                    }
                }

                if (!matchedPropertySchema && schemaObject["additionalProperties"] is JsonNode additionalProperties)
                {
                    propertySchemas.Add(additionalProperties);
                }

                JsonNode? completePropertyValue = null;
                resultObjectIncludingDefaults?.TryGetPropertyValue(propertyName, out completePropertyValue);
                foreach (JsonNode childSchema in propertySchemas)
                {
                    changed |= AddMissingRequiredNullableProperties(propertyValue, completePropertyValue, childSchema, schemaRoot, new HashSet<string>(StringComparer.Ordinal));
                }
            }

            if (schemaObject["required"] is JsonArray required && properties is not null)
            {
                foreach (JsonNode? requiredProperty in required)
                {
                    string? propertyName = requiredProperty?.GetValue<string>();
                    if (propertyName is null || resultObject.ContainsKey(propertyName) ||
                        !properties.TryGetPropertyValue(propertyName, out JsonNode? propertySchema))
                    {
                        continue;
                    }

                    bool allowsNull = SchemaAllowsNull(propertySchema, schemaRoot, new HashSet<string>(StringComparer.Ordinal));
                    if (resultObjectIncludingDefaults?.TryGetPropertyValue(propertyName, out JsonNode? completePropertyValue) is true)
                    {
                        if (completePropertyValue is not null || allowsNull)
                        {
                            resultObject[propertyName] = completePropertyValue?.DeepClone();
                            changed = true;
                        }
                    }
                    else if (allowsNull)
                    {
                        resultObject[propertyName] = null;
                        changed = true;
                    }
                }
            }
        }

        if (resultNode is JsonArray resultArray)
        {
            JsonArray? resultArrayIncludingDefaults = resultNodeIncludingDefaults as JsonArray;
            JsonArray? prefixItems = schemaObject["prefixItems"] as JsonArray;
            JsonNode? items = schemaObject["items"];
            for (int i = 0; i < resultArray.Count; i++)
            {
                JsonNode? itemSchema = prefixItems is not null && i < prefixItems.Count ? prefixItems[i] : items;
                if (resultArray[i] is { } item && itemSchema is not null)
                {
                    JsonNode? completeItem = resultArrayIncludingDefaults is not null && i < resultArrayIncludingDefaults.Count
                        ? resultArrayIncludingDefaults[i]
                        : null;
                    changed |= AddMissingRequiredNullableProperties(item, completeItem, itemSchema, schemaRoot, new HashSet<string>(StringComparer.Ordinal));
                }
            }
        }

        if (schemaObject["allOf"] is JsonArray allOf)
        {
            foreach (JsonNode? branch in allOf)
            {
                if (branch is not null)
                {
                    changed |= AddMissingRequiredNullableProperties(resultNode, resultNodeIncludingDefaults, branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal));
                }
            }
        }

        if (schemaObject["anyOf"] is JsonArray anyOf)
        {
            changed |= ApplyApplicableCompositionBranch(resultNode, resultNodeIncludingDefaults, anyOf, schemaRoot, exactlyOne: false);
        }

        if (schemaObject["oneOf"] is JsonArray oneOf)
        {
            changed |= ApplyApplicableCompositionBranch(resultNode, resultNodeIncludingDefaults, oneOf, schemaRoot, exactlyOne: true);
        }

        return changed;
    }

    private static bool ApplyApplicableCompositionBranch(
        JsonNode resultNode,
        JsonNode? resultNodeIncludingDefaults,
        JsonArray branches,
        JsonNode schemaRoot,
        bool exactlyOne)
    {
        List<JsonNode> applicableBranches = [];
        foreach (JsonNode? branch in branches)
        {
            if (branch is not null && SchemaCouldMatch(resultNode, branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal)))
            {
                applicableBranches.Add(branch);
            }
        }

        if (applicableBranches.Count == 0 || (exactlyOne && applicableBranches.Count != 1))
        {
            return false;
        }

        IEnumerable<JsonNode> branchesToApply = exactlyOne
            ? applicableBranches
            : applicableBranches.OrderBy(branch => CountMissingNullableRequirements(resultNode, branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal))).Take(1);
        bool changed = false;
        foreach (JsonNode branch in branchesToApply)
        {
            changed |= AddMissingRequiredNullableProperties(resultNode, resultNodeIncludingDefaults, branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal));
        }

        return changed;
    }

    private static int CountMissingNullableRequirements(JsonNode resultNode, JsonNode schemaNode, JsonNode schemaRoot, HashSet<string> activeReferences)
    {
        if (schemaNode is not JsonObject schemaObject)
        {
            return 0;
        }

        int count = 0;
        if (resultNode is JsonObject resultObject && schemaObject["required"] is JsonArray required && schemaObject["properties"] is JsonObject properties)
        {
            foreach (JsonNode? requiredProperty in required)
            {
                string? propertyName = requiredProperty?.GetValue<string>();
                if (propertyName is not null && !resultObject.ContainsKey(propertyName) &&
                    properties.TryGetPropertyValue(propertyName, out JsonNode? propertySchema) &&
                    SchemaAllowsNull(propertySchema, schemaRoot, new HashSet<string>(StringComparer.Ordinal)))
                {
                    count++;
                }
            }
        }

        if (schemaObject["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out string? refText) &&
            refText is not null && activeReferences.Add(refText) && TryResolveLocalReference(schemaRoot, refText, out JsonNode? referencedSchema))
        {
            count += CountMissingNullableRequirements(resultNode, referencedSchema!, schemaRoot, activeReferences);
            activeReferences.Remove(refText);
        }

        if (schemaObject["allOf"] is JsonArray allOf)
        {
            foreach (JsonNode? branch in allOf)
            {
                if (branch is not null)
                {
                    count += CountMissingNullableRequirements(resultNode, branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal));
                }
            }
        }

        return count;
    }

    private static bool SchemaCouldMatch(JsonNode? instance, JsonNode? schemaNode, JsonNode schemaRoot, HashSet<string> activeReferences)
    {
        if (schemaNode is JsonValue booleanSchema && booleanSchema.TryGetValue<bool>(out bool isAllowed))
        {
            return isAllowed;
        }

        if (schemaNode is not JsonObject schemaObject)
        {
            return true;
        }

        if (schemaObject["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out string? refText) &&
            refText is not null && activeReferences.Add(refText))
        {
            if (!TryResolveLocalReference(schemaRoot, refText, out JsonNode? referencedSchema) ||
                !SchemaCouldMatch(instance, referencedSchema, schemaRoot, activeReferences))
            {
                activeReferences.Remove(refText);
                return false;
            }
            activeReferences.Remove(refText);
        }

        if (schemaObject["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out string? type) &&
            !ValueMatchesType(instance, type))
        {
            return false;
        }

        if (schemaObject["type"] is JsonArray types && !types.Any(typeNode =>
                typeNode is JsonValue typeName && typeName.TryGetValue<string>(out string? name) && ValueMatchesType(instance, name)))
        {
            return false;
        }

        if (schemaObject.TryGetPropertyValue("const", out JsonNode? constValue) && !JsonNodesEqual(instance, constValue))
        {
            return false;
        }

        if (schemaObject["enum"] is JsonArray enumValues && !enumValues.Any(value => JsonNodesEqual(instance, value)))
        {
            return false;
        }

        if (instance is JsonObject instanceObject)
        {
            if (schemaObject["required"] is JsonArray required && schemaObject["properties"] is JsonObject properties)
            {
                foreach (JsonNode? requiredProperty in required)
                {
                    string? propertyName = requiredProperty?.GetValue<string>();
                    if (propertyName is not null && !instanceObject.ContainsKey(propertyName) &&
                        (!properties.TryGetPropertyValue(propertyName, out JsonNode? propertySchema) ||
                         !SchemaAllowsNull(propertySchema, schemaRoot, new HashSet<string>(StringComparer.Ordinal))))
                    {
                        return false;
                    }
                }
            }

            JsonObject? propertiesForInstance = schemaObject["properties"] as JsonObject;
            JsonObject? patternProperties = schemaObject["patternProperties"] as JsonObject;
            foreach (KeyValuePair<string, JsonNode?> property in instanceObject)
            {
                bool matched = false;
                if (propertiesForInstance?.TryGetPropertyValue(property.Key, out JsonNode? propertySchema) is true)
                {
                    matched = true;
                    if (!SchemaCouldMatch(property.Value, propertySchema, schemaRoot, new HashSet<string>(StringComparer.Ordinal)))
                    {
                        return false;
                    }
                }

                if (patternProperties is not null)
                {
                    foreach (KeyValuePair<string, JsonNode?> patternProperty in patternProperties)
                    {
                        if (Regex.IsMatch(property.Key, patternProperty.Key))
                        {
                            matched = true;
                            if (!SchemaCouldMatch(property.Value, patternProperty.Value, schemaRoot, new HashSet<string>(StringComparer.Ordinal)))
                            {
                                return false;
                            }
                        }
                    }
                }

                if (!matched && schemaObject["additionalProperties"] is JsonValue additionalBoolean &&
                    additionalBoolean.TryGetValue<bool>(out bool allowsAdditionalProperties) && !allowsAdditionalProperties)
                {
                    return false;
                }
            }
        }

        if (instance is JsonArray instanceArray)
        {
            JsonArray? prefixItems = schemaObject["prefixItems"] as JsonArray;
            JsonNode? items = schemaObject["items"];
            for (int i = 0; i < instanceArray.Count; i++)
            {
                JsonNode? itemSchema = prefixItems is not null && i < prefixItems.Count ? prefixItems[i] : items;
                if (itemSchema is not null && !SchemaCouldMatch(instanceArray[i], itemSchema, schemaRoot, new HashSet<string>(StringComparer.Ordinal)))
                {
                    return false;
                }
            }
        }

        if (schemaObject["allOf"] is JsonArray allOf &&
            allOf.Any(branch => !SchemaCouldMatch(instance, branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal))))
        {
            return false;
        }

        if (schemaObject["anyOf"] is JsonArray anyOf &&
            !anyOf.Any(branch => SchemaCouldMatch(instance, branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal))))
        {
            return false;
        }

        if (schemaObject["oneOf"] is JsonArray oneOf &&
            oneOf.Count(branch => SchemaCouldMatch(instance, branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal))) != 1)
        {
            return false;
        }

        return true;
    }

    private static bool ValueMatchesType(JsonNode? value, string? type) => type switch
    {
        "null" => value is null,
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "string" => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out _),
        "boolean" => value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out _),
        "integer" => value is JsonValue jsonValue && jsonValue.TryGetValue<JsonElement>(out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _),
        "number" => value is JsonValue jsonValue && jsonValue.TryGetValue<JsonElement>(out JsonElement element) && element.ValueKind == JsonValueKind.Number,
        _ => true,
    };

    private static bool JsonNodesEqual(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return JsonSerializer.SerializeToElement(left, McpJsonUtilities.JsonContext.Default.JsonNode).GetRawText() ==
            JsonSerializer.SerializeToElement(right, McpJsonUtilities.JsonContext.Default.JsonNode).GetRawText();
    }

    private static bool SchemaAllowsNull(JsonNode? schemaNode, JsonNode schemaRoot, HashSet<string> activeReferences)
    {
        if (schemaNode is null)
        {
            return true;
        }

        if (schemaNode is JsonValue schemaValue && schemaValue.TryGetValue<bool>(out bool allowsNull))
        {
            return allowsNull;
        }

        if (schemaNode is not JsonObject schemaObject)
        {
            return false;
        }

        if (schemaObject["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out string? refText) && refText is not null)
        {
            if (!activeReferences.Add(refText))
            {
                return true;
            }

            bool referenceAllowsNull = TryResolveLocalReference(schemaRoot, refText, out JsonNode? referencedSchema) &&
                SchemaAllowsNull(referencedSchema, schemaRoot, activeReferences);
            activeReferences.Remove(refText);
            if (!referenceAllowsNull)
            {
                return false;
            }
        }

        if (schemaObject["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out string? type) && type != "null")
        {
            return false;
        }

        if (schemaObject["type"] is JsonArray types &&
            !types.Any(typeNode => typeNode is JsonValue value && value.TryGetValue<string>(out string? name) && name == "null"))
        {
            return false;
        }

        if (schemaObject.TryGetPropertyValue("const", out JsonNode? constValue) && constValue is not null)
        {
            return false;
        }

        if (schemaObject["enum"] is JsonArray enumValues && !enumValues.Any(static value => value is null))
        {
            return false;
        }

        if (schemaObject["allOf"] is JsonArray allOf &&
            allOf.Any(branch => !SchemaAllowsNull(branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal))))
        {
            return false;
        }

        if (schemaObject["anyOf"] is JsonArray anyOf &&
            !anyOf.Any(branch => SchemaAllowsNull(branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal))))
        {
            return false;
        }

        if (schemaObject["oneOf"] is JsonArray oneOf &&
            oneOf.Count(branch => SchemaAllowsNull(branch, schemaRoot, new HashSet<string>(StringComparer.Ordinal))) != 1)
        {
            return false;
        }

        if (schemaObject["not"] is JsonNode notSchema && SchemaAllowsNull(notSchema, schemaRoot, new HashSet<string>(StringComparer.Ordinal)))
        {
            return false;
        }

        if (schemaObject["if"] is JsonNode ifSchema)
        {
            bool ifMatchesNull = SchemaCouldMatch(null, ifSchema, schemaRoot, new HashSet<string>(StringComparer.Ordinal));
            JsonNode? conditionalBranch = ifMatchesNull ? schemaObject["then"] : schemaObject["else"];
            if (conditionalBranch is not null && !SchemaAllowsNull(conditionalBranch, schemaRoot, new HashSet<string>(StringComparer.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveLocalReference(JsonNode schemaRoot, string reference, out JsonNode? resolvedSchema)
    {
        resolvedSchema = null;
        if (!reference.StartsWith('#'))
        {
            return false;
        }

        string pointer = Uri.UnescapeDataString(reference[1..]);
        if (pointer.Length == 0)
        {
            resolvedSchema = schemaRoot;
            return true;
        }

        if (!pointer.StartsWith('/'))
        {
            return false;
        }

        JsonNode? current = schemaRoot;
        foreach (string rawSegment in pointer[1..].Split('/'))
        {
            string segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current is JsonObject currentObject && currentObject.TryGetPropertyValue(segment, out JsonNode? child))
            {
                current = child;
            }
            else if (current is JsonArray currentArray && int.TryParse(segment, out int index) && index >= 0 && index < currentArray.Count)
            {
                current = currentArray[index];
            }
            else
            {
                return false;
            }
        }

        resolvedSchema = current;
        return true;
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

    /// <summary>
    /// Post-processes the input schema to add <c>x-mcp-header</c> extensions based on
    /// <see cref="McpHeaderAttribute"/> on method parameters.
    /// </summary>
    private static JsonElement AddMcpHeaderExtensions(JsonElement inputSchema, MethodInfo method)
    {
        // Collect parameters with McpHeaderAttribute
        var headerParams = new List<(string ParameterName, string HeaderName, ParameterInfo Parameter)>();
        var headerNamesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var param in method.GetParameters())
        {
            var attr = param.GetCustomAttribute<McpHeaderAttribute>();
            if (attr is null)
            {
                continue;
            }

            // Validate primitive type only
            var paramType = Nullable.GetUnderlyingType(param.ParameterType) ?? param.ParameterType;
            if (!IsPrimitiveHeaderType(paramType))
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' on method '{method.Name}' has [McpHeader] but is not a supported type. " +
                    "Only string, integer, and boolean types may be annotated with [McpHeader].");
            }

            // Validate case-insensitive uniqueness
            if (!headerNamesSet.Add(attr.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate x-mcp-header name '{attr.Name}' (case-insensitive) found on method '{method.Name}'. " +
                    "Header names must be case-insensitively unique within a tool's input schema.");
            }

            headerParams.Add((param.Name!, attr.Name, param));
        }

        if (headerParams.Count == 0)
        {
            return inputSchema;
        }

        // Parse the schema to a mutable JsonNode, add extensions, and convert back
        var schemaNode = JsonNode.Parse(inputSchema.GetRawText());
        if (schemaNode is not JsonObject schemaObj ||
            !schemaObj.TryGetPropertyValue("properties", out var propertiesNode) ||
            propertiesNode is not JsonObject propertiesObj)
        {
            return inputSchema;
        }

        foreach (var (parameterName, headerName, _) in headerParams)
        {
            if (propertiesObj.TryGetPropertyValue(parameterName, out var propNode) &&
                propNode is JsonObject propObj)
            {
                propObj["x-mcp-header"] = headerName;
            }
        }

        return JsonSerializer.Deserialize(schemaNode, McpJsonUtilities.JsonContext.Default.JsonElement);
    }

    private static bool IsPrimitiveHeaderType(Type type)
    {
        // Per SEP-2243, x-mcp-header may only be applied to integer, string, or boolean parameters,
        // and integer values must stay within the JavaScript safe integer range (-2^53+1 to 2^53-1).
        // ulong is excluded because its upper range (above long.MaxValue) cannot be represented as a
        // signed integer and the bulk of its domain falls outside the safe range. Remaining integer
        // types are allowed here; long values are additionally range-checked per value when emitted
        // (client) and validated (server).
        return type == typeof(string) ||
               type == typeof(bool) ||
               type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long);
    }
}
