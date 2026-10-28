using Microsoft.Extensions.AI;
using Json.Schema;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    private OutputSchemaCache? _outputSchema;
    private static readonly ConditionalWeakTable<JsonTypeInfo, SourceObjectContract> s_sourceContracts = new();
    [ThreadStatic]
    private static SourceRecoveryContext? t_serializingSourceMember;

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
        OutputSchemaCache cachedSchema = GetOutputSchema(outputSchema);
        JsonNode? schemaNode = cachedSchema.Node;
        JsonSchema? schema = cachedSchema.Schema;
        if (resultNode is null || schemaNode is null || schema is null)
        {
            return elementResult;
        }

        if (IsSchemaValid(schema, resultNode) || HasDuplicatePropertyNames(result))
        {
            return elementResult;
        }

        JsonNode? repairedResult = RepairStructuredResult(
            resultNode,
            schemaNode,
            schema,
            originalResult is null ? null : new SourceRecoveryContext(
                originalResult, AIFunction.JsonSerializerOptions.GetTypeInfo(typeof(object))),
            AIFunction.JsonSerializerOptions);

        return repairedResult is null
            ? elementResult
            : JsonSerializer.SerializeToElement(repairedResult, McpJsonUtilities.JsonContext.Default.JsonNode);
    }

    /// <summary>
    /// Restores omitted required properties only when the complete result validates against the
    /// published schema.  The serializer can omit a value because of either the global ignore
    /// policy or a member-level <see cref="JsonIgnoreAttribute"/>. Candidate values are read only
    /// from members of the active serialization contract, without serializing unrelated members.
    /// </summary>
    private static JsonNode? RepairStructuredResult(
        JsonNode resultNode,
        JsonNode schemaNode,
        JsonSchema schema,
        SourceRecoveryContext? originalResult,
        JsonSerializerOptions serializerOptions,
        int remainingDepth = 32,
        JsonNode? schemaDocument = null,
        SchemaReferenceResolver? references = null,
        List<JsonNode>? dynamicScope = null,
        RepairTraversalBudget? traversalBudget = null,
        List<RepairCondition>? inheritedConditions = null)
    {
        // An explicit schema may deliberately omit optional nulls.  Preserve that valid output
        // before looking for missing required members.
        if (remainingDepth <= 0)
        {
            return null;
        }

        const int maxCandidateSetEvaluations = 256;
        Queue<RepairAttempt> pending = new();
        Queue<RepairAttempt> fallback = new();
        HashSet<string> visited = new(StringComparer.Ordinal) { resultNode.ToJsonString() };
        schemaDocument ??= schemaNode;
        references ??= new(schemaDocument);
        traversalBudget ??= new();
        // Cache predicates independently of their callers. Conditional expectations and
        // instance paths remain on each group and are checked on every tentative repair.
        Dictionary<(JsonNode Schema, string Scope), JsonSchema?> branchSchemas = new();

        JsonSchema? GetBranchSchema(JsonNode branch, List<JsonNode> scope)
        {
            var schemaKey = (branch, references.GetScopeKey(scope));
            if (!branchSchemas.TryGetValue(schemaKey, out JsonSchema? branchSchema))
            {
                JsonNode document = schemaDocument.DeepClone();
                references.BindDynamicReferences(schemaDocument, document, scope);
                if (document is JsonObject documentObject)
                {
                    documentObject["$id"] = references.GetSchemaUri(schemaDocument).AbsoluteUri;
                }

                JsonObject branchDocument = new()
                {
                    ["$ref"] = references.GetSchemaUri(branch).AbsoluteUri,
                    ["$defs"] = new JsonObject { ["document"] = document },
                };
                TryBuildJsonSchema(branchDocument, out branchSchema);
                branchSchemas.Add(schemaKey, branchSchema);
            }

            return branchSchema;
        }

        bool AreConditionsValid(JsonNode instance, List<RepairCondition> conditions)
        {
            foreach (RepairCondition condition in conditions)
            {
                JsonSchema? conditionSchema = GetBranchSchema(condition.Schema, condition.DynamicScope);
                if (conditionSchema is null ||
                    !TryGetNodeAtPath(instance, condition.InstancePath, out JsonNode? conditionInstance) ||
                    conditionInstance is null ||
                    IsSchemaValid(conditionSchema, ProjectConditionInstance(conditionInstance, condition.Schema)) != condition.Expected)
                {
                    return false;
                }
            }

            return true;
        }

        bool IsRepairValid(JsonNode instance) =>
            IsSchemaValid(schema, instance) &&
            (inheritedConditions is null || AreConditionsValid(instance, inheritedConditions));

        if (IsRepairValid(resultNode))
        {
            return null;
        }

        JsonNode? EnqueueRepairs(JsonNode instance, int depth)
        {
            if (depth >= remainingDepth)
            {
                return null;
            }

            Dictionary<string, SchemaObjectContext> contexts = new(StringComparer.Ordinal);
            List<RepairCandidateGroup> groups = [];
            CollectSchemaContexts(
                instance, schemaNode, references.GetResourceRoot(schemaNode), string.Empty, contexts,
                new SchemaTraversal(traversalBudget), groups, references, dynamicScope);
            if (inheritedConditions is { Count: > 0 })
            {
                groups = groups.Select(group => group with
                {
                    Conditions = [.. inheritedConditions, .. group.Conditions],
                }).ToList();
            }

            List<RepairCandidate> candidates = CreateRepairCandidates(
                instance, originalResult, serializerOptions, contexts);
            if (candidates.Count == 0)
            {
                return null;
            }

            HashSet<string> grouped = new(groups.SelectMany(static group => group.Keys), StringComparer.Ordinal);
            Dictionary<string, RepairCandidate> candidatesByKey = candidates.ToDictionary(static candidate => candidate.Key, StringComparer.Ordinal);
            List<RepairCandidate> ungrouped = candidates.Where(candidate => !grouped.Contains(candidate.Key)).ToList();
            Dictionary<(JsonNode Applicator, string Path), JsonNode> exclusiveChoices = new();
            JsonNode combinedResult = instance.DeepClone();
            bool hasCombinedRepairs = false;
            foreach (RepairCandidate candidate in ungrouped)
            {
                ApplyRepairCandidate(combinedResult, candidate);
                hasCombinedRepairs = true;
            }

            foreach (RepairCandidateGroup group in groups)
            {
                if (group.Choices.Any(choice =>
                    exclusiveChoices.TryGetValue((choice.Applicator, choice.InstancePath), out JsonNode? selected) &&
                    !ReferenceEquals(selected, choice.Branch)))
                {
                    continue;
                }

                if (!TryGetNodeAtPath(combinedResult, group.InstancePath, out JsonNode? currentBranch) || currentBranch is null)
                {
                    continue;
                }

                JsonSchema? branchSchema = GetBranchSchema(group.Schema, group.DynamicScope);
                if (branchSchema is null)
                {
                    continue;
                }

                // A leaf must not lock an enclosing oneOf branch whose assertions fail.
                // Once that ancestor has qualified, avoid revalidating its entire subtree.
                List<RepairExclusiveChoice> unqualifiedAncestors = group.Choices.Where(choice =>
                    !(ReferenceEquals(choice.Branch, group.Schema) && choice.InstancePath == group.InstancePath) &&
                    !exclusiveChoices.ContainsKey((choice.Applicator, choice.InstancePath))).ToList();

                // Compose independently valid subtree repairs before searching whole-document subsets.
                List<RepairCandidate> groupCandidates = [];
                foreach (string key in group.Keys)
                {
                    if (candidatesByKey.TryGetValue(key, out RepairCandidate? candidate))
                    {
                        groupCandidates.Add(candidate);
                    }
                }

                foreach (bool includeDescendants in new[] { false, true })
                {
                    List<RepairCandidate> branchCandidates = includeDescendants
                        ? groupCandidates
                        : groupCandidates.Where(candidate =>
                            candidate.ParentPath == group.InstancePath && group.DirectKeys.Contains(candidate.Key)).ToList();
                    JsonNode branchResult = currentBranch.DeepClone();
                    foreach (RepairCandidate candidate in branchCandidates)
                    {
                        ApplyRepairCandidate(branchResult, candidate with { ParentPath = candidate.ParentPath.Substring(group.InstancePath.Length) });
                    }

                    bool branchIsValid = IsSchemaValid(branchSchema, branchResult);
                    if (!branchIsValid && (branchCandidates.Count > 0 || !includeDescendants) && depth + 1 < remainingDepth)
                    {
                        SourceRecoveryContext? branchSource = null;
                        if (originalResult is not null)
                        {
                            TryGetSourceObjectAtPath(originalResult, instance, group.InstancePath, out branchSource);
                        }

                        if (RepairStructuredResult(
                            branchResult, group.Schema, branchSchema, branchSource,
                            serializerOptions, remainingDepth - depth - 1, schemaDocument, references,
                            group.DynamicScope, traversalBudget,
                            group.Conditions.Where(condition =>
                                condition.InstancePath == group.InstancePath ||
                                condition.InstancePath.StartsWith(group.InstancePath + "/", StringComparison.Ordinal))
                                .Select(condition => condition with
                                {
                                    InstancePath = condition.InstancePath.Substring(group.InstancePath.Length),
                                }).ToList()) is JsonNode repairedBranch)
                        {
                            branchResult = repairedBranch;
                            branchIsValid = true;
                        }
                    }

                    if (branchIsValid && (group.Conditions.Count > 0 || unqualifiedAncestors.Count > 0))
                    {
                        // Conditions belong to the caller's instance and dynamic scope, which
                        // may differ from the nested branch being repaired. Test the tentative
                        // replacement, not the original instance or an isolated leaf schema.
                        JsonNode tentativeResult = branchResult;
                        if (group.InstancePath.Length > 0)
                        {
                            ReplaceRepairBranch(currentBranch, group.InstancePath, branchResult);
                            tentativeResult = combinedResult;
                        }

                        try
                        {
                            branchIsValid = AreConditionsValid(tentativeResult, group.Conditions);
                            foreach (RepairExclusiveChoice ancestor in unqualifiedAncestors)
                            {
                                if (!branchIsValid)
                                {
                                    break;
                                }

                                JsonSchema? ancestorSchema = GetBranchSchema(ancestor.Branch, ancestor.DynamicScope);
                                branchIsValid = ancestorSchema is not null &&
                                    TryGetNodeAtPath(tentativeResult, ancestor.InstancePath, out JsonNode? ancestorInstance) &&
                                    ancestorInstance is not null &&
                                    IsSchemaValid(ancestorSchema, ancestorInstance);
                            }
                        }
                        finally
                        {
                            if (group.InstancePath.Length > 0)
                            {
                                ReplaceRepairBranch(branchResult, group.InstancePath, currentBranch);
                            }
                        }
                    }

                    if (branchIsValid)
                    {
                        if (group.InstancePath.Length == 0)
                        {
                            combinedResult = branchResult;
                        }
                        else
                        {
                            ReplaceRepairBranch(currentBranch, group.InstancePath, branchResult);
                        }

                        hasCombinedRepairs = true;
                        foreach (RepairExclusiveChoice choice in group.Choices)
                        {
                            exclusiveChoices[(choice.Applicator, choice.InstancePath)] = choice.Branch;
                        }

                        break;
                    }
                }
            }

            if (hasCombinedRepairs)
            {
                // The common case composes independent repairs. Avoid allocating subset queues
                // (or repeatedly scanning the full candidate list) when that result already works.
                if (IsRepairValid(combinedResult))
                {
                    return combinedResult;
                }

                pending.Enqueue(new(combinedResult, [], depth + 1));
            }

            pending.Enqueue(new(instance, candidates, depth + 1));
            foreach (RepairCandidateGroup group in groups)
            {
                if (pending.Count >= maxCandidateSetEvaluations)
                {
                    break;
                }

                List<RepairCandidate> branchCandidates = [.. ungrouped];
                foreach (string key in group.Keys)
                {
                    if (candidatesByKey.TryGetValue(key, out RepairCandidate? candidate))
                    {
                        branchCandidates.Add(candidate);
                    }
                }

                if (branchCandidates.Count > 0 && branchCandidates.Count < candidates.Count &&
                    pending.Count < maxCandidateSetEvaluations)
                {
                    pending.Enqueue(new(instance, branchCandidates, depth + 1));
                }
            }

            for (int i = 0; i < candidates.Count && fallback.Count < maxCandidateSetEvaluations; i++)
            {
                fallback.Enqueue(new(instance, [candidates[i]], depth + 1));
            }

            for (int i = 0; i < candidates.Count && fallback.Count < maxCandidateSetEvaluations; i++)
            {
                for (int j = i + 1; j < candidates.Count && fallback.Count < maxCandidateSetEvaluations; j++)
                {
                    fallback.Enqueue(new(instance, [candidates[i], candidates[j]], depth + 1));
                }
            }

            return null;
        }

        if (EnqueueRepairs(resultNode, 0) is JsonNode composedResult)
        {
            return composedResult;
        }
        for (int evaluations = 0; evaluations < maxCandidateSetEvaluations && (pending.Count > 0 || fallback.Count > 0);)
        {
            RepairAttempt attempt = pending.Count > 0 ? pending.Dequeue() : fallback.Dequeue();
            JsonNode candidateResult = attempt.Result.DeepClone();
            foreach (RepairCandidate candidate in attempt.Candidates)
            {
                ApplyRepairCandidate(candidateResult, candidate);
            }

            if (!visited.Add(candidateResult.ToJsonString()))
            {
                continue;
            }

            evaluations++;
            if (IsRepairValid(candidateResult))
            {
                return candidateResult;
            }

            if (EnqueueRepairs(candidateResult, attempt.Depth) is JsonNode repairedResult)
            {
                return repairedResult;
            }
        }

        return null;
    }

    private static bool TryBuildJsonSchema(JsonNode schemaNode, out JsonSchema? schema)
    {
        try
        {
            JsonElement schemaElement = JsonSerializer.SerializeToElement(schemaNode, McpJsonUtilities.JsonContext.Default.JsonNode);
            schema = JsonSchema.Build(schemaElement, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            return true;
        }
        catch (Exception ex) when (ex is JsonException or JsonSchemaException or RefResolutionException or InvalidOperationException or ArgumentException)
        {
            schema = null;
            return false;
        }
    }

    private OutputSchemaCache GetOutputSchema(JsonElement outputSchema)
    {
        OutputSchemaCache? cachedSchema = Volatile.Read(ref _outputSchema);
        if (cachedSchema is not null && cachedSchema.Source.Equals(outputSchema))
        {
            return cachedSchema;
        }

        JsonNode? schemaNode = JsonSerializer.SerializeToNode(outputSchema, McpJsonUtilities.JsonContext.Default.JsonElement);
        JsonSchema? schema = null;
        if (schemaNode is not null)
        {
            TryBuildJsonSchema(schemaNode, out schema);
        }

        cachedSchema = new(outputSchema, schemaNode, schema);
        Volatile.Write(ref _outputSchema, cachedSchema);
        return cachedSchema;
    }

    private sealed record OutputSchemaCache(JsonElement Source, JsonNode? Node, JsonSchema? Schema);

    private static bool IsSchemaValid(JsonSchema schema, JsonNode instance)
    {
        try
        {
            JsonElement instanceElement = JsonSerializer.SerializeToElement(instance, McpJsonUtilities.JsonContext.Default.JsonNode);
            return schema.Evaluate(instanceElement).IsValid;
        }
        catch (Exception ex) when (ex is JsonException or JsonSchemaException or RefResolutionException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static List<RepairCandidate> CreateRepairCandidates(
        JsonNode resultNode,
        SourceRecoveryContext? originalResult,
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

                if (TryGetSourceValue(
                    originalResult,
                    resultNode,
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

    private sealed record RepairCandidate(string ParentPath, string PropertyName, JsonNode? Value)
    {
        internal string Key => ParentPath + "\u0000" + PropertyName;
    }

    private sealed record RepairAttempt(JsonNode Result, List<RepairCandidate> Candidates, int Depth);

    private sealed record RepairExclusiveChoice(
        JsonNode Applicator, JsonNode Branch, string InstancePath, List<JsonNode> DynamicScope);

    private sealed record RepairCondition(
        JsonNode Schema, string InstancePath, List<JsonNode> DynamicScope, bool Expected);

    private static JsonNode ProjectConditionInstance(JsonNode instance, JsonNode schema)
    {
        if (instance is not JsonObject instanceObject || schema is not JsonObject schemaObject ||
            schemaObject.Any(static property => property.Key is not ("properties" or "required" or "type")))
        {
            return instance;
        }

        // These keywords cannot observe unrelated properties. In particular, do not
        // copy a large sibling collection for every small descendant repair.
        JsonObject projection = new();
        if (schemaObject["properties"] is JsonObject properties)
        {
            foreach (var property in properties)
            {
                if (instanceObject.TryGetPropertyValue(property.Key, out JsonNode? value))
                {
                    projection[property.Key] = value?.DeepClone();
                }
            }
        }

        if (schemaObject["required"] is JsonArray required)
        {
            foreach (JsonNode? item in required)
            {
                if (item is JsonValue name && name.TryGetValue<string>(out string? propertyName) &&
                    propertyName is not null && !projection.ContainsKey(propertyName) &&
                    instanceObject.ContainsKey(propertyName))
                {
                    projection[propertyName] = null;
                }
            }
        }

        return projection;
    }

    private sealed record RepairCandidateGroup(
        HashSet<string> Keys, HashSet<string> DirectKeys, string InstancePath, JsonNode Schema,
        List<JsonNode> DynamicScope, List<RepairExclusiveChoice> Choices, List<RepairCondition> Conditions);

    private static void ReplaceRepairBranch(JsonNode currentBranch, string instancePath, JsonNode replacement)
    {
        string segment = DecodeJsonPointerSegment(instancePath.Substring(instancePath.LastIndexOf('/') + 1));
        if (currentBranch.Parent is JsonArray parentArray)
        {
            parentArray[int.Parse(segment, NumberStyles.None, CultureInfo.InvariantCulture)] = replacement;
        }
        else if (currentBranch.Parent is JsonObject parentObject)
        {
            parentObject[segment] = replacement;
        }
    }

    private static string DecodeJsonPointerSegment(string segment) => segment.Replace("~1", "/").Replace("~0", "~");

    private sealed class RepairTraversalBudget
    {
        // Shared by document attempts and recursive branch repairs, including cached merges.
        private int _remainingWork = 1_000_000;

        internal bool TrySpend(int work)
        {
            if (work > _remainingWork)
            {
                _remainingWork = 0;
                return false;
            }

            _remainingWork -= work;
            return true;
        }
    }

    private sealed class SchemaTraversal(RepairTraversalBudget budget)
    {
        internal RepairTraversalBudget Budget { get; } = budget;
        internal Dictionary<(JsonNode Schema, string Path, string Scope), SchemaTraversalResult> Completed { get; } = new();
        internal HashSet<(JsonNode Schema, string Path, string Scope)> Active { get; } = [];
        internal int IncompleteVersion { get; set; }
    }

    private sealed record SchemaTraversalResult(
        Dictionary<string, SchemaObjectContext> Contexts, List<RepairCandidateGroup> Groups);

    private static string GetSchemaPointer(JsonNode schema, JsonNode resource)
    {
        string pointer = string.Empty;
        for (JsonNode node = schema; !ReferenceEquals(node, resource) && node.Parent is JsonNode parent; node = parent)
        {
            string segment = parent is JsonArray array
                ? array.IndexOf(node).ToString(CultureInfo.InvariantCulture)
                : ((JsonObject)parent).First(property => ReferenceEquals(property.Value, node)).Key;
            pointer = AppendJsonPointer(string.Empty, segment) + pointer;
        }

        return pointer;
    }

    private static void CollectSchemaContexts(
        JsonNode? instanceNode,
        JsonNode? schemaNode,
        JsonNode schemaRoot,
        string instancePath,
        Dictionary<string, SchemaObjectContext> contexts,
        SchemaTraversal traversal,
        List<RepairCandidateGroup> groups,
        SchemaReferenceResolver references,
        List<JsonNode>? dynamicScope = null)
    {
        if (schemaNode is JsonValue schemaValue && schemaValue.TryGetValue<bool>(out _))
        {
            return;
        }

        if (schemaNode is not JsonObject schemaObject)
        {
            return;
        }

        dynamicScope ??= [schemaRoot];
        if (!ReferenceEquals(schemaRoot, schemaObject) && schemaObject.ContainsKey("$id"))
        {
            schemaRoot = schemaObject;
            dynamicScope = SchemaReferenceResolver.EnterResource(dynamicScope, schemaRoot);
        }

        var state = (schemaNode, instancePath, references.GetScopeKey(dynamicScope));
        if (!traversal.Budget.TrySpend(1))
        {
            return;
        }

        if (traversal.Completed.TryGetValue(state, out SchemaTraversalResult? completed))
        {
            if (traversal.Budget.TrySpend(completed.Contexts.Count + completed.Groups.Count))
            {
                MergeSchemaContexts(contexts, completed.Contexts);
                groups.AddRange(completed.Groups);
            }

            return;
        }

        if (traversal.Active.Count >= 256 || !traversal.Active.Add(state))
        {
            traversal.IncompleteVersion++;
            return;
        }

        int incompleteVersion = traversal.IncompleteVersion;
        Dictionary<string, SchemaObjectContext> destinationContexts = contexts;
        List<RepairCandidateGroup> destinationGroups = groups;
        contexts = new(StringComparer.Ordinal);
        groups = [];

        foreach (string referenceKeyword in new[] { "$ref", "$dynamicRef" })
        {
            if (schemaObject[referenceKeyword] is JsonValue refValue &&
                refValue.TryGetValue<string>(out string? refText) &&
                refText is not null)
            {
                if (references.TryResolve(schemaRoot, refText, out JsonNode? referencedSchema, out JsonNode referencedResource))
                {
                    if (referenceKeyword == "$dynamicRef" && referencedSchema is JsonObject referencedObject &&
                        referencedObject["$dynamicAnchor"]?.GetValue<string>() is string anchor &&
                        refText.IndexOf('#') is int fragmentStart && fragmentStart >= 0 &&
                        string.Equals(Uri.UnescapeDataString(refText[(fragmentStart + 1)..]), anchor, StringComparison.Ordinal))
                    {
                        foreach (JsonNode resource in dynamicScope)
                        {
                            if (TryFindSchemaAnchor(resource, anchor, out JsonNode? dynamicSchema, dynamicOnly: true))
                            {
                                referencedSchema = dynamicSchema;
                                referencedResource = resource;
                                break;
                            }
                        }
                    }

                    CollectSchemaContexts(instanceNode, referencedSchema, referencedResource, instancePath, contexts, traversal, groups, references,
                        SchemaReferenceResolver.EnterResource(dynamicScope, referencedResource));
                }
            }
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
                    traversal,
                    groups, references, dynamicScope);
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
                                traversal,
                                groups, references, dynamicScope);
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
                        traversal,
                        groups, references, dynamicScope);
                }

                if (schemaObject["unevaluatedProperties"] is JsonNode unevaluatedPropertySchema)
                {
                    CollectSchemaBranch(
                        property.Value, unevaluatedPropertySchema, schemaRoot,
                        AppendJsonPointer(instancePath, property.Key), contexts, traversal, groups, references, dynamicScope);
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
                        traversal,
                        groups, references, dynamicScope);
                }

                foreach (string keyword in new[] { "contains", "unevaluatedItems" })
                {
                    if (schemaObject[keyword] is JsonNode subschema)
                    {
                        CollectSchemaBranch(
                            arrayInstance[i], subschema, schemaRoot,
                            AppendJsonPointer(instancePath, i.ToString(CultureInfo.InvariantCulture)),
                            contexts, traversal, groups, references, dynamicScope);
                    }
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
                        traversal,
                        groups, references, dynamicScope);
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
                    if (compositionKeyword == "allOf")
                    {
                        CollectSchemaContexts(
                            instanceNode, branch, schemaRoot, instancePath, contexts,
                            traversal, groups, references, dynamicScope);
                    }
                    else
                    {
                        CollectSchemaBranch(instanceNode, branch, schemaRoot, instancePath, contexts, traversal, groups, references, dynamicScope,
                            compositionKeyword == "oneOf" ? composition : null);
                    }
                }
            }
        }

        if (schemaObject["if"] is JsonNode ifSchema)
        {
            if (schemaObject["then"] is JsonNode thenSchema)
            {
                CollectSchemaBranch(instanceNode, thenSchema, schemaRoot, instancePath, contexts, traversal, groups, references, dynamicScope,
                    condition: new(ifSchema, instancePath,
                        SchemaReferenceResolver.EnterResource(dynamicScope, references.GetResourceRoot(ifSchema)), true));
            }

            if (schemaObject["else"] is JsonNode elseSchema)
            {
                CollectSchemaBranch(instanceNode, elseSchema, schemaRoot, instancePath, contexts, traversal, groups, references, dynamicScope,
                    condition: new(ifSchema, instancePath,
                        SchemaReferenceResolver.EnterResource(dynamicScope, references.GetResourceRoot(ifSchema)), false));
            }
        }

        groups = groups.Distinct().ToList();
        traversal.Active.Remove(state);
        // A cycle or depth cutoff produces a partial summary that cannot be reused
        // when this schema is reached later through a different caller.
        if (incompleteVersion == traversal.IncompleteVersion)
        {
            traversal.Completed[state] = new(contexts, groups);
        }
        if (traversal.Budget.TrySpend(contexts.Count + groups.Count))
        {
            MergeSchemaContexts(destinationContexts, contexts);
            destinationGroups.AddRange(groups);
        }
    }

    private static void MergeSchemaContexts(
        Dictionary<string, SchemaObjectContext> destination,
        Dictionary<string, SchemaObjectContext> source)
    {
        foreach (KeyValuePair<string, SchemaObjectContext> entry in source)
        {
            GetOrCreateSchemaContext(destination, entry.Key).Required.UnionWith(entry.Value.Required);
        }
    }

    private static void CollectSchemaBranch(
        JsonNode? instanceNode,
        JsonNode branch,
        JsonNode schemaRoot,
        string instancePath,
        Dictionary<string, SchemaObjectContext> contexts,
        SchemaTraversal traversal,
        List<RepairCandidateGroup> groups,
        SchemaReferenceResolver references,
        List<JsonNode> dynamicScope,
        JsonNode? exclusiveApplicator = null,
        RepairCondition? condition = null)
    {
        Dictionary<string, SchemaObjectContext> branchContexts = new(StringComparer.Ordinal);
        List<RepairCandidateGroup> branchGroups = [];
        CollectSchemaContexts(
            instanceNode, branch, schemaRoot, instancePath, branchContexts,
            traversal, branchGroups, references, dynamicScope);
        List<JsonNode> branchScope = SchemaReferenceResolver.EnterResource(dynamicScope, references.GetResourceRoot(branch));
        List<RepairExclusiveChoice> choices = exclusiveApplicator is null
            ? [] : [new(exclusiveApplicator, branch, instancePath, branchScope)];
        List<RepairCondition> conditions = condition is null ? [] : [condition];
        HashSet<string> group = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, SchemaObjectContext> entry in branchContexts)
        {
            GetOrCreateSchemaContext(contexts, entry.Key).Required.UnionWith(entry.Value.Required);
            foreach (string property in entry.Value.Required)
            {
                group.Add(entry.Key + "\u0000" + property);
            }
        }

        if (group.Count > 0)
        {
            HashSet<string> directKeys = new(group, StringComparer.Ordinal);
            directKeys.ExceptWith(branchGroups.SelectMany(static nested => nested.Keys));
            // Restore this branch's own discriminator before composing nested selectors.
            // Nested alternatives must not be added en masse before re-evaluating them.
            groups.Add(new(group, directKeys, instancePath, branch, branchScope, choices, conditions));
        }

        foreach (RepairCandidateGroup branchGroup in branchGroups)
        {
            groups.Add(branchGroup with
            {
                Choices = [.. choices, .. branchGroup.Choices],
                Conditions = [.. conditions, .. branchGroup.Conditions],
            });
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
            string segment = DecodeJsonPointerSegment(rawSegment);
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
        SourceRecoveryContext? sourceRoot,
        JsonNode serializedRoot,
        string parentPath,
        string propertyName,
        JsonSerializerOptions serializerOptions,
        out JsonNode? value)
    {
        value = null;
        if (sourceRoot is null ||
            !TryGetSourceObjectAtPath(sourceRoot, serializedRoot, parentPath, out SourceRecoveryContext? parent) ||
            parent is null ||
            !TryGetNodeAtPath(serializedRoot, parentPath, out JsonNode? serializedParent) ||
            parent.GetChild(propertyName, serializedParent) is not { Property: { } property, Owner: { } owner } member)
        {
            return false;
        }

        if (!WasOmittedBySupportedIgnoreCondition(owner, member.Value, property, serializerOptions))
        {
            return false;
        }

        return TrySerializeSourceMember(member, out value);
    }

    private static bool WasOmittedBySupportedIgnoreCondition(
        object sourceParent,
        object? value,
        JsonPropertyInfo? propertyInfo,
        JsonSerializerOptions serializerOptions)
    {
        JsonIgnoreAttribute? ignoreAttribute = GetIgnoreAttribute(propertyInfo);

        if (propertyInfo is not null && IsExcludedReadOnlyMember(propertyInfo, serializerOptions))
        {
            return false;
        }

        if (value is null && serializerOptions.RespectNullableAnnotations && propertyInfo is { IsGetNullable: false })
        {
            return false;
        }

        if (propertyInfo?.ShouldSerialize is { } shouldSerialize &&
            (ignoreAttribute is null || !IsJsonIgnoreConditionPredicate(shouldSerialize)) &&
            !shouldSerialize(sourceParent, value))
        {
            return false;
        }

        JsonIgnoreCondition ignoreCondition = ignoreAttribute?.Condition ?? serializerOptions.DefaultIgnoreCondition;
        // For an absent value-type member, the serializer already compared against default.
        // Boxed Equals can disagree with the EqualityComparer<T> used by the serializer.
        return ignoreCondition switch
        {
            JsonIgnoreCondition.WhenWritingNull => value is null,
            JsonIgnoreCondition.WhenWritingDefault =>
                propertyInfo is not null &&
                (value is null || (propertyInfo.PropertyType.IsValueType &&
                    Nullable.GetUnderlyingType(propertyInfo.PropertyType) is null)),
            _ => false,
        };
    }

    private static JsonIgnoreAttribute? GetIgnoreAttribute(JsonPropertyInfo? propertyInfo) =>
        propertyInfo?.AttributeProvider is ICustomAttributeProvider attributeProvider
            ? attributeProvider.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
                .OfType<JsonIgnoreAttribute>()
                .FirstOrDefault()
            : null;

    private static bool IsExcludedReadOnlyMember(JsonPropertyInfo propertyInfo, JsonSerializerOptions serializerOptions)
    {
        if (propertyInfo.ShouldSerialize is not null || GetIgnoreAttribute(propertyInfo) is not null || propertyInfo.Set is not null)
        {
            return false;
        }

        if (propertyInfo.AttributeProvider is FieldInfo)
        {
            return serializerOptions.IgnoreReadOnlyFields;
        }

        if (propertyInfo.AttributeProvider is not PropertyInfo && serializerOptions.IgnoreReadOnlyFields)
        {
            return true;
        }

        return serializerOptions.IgnoreReadOnlyProperties &&
            serializerOptions.GetTypeInfo(propertyInfo.PropertyType).Kind is not (JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary);
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

    private static bool TryGetSourceObjectAtPath(
        SourceRecoveryContext sourceRoot,
        JsonNode serializedRoot,
        string path,
        out SourceRecoveryContext? value)
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

        JsonNode? serializedParent = serializedRoot;
        foreach (string rawSegment in path.Substring(1).Split('/'))
        {
            string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            if (value?.GetChild(segment, serializedParent) is not { } child)
            {
                value = null;
                return false;
            }

            value = child;
            serializedParent = serializedParent switch
            {
                JsonObject jsonObject => jsonObject[segment],
                JsonArray jsonArray when int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                    index >= 0 && index < jsonArray.Count => jsonArray[index],
                _ => null,
            };
        }

        return true;
    }

    private sealed class SourceRecoveryContext(
        object? value, JsonTypeInfo contract, JsonPropertyInfo? property = null,
        object? owner = null, JsonTypeInfo? ownerContract = null)
    {
        private readonly Dictionary<string, SourceRecoveryContext?> _children = new(StringComparer.Ordinal);
        private List<object?>? _items;
        private bool _dictionaryRead;

        internal object? Value { get; } = value;
        internal JsonTypeInfo Contract { get; } = contract;
        internal JsonPropertyInfo? Property { get; } = property;
        internal object? Owner { get; } = owner;
        internal JsonTypeInfo? OwnerContract { get; } = ownerContract;

        internal SourceRecoveryContext? GetChild(string name, JsonNode? serializedParent)
        {
            if (_children.TryGetValue(name, out SourceRecoveryContext? child))
            {
                return child;
            }

            try
            {
                child = GetChildCore(name, serializedParent);
            }
            catch (NotSupportedException)
            {
                // Missing metadata and opaque converter contracts cannot authorize recovery.
                child = null;
            }

            _children[name] = child;
            return child;
        }

        private SourceRecoveryContext? GetChildCore(string name, JsonNode? serializedParent)
        {
            if (Value is null || Property?.CustomConverter is not null)
            {
                return null;
            }

            JsonSerializerOptions options = Contract.Options;
            JsonTypeInfo typeInfo = GetActiveContract();
            if (options.ReferenceHandler is { } handler && !ReferenceEquals(handler, ReferenceHandler.IgnoreCycles) &&
                serializedParent is JsonObject referenceObject)
            {
                if (referenceObject.ContainsKey("$ref"))
                {
                    return null;
                }

                if (typeInfo.Kind == JsonTypeInfoKind.Enumerable && name == "$values" &&
                    referenceObject["$values"] is JsonArray)
                {
                    return new(Value, typeInfo);
                }
            }

            if (typeInfo.Kind == JsonTypeInfoKind.Dictionary &&
                serializedParent is JsonObject serializedDictionary && Value is IEnumerable entries)
            {
                if (!_dictionaryRead)
                {
                    _dictionaryRead = true;
                    JsonTypeInfo keyContract = options.GetTypeInfo(typeInfo.KeyType!);
                    foreach (object entry in entries)
                    {
                        if (!TryGetDictionaryEntry(entry, out object? entryKey, out object? entryValue))
                        {
                            continue;
                        }

                        // Non-generic dictionaries dispatch non-string keys through the object converter.
                        JsonTypeInfo entryKeyContract = Value is IDictionary && keyContract.Type == typeof(string) && entryKey is not string
                            ? options.GetTypeInfo(typeof(object))
                            : keyContract;
                        if (!TrySerializeDictionaryKey(entryKey, entryKeyContract, out string serializedKey) ||
                            !serializedDictionary.ContainsKey(serializedKey))
                        {
                            continue;
                        }

                        _children[serializedKey] = _children.ContainsKey(serializedKey)
                            ? null
                            : new(entryValue, options.GetTypeInfo(typeInfo.ElementType!));
                    }
                }

                return _children.TryGetValue(name, out SourceRecoveryContext? entryContext) ? entryContext : null;
            }

            if (typeInfo.Kind == JsonTypeInfoKind.Enumerable && serializedParent is JsonArray serializedArray &&
                int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                index >= 0 && index < serializedArray.Count && Value is IEnumerable enumerable)
            {
                object? item;
                if (Value is IList list)
                {
                    if (index >= list.Count)
                    {
                        return null;
                    }

                    item = list[index];
                }
                else
                {
                    if (_items is null)
                    {
                        _items = [];
                        foreach (object? element in enumerable)
                        {
                            _items.Add(element);
                            if (_items.Count >= serializedArray.Count)
                            {
                                break;
                            }
                        }
                    }

                    if (index >= _items.Count)
                    {
                        return null;
                    }

                    item = _items[index];
                }

                return new(item, options.GetTypeInfo(typeInfo.ElementType!));
            }

            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return null;
            }

            SourceObjectContract sourceContract = s_sourceContracts.GetValue(typeInfo, static contract => new(contract));
            if (sourceContract.Properties.TryGetValue(name, out JsonPropertyInfo? member))
            {
                if (member.Get is null || IsExcludedReadOnlyMember(member, options))
                {
                    return null;
                }

                object? memberValue = member.Get(Value);
                JsonTypeInfo memberContract = options.GetTypeInfo(member.PropertyType);
                return new(memberValue, memberContract, member, Value, typeInfo);
            }

            JsonPropertyInfo? extensionData = sourceContract.ExtensionData;
            if (extensionData?.Get is not { } getExtensionData ||
                extensionData.CustomConverter is not null || IsExcludedReadOnlyMember(extensionData, options))
            {
                return null;
            }

            object? extensionValue = getExtensionData(Value);
            if (extensionValue is null || extensionData.ShouldSerialize?.Invoke(Value, extensionValue) == false)
            {
                return null;
            }

            JsonTypeInfo extensionType = options.GetTypeInfo(extensionData.PropertyType);
            if (extensionType.Kind != JsonTypeInfoKind.Dictionary)
            {
                return null;
            }

            object? extensionEntry;
            if (extensionValue is IDictionary<string, object?> objectData && objectData.TryGetValue(name, out extensionEntry))
            {
                return new(extensionEntry, options.GetTypeInfo(extensionType.ElementType!));
            }

            if (extensionValue is IDictionary<string, JsonElement> elementData && elementData.TryGetValue(name, out JsonElement elementEntry))
            {
                return new(elementEntry, options.GetTypeInfo(typeof(JsonElement)));
            }

            return null;
        }

        private JsonTypeInfo GetActiveContract()
        {
            JsonSerializerOptions options = Contract.Options;
            Type runtimeType = Value!.GetType();
            if (runtimeType == Contract.Type)
            {
                return Contract;
            }

            if (Contract.Type == typeof(object) && Contract.Converter.GetType().Assembly == typeof(JsonSerializer).Assembly)
            {
                return options.GetTypeInfo(runtimeType);
            }

            if (Nullable.GetUnderlyingType(Contract.Type) is { } underlyingType &&
                Contract.Converter.GetType().Assembly == typeof(JsonSerializer).Assembly)
            {
                return options.GetTypeInfo(underlyingType);
            }

            if (Contract.PolymorphismOptions is not { } polymorphism)
            {
                return Contract;
            }

            foreach (JsonDerivedType derived in polymorphism.DerivedTypes)
            {
                if (derived.DerivedType == runtimeType)
                {
                    return options.GetTypeInfo(runtimeType);
                }
            }

            if (polymorphism.UnknownDerivedTypeHandling == JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)
            {
                Type? nearest = null;
                foreach (JsonDerivedType derived in polymorphism.DerivedTypes)
                {
                    if (derived.DerivedType.IsAssignableFrom(runtimeType) &&
                        (nearest is null || nearest.IsAssignableFrom(derived.DerivedType)))
                    {
                        nearest = derived.DerivedType;
                    }
                }

                if (nearest is not null)
                {
                    return options.GetTypeInfo(nearest);
                }
            }

            return Contract;
        }
    }

    private sealed class SourceObjectContract
    {
        private readonly JsonTypeInfo _contract;
        private readonly ConcurrentDictionary<JsonPropertyInfo, Lazy<JsonTypeInfo<object>>> _serializationContracts = new();

        internal Dictionary<string, JsonPropertyInfo> Properties { get; } = new(StringComparer.Ordinal);
        internal JsonPropertyInfo? ExtensionData { get; }

        internal SourceObjectContract(JsonTypeInfo contract)
        {
            _contract = contract;
            foreach (JsonPropertyInfo property in contract.Properties)
            {
                if (property.IsExtensionData)
                {
                    ExtensionData = property;
                }
                else
                {
                    Properties.Add(property.Name, property);
                }
            }
        }

        internal JsonTypeInfo<object> GetSerializationContract(JsonPropertyInfo property) =>
            _serializationContracts.GetOrAdd(property, member => new(() => CreateSerializationContract(member))).Value;

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming", "IL2026",
            Justification = "The property type metadata is cached in the original options before CreateJsonPropertyInfo reuses its typed property factory.")]
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Aot", "IL3050",
            Justification = "CreateJsonPropertyInfo uses the cached property type metadata, so no new generic types are constructed.")]
        private JsonTypeInfo<object> CreateSerializationContract(JsonPropertyInfo property)
        {
            JsonSerializerOptions options = _contract.Options;
            _ = options.GetTypeInfo(property.PropertyType);
            // Reuse the owner's object converter, not its frozen metadata. The object-typed
            // adapter still serializes the actual owner, preserving cycle detection.
            JsonTypeInfo<object> contract = JsonMetadataServices.CreateValueInfo<object>(options, _contract.Converter);
            contract.NumberHandling = _contract.NumberHandling;
            contract.PolymorphismOptions = null;
            contract.OnSerializing = null;
            contract.OnSerialized = null;
            JsonPropertyInfo candidate = contract.CreateJsonPropertyInfo(property.PropertyType, property.Name);
            candidate.Get = static _ => t_serializingSourceMember!.Value;
            candidate.ShouldSerialize = static (_, _) => true;
            candidate.CustomConverter = GetPropertyConverter(property, property.PropertyType);
            candidate.NumberHandling = property.NumberHandling;
            candidate.IsGetNullable = property.IsGetNullable;
            contract.Properties.Add(candidate);
            contract.MakeReadOnly();
            return contract;
        }
    }

