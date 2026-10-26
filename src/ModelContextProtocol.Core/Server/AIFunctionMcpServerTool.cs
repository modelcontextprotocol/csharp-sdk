using Microsoft.Extensions.AI;
using Json.Schema;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
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
    private readonly Lazy<JsonSchema?> _outputSchema;
    private readonly Lazy<JsonTypeInfo> _serializerTypeInfoIncludingDefaults;

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
        _outputSchema = new Lazy<JsonSchema?>(BuildOutputSchema, LazyThreadSafetyMode.ExecutionAndPublication);
        _serializerTypeInfoIncludingDefaults = new Lazy<JsonTypeInfo>(() =>
        {
            JsonSerializerOptions options = new(AIFunction.JsonSerializerOptions)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            };
            return options.GetTypeInfo(typeof(object));
        }, LazyThreadSafetyMode.ExecutionAndPublication);

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
        switch (aiFunctionResult)
        {
            case JsonElement jsonElement:
                elementResult = jsonElement;
                break;
            case JsonNode node:
                elementResult = JsonSerializer.SerializeToElement(node, McpJsonUtilities.JsonContext.Default.JsonNode);
                break;
            case null:
                elementResult = null;
                break;
            default:
                JsonSerializerOptions serializerOptions = AIFunction.JsonSerializerOptions;
                JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(typeof(object));
                elementResult = JsonSerializer.SerializeToElement(aiFunctionResult, typeInfo);
                break;
        }

        elementResult = AddMissingRequiredNullableProperties(elementResult, aiFunctionResult);

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

    private JsonElement? AddMissingRequiredNullableProperties(
        JsonElement? elementResult,
        object? originalResult)
    {
        if (elementResult is not { } result ||
            ProtocolTool.OutputSchema is not { } outputSchema ||
            result.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return elementResult;
        }

        JsonNode? resultNode = JsonSerializer.SerializeToNode(result, McpJsonUtilities.JsonContext.Default.JsonElement);
        JsonNode? schemaNode = JsonSerializer.SerializeToNode(outputSchema, McpJsonUtilities.JsonContext.Default.JsonElement);
        JsonSchema? schema = _outputSchema.Value;
        if (resultNode is null || schemaNode is null || schema is null)
        {
            return elementResult;
        }

        if (IsSchemaValid(schema, resultNode))
        {
            return elementResult;
        }

        JsonNode? resultNodeIncludingDefaults = resultNode;
        if (originalResult is not null and not JsonElement and not JsonNode &&
            AIFunction.JsonSerializerOptions.DefaultIgnoreCondition is not JsonIgnoreCondition.Never)
        {
            try
            {
                JsonElement completeResult = JsonSerializer.SerializeToElement(
                    originalResult, _serializerTypeInfoIncludingDefaults.Value);
                resultNodeIncludingDefaults = JsonSerializer.SerializeToNode(
                    completeResult, McpJsonUtilities.JsonContext.Default.JsonElement);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                // A value omitted from the actual result may fail when the ignore policy is
                // relaxed. Required members can still be recovered individually from metadata.
                resultNodeIncludingDefaults = null;
            }
        }

        JsonNode? repairedResult = RepairStructuredResult(
            resultNode,
            resultNodeIncludingDefaults,
            schemaNode,
            schema,
            originalResult,
            AIFunction.JsonSerializerOptions);

        return repairedResult is null
            ? elementResult
            : JsonSerializer.SerializeToElement(repairedResult, McpJsonUtilities.JsonContext.Default.JsonNode);
    }

    /// <summary>
    /// Restores omitted required properties only when the complete result validates against the
    /// published schema.  The serializer can omit a value because of either the global ignore
    /// policy or a member-level <see cref="JsonIgnoreAttribute"/>.  Candidate values are therefore
    /// read from the source object when the second serialization snapshot does not contain them.
    /// </summary>
    private static JsonNode? RepairStructuredResult(
        JsonNode resultNode,
        JsonNode? resultNodeIncludingDefaults,
        JsonNode schemaNode,
        JsonSchema schema,
        object? originalResult,
        JsonSerializerOptions serializerOptions)
    {
        // An explicit schema may deliberately omit optional nulls.  Preserve that valid output
        // before looking for missing required members.
        if (IsSchemaValid(schema, resultNode))
        {
            return null;
        }

        return TryRepairStructuredResult(
            resultNode,
            resultNodeIncludingDefaults,
            schemaNode,
            schema,
            originalResult,
            serializerOptions,
            depth: 0,
            new RepairSearchState());
    }

    private static JsonNode? TryRepairStructuredResult(
        JsonNode resultNode,
        JsonNode? resultNodeIncludingDefaults,
        JsonNode schemaNode,
        JsonSchema schema,
        object? originalResult,
        JsonSerializerOptions serializerOptions,
        int depth,
        RepairSearchState searchState)
    {
        if (IsSchemaValid(schema, resultNode))
        {
            return resultNode.DeepClone();
        }

        const int maxRepairDepth = 32;
        const int maxCandidateSetEvaluations = 256;
        if (depth >= maxRepairDepth || searchState.CandidateSetEvaluations >= maxCandidateSetEvaluations ||
            !searchState.VisitedStates.Add(resultNode.ToJsonString()))
        {
            return null;
        }

        Dictionary<string, SchemaObjectContext> contexts = new(StringComparer.Ordinal);
        CollectSchemaContexts(
            resultNode,
            schemaNode,
            schemaNode,
            string.Empty,
            contexts,
            new HashSet<string>(StringComparer.Ordinal));

        List<RepairCandidate> candidates = CreateRepairCandidates(
            resultNode,
            resultNodeIncludingDefaults,
            originalResult,
            serializerOptions,
            contexts);
        if (candidates.Count == 0)
        {
            return null;
        }

        bool[] included = Enumerable.Repeat(true, candidates.Count).ToArray();
        string originalJson = resultNode.ToJsonString();
        JsonNode? repairedResult = null;

        bool TryCurrentSet()
        {
            if (searchState.CandidateSetEvaluations >= maxCandidateSetEvaluations)
            {
                return false;
            }

            searchState.CandidateSetEvaluations++;
            JsonNode candidateResult = resultNode.DeepClone();
            bool hasIncludedCandidate = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (included[i])
                {
                    ApplyRepairCandidate(candidateResult, candidates[i]);
                    hasIncludedCandidate = true;
                }
            }

            if (!hasIncludedCandidate || string.Equals(candidateResult.ToJsonString(), originalJson, StringComparison.Ordinal))
            {
                return false;
            }

            repairedResult = TryRepairStructuredResult(
                candidateResult,
                resultNodeIncludingDefaults,
                schemaNode,
                schema,
                originalResult,
                serializerOptions,
                depth + 1,
                searchState);
            return repairedResult is not null;
        }

        bool SearchInclusions(int nextIndex, int remaining)
        {
            if (remaining == 0)
            {
                return TryCurrentSet();
            }

            for (int i = nextIndex; i <= candidates.Count - remaining; i++)
            {
                included[i] = true;
                if (SearchInclusions(i + 1, remaining - 1))
                {
                    return true;
                }

                included[i] = false;
                if (searchState.CandidateSetEvaluations >= maxCandidateSetEvaluations)
                {
                    return false;
                }
            }

            return false;
        }

        if (TryCurrentSet())
        {
            return repairedResult;
        }

        for (int i = 0; i < included.Length; i++)
        {
            included[i] = false;
        }

        for (int includedCount = 1; includedCount < candidates.Count; includedCount++)
        {
            if (SearchInclusions(0, includedCount))
            {
                return repairedResult;
            }

            if (searchState.CandidateSetEvaluations >= maxCandidateSetEvaluations)
            {
                break;
            }
        }

        return null;
    }

    private static bool TryBuildJsonSchema(JsonNode schemaNode, out JsonSchema? schema)
    {
        try
        {
            JsonElement schemaElement = JsonSerializer.SerializeToElement(schemaNode, McpJsonUtilities.JsonContext.Default.JsonNode);
            schema = JsonSchema.Build(schemaElement);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            schema = null;
            return false;
        }
    }

    private JsonSchema? BuildOutputSchema()
    {
        if (ProtocolTool.OutputSchema is not { } outputSchema)
        {
            return null;
        }

        JsonNode? schemaNode = JsonSerializer.SerializeToNode(outputSchema, McpJsonUtilities.JsonContext.Default.JsonElement);
        return schemaNode is not null && TryBuildJsonSchema(schemaNode, out JsonSchema? schema)
            ? schema
            : null;
    }

    private static bool IsSchemaValid(JsonSchema schema, JsonNode instance)
    {
        try
        {
            JsonElement instanceElement = JsonSerializer.SerializeToElement(instance, McpJsonUtilities.JsonContext.Default.JsonNode);
            return schema.Evaluate(instanceElement).IsValid;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static List<RepairCandidate> CreateRepairCandidates(
        JsonNode resultNode,
        JsonNode? resultNodeIncludingDefaults,
        object? originalResult,
        JsonSerializerOptions serializerOptions,
        Dictionary<string, SchemaObjectContext> contexts)
    {
        List<RepairCandidate> candidates = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, SchemaObjectContext> contextEntry in contexts)
        {
            if (!TryGetNodeAtPath(resultNode, contextEntry.Key, out JsonNode? parentNode) || parentNode is not JsonObject parentObject)
            {
                continue;
            }

            foreach (string propertyName in contextEntry.Value.Required)
            {
                if (parentObject.ContainsKey(propertyName))
                {
                    continue;
                }

                string candidateKey = contextEntry.Key + "\u0000" + propertyName;
                if (!seen.Add(candidateKey))
                {
                    continue;
                }

                bool hasCompleteValue = TryGetNodeAtPath(
                    resultNodeIncludingDefaults,
                    AppendJsonPointer(contextEntry.Key, propertyName),
                    out JsonNode? completeValue);

                if (hasCompleteValue)
                {
                    candidates.Add(new RepairCandidate(contextEntry.Key, propertyName, completeValue?.DeepClone()));
                }
                else if (TryGetSourceValue(
                    originalResult,
                    contextEntry.Key,
                    propertyName,
                    serializerOptions,
                    out JsonNode? sourceValue))
                {
                    candidates.Add(new RepairCandidate(contextEntry.Key, propertyName, sourceValue));
                }
            }
        }

        return candidates;
    }

    private static void ApplyRepairCandidate(JsonNode resultNode, RepairCandidate candidate)
    {
        if (TryGetNodeAtPath(resultNode, candidate.ParentPath, out JsonNode? parentNode) && parentNode is JsonObject parentObject)
        {
            parentObject[candidate.PropertyName] = candidate.Value?.DeepClone();
        }
    }

    private sealed class SchemaObjectContext
    {
        internal HashSet<string> Required { get; } = new(StringComparer.Ordinal);
    }

    private sealed record RepairCandidate(string ParentPath, string PropertyName, JsonNode? Value);

    private sealed class RepairSearchState
    {
        internal int CandidateSetEvaluations { get; set; }

        internal HashSet<string> VisitedStates { get; } = new(StringComparer.Ordinal);
    }

    private static void CollectSchemaContexts(
        JsonNode? instanceNode,
        JsonNode? schemaNode,
        JsonNode schemaRoot,
        string instancePath,
        Dictionary<string, SchemaObjectContext> contexts,
        HashSet<string> activeReferences)
    {
        if (schemaNode is JsonValue schemaValue && schemaValue.TryGetValue<bool>(out _))
        {
            return;
        }

        if (schemaNode is not JsonObject schemaObject)
        {
            return;
        }

        if (schemaObject["$ref"] is JsonValue refValue &&
            refValue.TryGetValue<string>(out string? refText) &&
            refText is not null &&
            activeReferences.Add(refText + "|" + instancePath))
        {
            if (TryResolveLocalReference(schemaRoot, refText, out JsonNode? referencedSchema))
            {
                CollectSchemaContexts(instanceNode, referencedSchema, schemaRoot, instancePath, contexts, activeReferences);
            }

            activeReferences.Remove(refText + "|" + instancePath);
        }

        SchemaObjectContext context = GetOrCreateSchemaContext(contexts, instancePath);
        if (schemaObject["required"] is JsonArray required)
        {
            foreach (JsonNode? requiredProperty in required)
            {
                if (requiredProperty?.GetValue<string>() is string propertyName)
                {
                    context.Required.Add(propertyName);
                }
            }
        }

        if (schemaObject["properties"] is JsonObject properties)
        {
            foreach (KeyValuePair<string, JsonNode?> property in properties)
            {
                if (property.Value is null)
                {
                    continue;
                }

                if (instanceNode is not JsonObject instanceObject ||
                    !instanceObject.TryGetPropertyValue(property.Key, out JsonNode? propertyInstance) ||
                    propertyInstance is null)
                {
                    continue;
                }

                CollectSchemaContexts(
                    propertyInstance,
                    property.Value,
                    schemaRoot,
                    AppendJsonPointer(instancePath, property.Key),
                    contexts,
                    new HashSet<string>(activeReferences, StringComparer.Ordinal));
            }
        }

        if (instanceNode is JsonObject objectInstance)
        {
            JsonObject? patternProperties = schemaObject["patternProperties"] as JsonObject;
            JsonNode? additionalProperties = schemaObject["additionalProperties"];
            foreach (KeyValuePair<string, JsonNode?> property in objectInstance)
            {
                bool isExplicitProperty = schemaObject["properties"] is JsonObject explicitProperties &&
                    explicitProperties.ContainsKey(property.Key);

                bool matchedPattern = false;
                if (patternProperties is not null)
                {
                    foreach (KeyValuePair<string, JsonNode?> patternProperty in patternProperties)
                    {
                        if (patternProperty.Value is not null && Regex.IsMatch(property.Key, patternProperty.Key))
                        {
                            matchedPattern = true;
                            CollectSchemaContexts(
                                property.Value,
                                patternProperty.Value,
                                schemaRoot,
                                AppendJsonPointer(instancePath, property.Key),
                                contexts,
                                new HashSet<string>(activeReferences, StringComparer.Ordinal));
                        }
                    }
                }

                if (!isExplicitProperty && !matchedPattern && additionalProperties is not null)
                {
                    CollectSchemaContexts(
                        property.Value,
                        additionalProperties,
                        schemaRoot,
                        AppendJsonPointer(instancePath, property.Key),
                        contexts,
                        new HashSet<string>(activeReferences, StringComparer.Ordinal));
                }
            }
        }

        if (instanceNode is JsonArray arrayInstance)
        {
            JsonArray? prefixItems = schemaObject["prefixItems"] as JsonArray;
            JsonNode? items = schemaObject["items"];
            for (int i = 0; i < arrayInstance.Count; i++)
            {
                JsonNode? itemSchema = prefixItems is not null && i < prefixItems.Count ? prefixItems[i] : items;
                if (itemSchema is not null)
                {
                    CollectSchemaContexts(
                        arrayInstance[i],
                        itemSchema,
                        schemaRoot,
                        AppendJsonPointer(instancePath, i.ToString(CultureInfo.InvariantCulture)),
                        contexts,
                        new HashSet<string>(activeReferences, StringComparer.Ordinal));
                }
            }
        }

        if (instanceNode is JsonObject dependencyInstance &&
            schemaObject["dependentRequired"] is JsonObject dependentRequired)
        {
            foreach (KeyValuePair<string, JsonNode?> dependency in dependentRequired)
            {
                if (dependencyInstance.ContainsKey(dependency.Key) && dependency.Value is JsonArray requiredProperties)
                {
                    foreach (JsonNode? requiredProperty in requiredProperties)
                    {
                        if (requiredProperty?.GetValue<string>() is string propertyName)
                        {
                            context.Required.Add(propertyName);
                        }
                    }
                }
            }
        }

        if (instanceNode is JsonObject dependencyInstanceForSchemas &&
            schemaObject["dependentSchemas"] is JsonObject dependentSchemas)
        {
            foreach (KeyValuePair<string, JsonNode?> dependentSchema in dependentSchemas)
            {
                if (dependentSchema.Value is not null && dependencyInstanceForSchemas.ContainsKey(dependentSchema.Key))
                {
                    CollectSchemaContexts(
                        instanceNode,
                        dependentSchema.Value,
                        schemaRoot,
                        instancePath,
                        contexts,
                        new HashSet<string>(activeReferences, StringComparer.Ordinal));
                }
            }
        }

        foreach (string compositionKeyword in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (schemaObject[compositionKeyword] is not JsonArray composition)
            {
                continue;
            }

            foreach (JsonNode? branch in composition)
            {
                if (branch is not null)
                {
                    CollectSchemaContexts(
                        instanceNode,
                        branch,
                        schemaRoot,
                        instancePath,
                        contexts,
                        new HashSet<string>(activeReferences, StringComparer.Ordinal));
                }
            }
        }

        if (schemaObject["if"] is JsonNode ifSchema)
        {
            if (schemaObject["then"] is JsonNode thenSchema)
            {
                CollectSchemaContexts(instanceNode, thenSchema, schemaRoot, instancePath, contexts, new HashSet<string>(activeReferences, StringComparer.Ordinal));
            }

            if (schemaObject["else"] is JsonNode elseSchema)
            {
                CollectSchemaContexts(instanceNode, elseSchema, schemaRoot, instancePath, contexts, new HashSet<string>(activeReferences, StringComparer.Ordinal));
            }
        }
    }

    private static SchemaObjectContext GetOrCreateSchemaContext(Dictionary<string, SchemaObjectContext> contexts, string instancePath)
    {
        if (!contexts.TryGetValue(instancePath, out SchemaObjectContext? context))
        {
            contexts[instancePath] = context = new SchemaObjectContext();
        }

        return context;
    }

    private static string AppendJsonPointer(string parentPath, string segment)
    {
        string escapedSegment = segment.Replace("~", "~0").Replace("/", "~1");
        return parentPath.Length == 0 ? "/" + escapedSegment : parentPath + "/" + escapedSegment;
    }

    private static bool TryGetNodeAtPath(JsonNode? root, string path, out JsonNode? value)
    {
        value = root;
        if (path.Length == 0)
        {
            return root is not null;
        }

        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        foreach (string rawSegment in path.Substring(1).Split('/'))
        {
            string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            if (value is JsonObject objectValue && objectValue.TryGetPropertyValue(segment, out JsonNode? objectChild))
            {
                value = objectChild;
            }
            else if (value is JsonArray arrayValue && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                index >= 0 && index < arrayValue.Count)
            {
                value = arrayValue[index];
            }
            else
            {
                value = null;
                return false;
            }
        }

        return true;
    }

    private static bool TryGetSourceValue(
        object? sourceRoot,
        string parentPath,
        string propertyName,
        JsonSerializerOptions serializerOptions,
        out JsonNode? value)
    {
        value = null;
        if (sourceRoot is null || !TryGetSourceObjectAtPath(sourceRoot, parentPath, serializerOptions, out object? sourceParent) || sourceParent is null)
        {
            return false;
        }

        if (!TryGetSourceMember(
                sourceParent,
                propertyName,
                serializerOptions,
                out object? sourceValue,
                out Type? declaredType,
                out JsonPropertyInfo? propertyInfo))
        {
            return false;
        }

        if (!WasOmittedBySupportedIgnoreCondition(
                sourceParent, sourceValue, propertyInfo, serializerOptions))
        {
            return false;
        }

        value = SerializeSourceValue(sourceValue, declaredType, serializerOptions, propertyInfo);
        return sourceValue is null || value is not null;
    }

    private static bool WasOmittedBySupportedIgnoreCondition(
        object sourceParent,
        object? value,
        JsonPropertyInfo? propertyInfo,
        JsonSerializerOptions serializerOptions)
    {
        JsonIgnoreAttribute? ignoreAttribute = propertyInfo?.AttributeProvider is ICustomAttributeProvider attributeProvider
            ? attributeProvider.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
                .OfType<JsonIgnoreAttribute>()
                .FirstOrDefault()
            : null;

        if (propertyInfo?.ShouldSerialize is { } shouldSerialize &&
            !IsJsonIgnoreConditionPredicate(shouldSerialize) &&
            !shouldSerialize(sourceParent, value))
        {
            return false;
        }

        JsonIgnoreCondition ignoreCondition = ignoreAttribute?.Condition ?? serializerOptions.DefaultIgnoreCondition;
        return ignoreCondition switch
        {
            JsonIgnoreCondition.WhenWritingNull => value is null,
            JsonIgnoreCondition.WhenWritingDefault =>
                propertyInfo is not null &&
                IsDefaultValue(value, propertyInfo.PropertyType),
            _ => false,
        };
    }

    // System.Text.Json uses this generated delegate for JsonIgnore conditions; evaluate those from metadata instead.
    private static bool IsJsonIgnoreConditionPredicate(Delegate predicate)
    {
        Type? generatedClosureType = predicate.Method.DeclaringType;
        Type? propertyInfoType = generatedClosureType?.DeclaringType;

        return predicate.Method.Name.StartsWith("<SetShouldSerialize>b__", StringComparison.Ordinal) &&
            propertyInfoType?.Assembly == typeof(JsonPropertyInfo).Assembly &&
            string.Equals(propertyInfoType.Name, "JsonPropertyInfo`1", StringComparison.Ordinal);
    }

    private static bool IsDefaultValue(object? value, Type valueType)
    {
        if (value is null)
        {
            return true;
        }

        return Nullable.GetUnderlyingType(valueType) is null &&
            valueType.IsValueType &&
            value.Equals(CreateDefaultValue(valueType));
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2067:Type argument does not satisfy DynamicallyAccessedMemberTypes requirements",
        Justification = "This helper is called only for value types; zero-initializing them does not invoke constructors.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2072:Type argument does not satisfy DynamicallyAccessedMemberTypes requirements",
        Justification = "This helper is called only for value types; zero-initializing them does not invoke constructors.")]
    private static object CreateDefaultValue(Type valueType)
    {
#if NETSTANDARD2_0
        return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(valueType);
#else
        return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(valueType);
#endif
    }

    private static bool TryGetSourceObjectAtPath(
        object sourceRoot,
        string path,
        JsonSerializerOptions serializerOptions,
        out object? value)
    {
        value = sourceRoot;
        if (path.Length == 0)
        {
            return true;
        }

        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        foreach (string rawSegment in path.Substring(1).Split('/'))
        {
            string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            if (value is null)
            {
                return false;
            }

            if (value is JsonNode jsonNode)
            {
                if (jsonNode is JsonObject jsonObject && jsonObject.TryGetPropertyValue(segment, out JsonNode? childNode))
                {
                    value = childNode;
                    continue;
                }

                if (jsonNode is JsonArray jsonArray && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int jsonIndex) &&
                    jsonIndex >= 0 && jsonIndex < jsonArray.Count)
                {
                    value = jsonArray[jsonIndex];
                    continue;
                }

                value = null;
                return false;
            }

            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty(segment, out JsonElement childElement))
                {
                    value = childElement;
                    continue;
                }

                if (jsonElement.ValueKind == JsonValueKind.Array && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int elementIndex) &&
                    elementIndex >= 0 && elementIndex < jsonElement.GetArrayLength())
                {
                    value = jsonElement[elementIndex];
                    continue;
                }

                value = null;
                return false;
            }

            if (value is IDictionary dictionary && TryGetDictionaryValue(dictionary, segment, serializerOptions, out object? dictionaryValue))
            {
                value = dictionaryValue;
                continue;
            }

            if (int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && value is IEnumerable enumerable)
            {
                int currentIndex = 0;
                bool found = false;
                foreach (object? item in enumerable)
                {
                    if (currentIndex++ == index)
                    {
                        value = item;
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    continue;
                }
            }

            if (!TryGetSourceMember(value, segment, serializerOptions, out object? memberValue, out _, out _))
            {
                value = null;
                return false;
            }

            value = memberValue;
        }

        return true;
    }

    private static bool TryGetDictionaryValue(
        IDictionary dictionary,
        string propertyName,
        JsonSerializerOptions serializerOptions,
        out object? value)
    {
        value = null;
        JsonTypeInfo dictionaryTypeInfo;
        try
        {
            dictionaryTypeInfo = serializerOptions.GetTypeInfo(dictionary.GetType());
        }
        catch (NotSupportedException)
        {
            return false;
        }

        if (dictionaryTypeInfo.Kind != JsonTypeInfoKind.Dictionary ||
            dictionaryTypeInfo.KeyType is null ||
            dictionaryTypeInfo.ElementType is not { } valueType ||
            dictionaryTypeInfo.CreateObject?.Invoke() is not IDictionary singleEntry)
        {
            return false;
        }

        object? placeholderValue = valueType.IsValueType
            ? CreateDefaultValue(valueType)
            : null;

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is null)
            {
                continue;
            }

            try
            {
                singleEntry.Clear();
                singleEntry.Add(entry.Key, placeholderValue);
                JsonElement serialized = JsonSerializer.SerializeToElement(singleEntry, dictionaryTypeInfo);
                if (serialized.ValueKind == JsonValueKind.Object && serialized.TryGetProperty(propertyName, out _))
                {
                    value = entry.Value;
                    return true;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryGetSourceMember(
        object source,
        string propertyName,
        JsonSerializerOptions serializerOptions,
        out object? value,
        out Type? declaredType,
        out JsonPropertyInfo? propertyInfo)
    {
        value = null;
        declaredType = null;
        propertyInfo = null;

        if (source is JsonNode jsonNode)
        {
            if (jsonNode is JsonObject jsonObject && jsonObject.TryGetPropertyValue(propertyName, out JsonNode? childNode))
            {
                value = childNode;
                declaredType = typeof(JsonNode);
                return true;
            }

            return false;
        }

        if (source is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty(propertyName, out JsonElement childElement))
        {
            value = childElement;
            declaredType = typeof(JsonElement);
            return true;
        }

        if (source is IDictionary dictionary && dictionary.Contains(propertyName))
        {
            value = dictionary[propertyName];
            declaredType = value?.GetType();
            return true;
        }

        try
        {
            JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(source.GetType());
            foreach (JsonPropertyInfo property in typeInfo.Properties)
            {
                if (string.Equals(property.Name, propertyName, StringComparison.Ordinal) && property.Get is not null)
                {
                    value = property.Get(source);
                    declaredType = property.PropertyType;
                    propertyInfo = property;
                    return true;
                }
            }
        }
        catch (NotSupportedException)
        {
            // Without serializer metadata, keep the serialized snapshot as the source of truth.
        }

        return false;
    }

    private static JsonNode? SerializeSourceValue(
        object? value,
        Type? declaredType,
        JsonSerializerOptions serializerOptions,
        JsonPropertyInfo? propertyInfo)
    {
        if (value is JsonNode jsonNode)
        {
            return jsonNode.DeepClone();
        }

        if (value is JsonElement jsonElement)
        {
            return JsonSerializer.SerializeToNode(jsonElement, McpJsonUtilities.JsonContext.Default.JsonElement);
        }

        Type valueType = declaredType ?? value?.GetType() ?? typeof(object);
        try
        {
            JsonSerializerOptions effectiveOptions = serializerOptions;
            JsonConverter? propertyConverter = GetPropertyConverter(propertyInfo, valueType);
            if (propertyConverter is not null || propertyInfo?.NumberHandling is not null)
            {
                effectiveOptions = new JsonSerializerOptions(serializerOptions);
                if (propertyConverter is not null)
                {
                    effectiveOptions.Converters.Insert(0, propertyConverter);
                }

                if (propertyInfo?.NumberHandling is { } numberHandling)
                {
                    effectiveOptions.NumberHandling = numberHandling;
                }
            }

            JsonTypeInfo typeInfo = effectiveOptions.GetTypeInfo(valueType);
            JsonElement element = JsonSerializer.SerializeToElement(value, typeInfo);
            return JsonSerializer.SerializeToNode(element, McpJsonUtilities.JsonContext.Default.JsonElement);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static JsonConverter? GetPropertyConverter(JsonPropertyInfo? propertyInfo, Type propertyType)
    {
        if (propertyInfo is null)
        {
            return null;
        }

        PropertyInfo? customConverterProperty = typeof(JsonPropertyInfo).GetProperty("CustomConverter", BindingFlags.Instance | BindingFlags.Public);
        if (customConverterProperty?.GetValue(propertyInfo) is JsonConverter customConverter)
        {
            return customConverter;
        }

        if (propertyInfo.AttributeProvider is ICustomAttributeProvider attributeProvider &&
            attributeProvider.GetCustomAttributes(typeof(JsonConverterAttribute), inherit: true)
                .OfType<JsonConverterAttribute>()
                .FirstOrDefault() is { } converterAttribute)
        {
            return converterAttribute.CreateConverter(propertyType);
        }

        return null;
    }

    private static bool TryResolveLocalReference(JsonNode schemaRoot, string reference, out JsonNode? resolvedSchema)
    {
        resolvedSchema = null;
        if (!reference.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        string pointer = Uri.UnescapeDataString(reference[1..]);
        if (pointer.Length == 0)
        {
            resolvedSchema = schemaRoot;
            return true;
        }

        if (!pointer.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        JsonNode? current = schemaRoot;
        foreach (string rawSegment in pointer[1..].Split('/'))
        {
            string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
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