#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties, typeof(KeyValuePair<,>))]
#endif
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "The reflected properties belong to KeyValuePair<,>, whose public properties are explicitly preserved.")]
    private static bool TryGetDictionaryEntry(object entry, out object? key, out object? value)
    {
        if (entry is DictionaryEntry dictionaryEntry)
        {
            key = dictionaryEntry.Key;
            value = dictionaryEntry.Value;
            return true;
        }

        Type type = entry.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            key = type.GetProperty(nameof(KeyValuePair<object, object>.Key))!.GetValue(entry);
            value = type.GetProperty(nameof(KeyValuePair<object, object>.Value))!.GetValue(entry);
            return true;
        }

        key = value = null;
        return false;
    }

#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        nameof(JsonConverter<object>.WriteAsPropertyName), typeof(JsonConverter<>))]
#endif
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "The reflected method belongs to JsonConverter<T>, whose WriteAsPropertyName method is explicitly preserved.")]
    private static bool TrySerializeDictionaryKey(object? key, JsonTypeInfo contract, out string name)
    {
        name = string.Empty;
        try
        {
            if (key is string stringValue && contract.Type == typeof(string) &&
                contract.Converter.GetType().Assembly == typeof(JsonSerializer).Assembly)
            {
                name = contract.Options.DictionaryKeyPolicy is { } policy ? policy.ConvertName(stringValue) : stringValue;
                if (name is not null)
                {
                    return true;
                }

                name = string.Empty;
                return false;
            }

            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream))
            {
                writer.WriteStartObject();
                if (contract.Converter is JsonConverter<string> stringConverter && key is string stringKey)
                {
                    stringConverter.WriteAsPropertyName(writer, stringKey, contract.Options);
                }
                else if (contract.Converter is JsonConverter<object> objectConverter && key is not null)
                {
                    objectConverter.WriteAsPropertyName(writer, key, contract.Options);
                }
                else
                {
                    Type? converterType = contract.Converter.GetType();
                    while (converterType is not null &&
                        (!converterType.IsGenericType || converterType.GetGenericTypeDefinition() != typeof(JsonConverter<>)))
                    {
                        converterType = converterType.BaseType;
                    }

                    converterType?.GetMethod(nameof(JsonConverter<object>.WriteAsPropertyName))?
                        .Invoke(contract.Converter, [writer, key, contract.Options]);
                }

                writer.WriteNullValue();
                writer.WriteEndObject();
            }

            using JsonDocument document = JsonDocument.Parse(stream.ToArray());
            name = document.RootElement.EnumerateObject().Single().Name;
            return true;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is
            OperationCanceledException or OutOfMemoryException or StackOverflowException or AccessViolationException)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException!).Throw();
            throw;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return false;
        }
    }

    private static bool TrySerializeSourceMember(SourceRecoveryContext member, out JsonNode? serializedValue)
    {
        serializedValue = null;
        try
        {
            JsonTypeInfo originalContract = member.OwnerContract!;
            JsonSerializerOptions options = originalContract.Options;
            JsonPropertyInfo property = member.Property!;
            JsonTypeInfo<object> contract = s_sourceContracts.GetValue(originalContract, static owner => new(owner))
                .GetSerializationContract(property);
            JsonElement element;
            SourceRecoveryContext? previous = t_serializingSourceMember;
            try
            {
                // Serialization is synchronous; restore the slot for nested invocations.
                t_serializingSourceMember = member;
                // Value-type serialization must not track the original box as a reference cycle.
                object? owner = originalContract.Type.IsValueType ? RuntimeHelpers.GetObjectValue(member.Owner) : member.Owner;
                element = JsonSerializer.SerializeToElement(owner!, contract).GetProperty(property.Name);
            }
            finally
            {
                t_serializingSourceMember = previous;
            }
            if (options.ReferenceHandler is { } handler && !ReferenceEquals(handler, ReferenceHandler.IgnoreCycles) &&
                ContainsReferenceMetadata(element))
            {
                return false;
            }

            serializedValue = JsonSerializer.SerializeToNode(element, McpJsonUtilities.JsonContext.Default.JsonElement);
            return true;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return false;
        }
    }

    private static bool ContainsReferenceMetadata(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Any(static property =>
                property.Name is "$id" or "$ref" || ContainsReferenceMetadata(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Any(static item => ContainsReferenceMetadata(item)),
            _ => false,
        };

    private static bool HasDuplicatePropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicatePropertyNames(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(static item => HasDuplicatePropertyNames(item));
        }

        return false;
    }

    private static JsonConverter? GetPropertyConverter(JsonPropertyInfo? propertyInfo, Type propertyType)
    {
        if (propertyInfo is null)
        {
            return null;
        }

        if (propertyInfo.CustomConverter is JsonConverter customConverter)
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

    private sealed class SchemaReferenceResolver
    {
        private readonly Dictionary<Uri, JsonNode> _resources = new();
        private readonly Dictionary<JsonNode, Uri> _resourceIds = new();
        private readonly List<JsonNode> _schemas = [];

        internal SchemaReferenceResolver(JsonNode schema)
        {
            Register(schema, new Uri("https://mcp.invalid/schema"), isRoot: true);
        }

        internal Uri GetSchemaUri(JsonNode schema)
        {
            JsonNode resource = GetResourceRoot(schema);
            string pointer = GetSchemaPointer(schema, resource);
            return pointer.Length == 0 ? _resourceIds[resource] : new Uri(_resourceIds[resource], "#" + Uri.EscapeDataString(pointer));
        }

        internal JsonNode GetResourceRoot(JsonNode schema)
        {
            JsonNode resource = schema;
            while (!_resourceIds.ContainsKey(resource) && resource.Parent is JsonNode parent)
            {
                resource = parent;
            }

            return resource;
        }

        internal static List<JsonNode> EnterResource(List<JsonNode> scope, JsonNode resource) =>
            scope.Contains(resource) ? scope : [.. scope, resource];

        internal string GetScopeKey(List<JsonNode> scope) =>
            string.Join("\n", scope.Select(resource => _resourceIds[resource].AbsoluteUri));

        internal void BindDynamicReferences(JsonNode document, JsonNode copy, List<JsonNode> scope)
        {
            // JsonSchemaNode exposes evaluation metadata, but EvaluationContext cannot be
            // constructed with a caller scope. Bind only names already fixed by that scope:
            // entering further resources cannot change their outermost dynamic anchor.
            foreach (JsonNode node in _schemas)
            {
                if (node is not JsonObject schemaObject ||
                    schemaObject["$dynamicRef"] is not JsonValue value ||
                    !value.TryGetValue<string>(out string? reference) ||
                    reference is null ||
                    !TryResolve(GetResourceRoot(node), reference, out JsonNode? target, out _) ||
                    target is not JsonObject targetObject ||
                    targetObject["$dynamicAnchor"]?.GetValue<string>() is not string anchor ||
                    reference.IndexOf('#') < 0 ||
                    !string.Equals(Uri.UnescapeDataString(reference[(reference.IndexOf('#') + 1)..]), anchor, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (JsonNode resource in scope)
                {
                    if (TryFindSchemaAnchor(resource, anchor, out JsonNode? bound, dynamicOnly: true) &&
                        bound is not null &&
                        TryGetNodeAtPath(copy, GetSchemaPointer(node, document), out JsonNode? copiedNode) &&
                        copiedNode is JsonObject copiedObject)
                    {
                        copiedObject.Remove("$dynamicRef");
                        if (copiedObject.ContainsKey("$ref"))
                        {
                            JsonArray allOf = copiedObject["allOf"] as JsonArray ?? [];
                            if (allOf.Parent is null)
                            {
                                copiedObject["allOf"] = allOf;
                            }

                            allOf.Add((JsonNode)new JsonObject { ["$ref"] = GetSchemaUri(bound).AbsoluteUri });
                        }
                        else
                        {
                            copiedObject["$ref"] = GetSchemaUri(bound).AbsoluteUri;
                        }
                        break;
                    }
                }
            }
        }

        private void Register(JsonNode? schema, Uri baseUri, bool isRoot = false)
        {
            if (schema is not JsonObject schemaObject)
            {
                return;
            }

            _schemas.Add(schema);
            bool isResource = isRoot;
            if (schemaObject["$id"] is JsonValue idValue && idValue.TryGetValue<string>(out string? id) &&
                Uri.TryCreate(baseUri, id, out Uri? resourceUri))
            {
                baseUri = resourceUri;
                isResource = true;
            }

            if (isResource)
            {
                _resourceIds[schema] = baseUri;
                _resources[new UriBuilder(baseUri) { Fragment = string.Empty }.Uri] = schema;
            }

            foreach (string keyword in new[] { "$defs", "definitions", "properties", "patternProperties", "dependentSchemas" })
            {
                if (schemaObject[keyword] is JsonObject schemas)
                {
                    foreach (KeyValuePair<string, JsonNode?> child in schemas)
                    {
                        Register(child.Value, baseUri);
                    }
                }
            }

            foreach (string keyword in new[] { "allOf", "anyOf", "oneOf", "prefixItems" })
            {
                if (schemaObject[keyword] is JsonArray schemas)
                {
                    foreach (JsonNode? child in schemas)
                    {
                        Register(child, baseUri);
                    }
                }
            }

            foreach (string keyword in new[] { "items", "additionalProperties", "unevaluatedProperties", "unevaluatedItems", "contains", "if", "then", "else", "not", "propertyNames" })
            {
                Register(schemaObject[keyword], baseUri);
            }
        }

        internal bool TryResolve(JsonNode currentResource, string reference, out JsonNode? schema, out JsonNode resource)
        {
            schema = null;
            resource = currentResource;
            if (!_resourceIds.TryGetValue(currentResource, out Uri? baseUri) ||
                !Uri.TryCreate(baseUri, reference, out Uri? targetUri) ||
                !_resources.TryGetValue(new UriBuilder(targetUri) { Fragment = string.Empty }.Uri, out JsonNode? targetResource) ||
                !TryResolveLocalReference(targetResource, targetUri.Fragment.Length == 0 ? "#" : targetUri.Fragment, out schema))
            {
                return false;
            }

            resource = targetResource;
            for (JsonNode? node = schema; node is not null; node = node.Parent)
            {
                if (_resourceIds.ContainsKey(node))
                {
                    resource = node;
                    break;
                }
            }

            return true;
        }
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
            return TryFindSchemaAnchor(schemaRoot, pointer, out resolvedSchema);
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

    private static bool TryFindSchemaAnchor(JsonNode? node, string anchor, out JsonNode? resolvedSchema, bool dynamicOnly = false, bool isResourceRoot = true)
    {
        resolvedSchema = null;
        if (node is not JsonObject schemaObject || (!isResourceRoot && schemaObject.ContainsKey("$id")))
        {
            return false;
        }

        if ((!dynamicOnly && schemaObject["$anchor"]?.GetValue<string>() == anchor) ||
            schemaObject["$dynamicAnchor"]?.GetValue<string>() == anchor)
        {
            resolvedSchema = node;
            return true;
        }

        foreach (string keyword in new[] { "$defs", "definitions", "properties", "patternProperties", "dependentSchemas" })
        {
            if (schemaObject[keyword] is JsonObject schemas)
            {
                foreach (KeyValuePair<string, JsonNode?> schema in schemas)
                {
                    if (TryFindSchemaAnchor(schema.Value, anchor, out resolvedSchema, dynamicOnly, isResourceRoot: false))
                    {
                        return true;
                    }
                }
            }
        }

        foreach (string keyword in new[] { "allOf", "anyOf", "oneOf", "prefixItems" })
        {
            if (schemaObject[keyword] is JsonArray schemas)
            {
                foreach (JsonNode? schema in schemas)
                {
                    if (TryFindSchemaAnchor(schema, anchor, out resolvedSchema, dynamicOnly, isResourceRoot: false))
                    {
                        return true;
                    }
                }
            }
        }

        foreach (string keyword in new[] { "items", "additionalProperties", "unevaluatedProperties", "unevaluatedItems", "contains", "if", "then", "else", "not", "propertyNames" })
        {
            if (TryFindSchemaAnchor(schemaObject[keyword], anchor, out resolvedSchema, dynamicOnly, isResourceRoot: false))
            {
                return true;
            }
        }

        return false;
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
