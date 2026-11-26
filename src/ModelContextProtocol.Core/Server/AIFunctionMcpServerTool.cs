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
    private readonly bool? _returnsCallToolResult;
    private readonly Type? _returnType;
    private readonly bool _restoreRequiredNulls;

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

        return CreateCore(
            AIFunctionFactory.Create(method, target, CreateAIFunctionFactoryOptions(method, options)),
            options,
            returnsCallToolResult: ReturnsCallToolResult(method));
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

        return CreateCore(
            AIFunctionFactory.Create(method, args =>
            {
                Debug.Assert(args.Services is RequestServiceProvider<CallToolRequestParams>, $"The service provider should be a {nameof(RequestServiceProvider<>)} for this method to work correctly.");
                return createTargetFunc(((RequestServiceProvider<CallToolRequestParams>)args.Services!).Request);
            }, CreateAIFunctionFactoryOptions(method, options)),
            options,
            returnsCallToolResult: ReturnsCallToolResult(method));
    }

    private static bool? ReturnsCallToolResult(MethodInfo method)
    {
        Type returnType = method.ReturnType;
        if (returnType.IsGenericType &&
            (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            returnType = returnType.GetGenericArguments()[0];
        }

        return returnType == typeof(CallToolResult) ? true :
            returnType.IsAssignableFrom(typeof(CallToolResult)) ? null : false;
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
    public static new AIFunctionMcpServerTool Create(AIFunction function, McpServerToolCreateOptions? options) =>
        CreateCore(function, options, returnsCallToolResult: null);

    private static AIFunctionMcpServerTool CreateCore(AIFunction function, McpServerToolCreateOptions? options, bool? returnsCallToolResult)
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

        return new AIFunctionMcpServerTool(
            function,
            tool,
            options?.Services,
            options?.Metadata ?? [],
            returnsCallToolResult,
            function.UnderlyingMethod?.ReturnType,
            options?.UseStructuredContent is true && options.OutputSchema is null && function.ReturnJsonSchema is not null);
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
    private AIFunctionMcpServerTool(
        AIFunction function,
        Tool tool,
        IServiceProvider? serviceProvider,
        IReadOnlyList<object> metadata,
        bool? returnsCallToolResult,
        Type? returnType,
        bool restoreRequiredNulls)
    {
        ValidateToolName(tool.Name);

        AIFunction = function;
        ProtocolTool = tool;

        _metadata = metadata;
        _returnsCallToolResult = returnsCallToolResult;
        _returnType = GetStructuredReturnType(returnType);
        _restoreRequiredNulls = restoreRequiredNulls &&
            function.JsonSerializerOptions.DefaultIgnoreCondition is JsonIgnoreCondition.WhenWritingNull;
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
    /// Schema 2020-12 document. The supplied schema is wrapped in
    /// <c>{"type":"object","properties":{"result":&lt;schema&gt;}}</c> only when this
    /// SDK tool wraps its structured content; otherwise its root type is normalized to
    /// <c>"object"</c>. When marshalling can return either ordinary values or explicit tool
    /// results, the schema accepts both shapes. Returns <paramref name="protocolTool"/> unchanged when there is no
    /// output schema. Callers must gate the call on the negotiated version — this method
    /// is unconditional; the gate lives at the emission site.
    /// </summary>
    internal Tool BuildLegacyWireProtocolTool(Tool protocolTool, bool hasCustomInvocation)
    {
        if (protocolTool.OutputSchema is not { } natural)
        {
            return protocolTool;
        }

        bool wrapValue = ShouldWrapValueForLegacyWire(ProtocolTool.OutputSchema ?? natural);
        bool? returnsCallToolResult = hasCustomInvocation ? null : _returnsCallToolResult;
        JsonElement legacyOutputSchema = wrapValue && returnsCallToolResult is not false
            ? TransformOutputSchemaForPossibleToolResult(natural, nullEnvelopeOnly: returnsCallToolResult is true)
            : TransformOutputSchemaForLegacyWire(natural, wrapValue);

        return new Tool
        {
            Name = protocolTool.Name,
            Title = protocolTool.Title,
            Description = protocolTool.Description,
            InputSchema = protocolTool.InputSchema,
            OutputSchema = legacyOutputSchema,
            Annotations = protocolTool.Annotations,
            Icons = protocolTool.Icons,
            Meta = protocolTool.Meta,
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

        if (result is CallToolResult callToolResult)
        {
            return callToolResult;
        }

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
    /// Transforms <paramref name="naturalSchema"/> into an object-typed schema for clients
    /// on protocol versions older than <c>"2026-07-28"</c>, adding a required
    /// <c>result</c> envelope only when <paramref name="wrapValue"/> is true. SEP-2106 clients
    /// (<c>"2026-07-28"</c>+) see the natural schema and never need this transform.
    /// The envelope decision follows the underlying SDK tool's schema so it matches invocation,
    /// even when a delegating wrapper expresses its schema differently.
    /// </summary>
    /// <param name="naturalSchema">The natural JSON Schema 2020-12 document.</param>
    /// <param name="wrapValue">Whether invocation wraps structured content in a result envelope.</param>
    internal static JsonElement TransformOutputSchemaForLegacyWire(JsonElement naturalSchema, bool wrapValue)
    {
        if (!wrapValue &&
            naturalSchema.ValueKind is JsonValueKind.Object &&
            naturalSchema.TryGetProperty("type", out JsonElement typeProperty) &&
            typeProperty.ValueKind is JsonValueKind.String &&
            typeProperty.GetString() is "object")
        {
            return naturalSchema;
        }

        JsonNode? schemaNode;
        if (!wrapValue)
        {
            JsonObject objSchema = CreateObjectWireSchema(naturalSchema);
            objSchema["$ref"] = "#/$defs/output";
            schemaNode = objSchema;
        }
        else
        {
            JsonNode? naturalNode = JsonSerializer.SerializeToNode(naturalSchema, McpJsonUtilities.JsonContext.Default.JsonElement);
            schemaNode = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["result"] = naturalNode
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

    private static JsonObject CreateObjectWireSchema(JsonElement naturalSchema)
    {
        JsonNode? naturalNode = JsonSerializer.SerializeToNode(naturalSchema, McpJsonUtilities.JsonContext.Default.JsonElement);
        RewriteRefPointers(naturalNode, "/$defs/output");
        return new JsonObject
        {
            ["type"] = "object",
            ["$defs"] = new JsonObject { ["output"] = naturalNode },
        };
    }

    private static JsonElement TransformOutputSchemaForPossibleToolResult(JsonElement naturalSchema, bool nullEnvelopeOnly)
    {
        const string outputReference = "#/$defs/output";
        JsonObject resultSchema = new() { ["$ref"] = outputReference };
        if (nullEnvelopeOnly)
        {
            // A null CallToolResult follows the ordinary result path.
            resultSchema["type"] = "null";
        }

        JsonObject schemaNode = CreateObjectWireSchema(naturalSchema);
        schemaNode["anyOf"] = new JsonArray
        {
            (JsonNode)new JsonObject { ["$ref"] = outputReference },
            (JsonNode)new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["result"] = resultSchema,
                },
                ["required"] = new JsonArray { (JsonNode)"result" },
            },
        };
        return JsonSerializer.Deserialize(schemaNode, McpJsonUtilities.JsonContext.Default.JsonElement);
    }

    /// <summary>
    /// Rebases root-relative schema references when moving a schema, without changing literal
    /// instance data or references inside resources that declare their own identifier.
    /// </summary>
    private static void RewriteRefPointers(JsonNode? node, string prefix = "/properties/result")
    {
        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                RewriteRefPointers(item, prefix);
            }

            return;
        }

        if (node is JsonObject obj)
        {
            if (obj["$id"] is JsonValue idNode &&
                idNode.TryGetValue<string>(out string? id) && !string.IsNullOrEmpty(id))
            {
                return;
            }

            RewriteRefProperty(obj, "$ref", prefix);
            RewriteRefProperty(obj, "$dynamicRef", prefix);

            foreach (var property in obj)
            {
                switch (property.Key)
                {
                    case "$defs":
                    case "definitions":
                    case "properties":
                    case "patternProperties":
                    case "dependentSchemas":
                        if (property.Value is JsonObject schemas)
                        {
                            foreach (var schema in schemas)
                            {
                                RewriteRefPointers(schema.Value, prefix);
                            }
                        }
                        break;

                    case "allOf":
                    case "anyOf":
                    case "oneOf":
                    case "prefixItems":
                    case "items":
                    case "additionalProperties":
                    case "unevaluatedProperties":
                    case "unevaluatedItems":
                    case "propertyNames":
                    case "contains":
                    case "not":
                    case "if":
                    case "then":
                    case "else":
                    case "contentSchema":
                        RewriteRefPointers(property.Value, prefix);
                        break;
                }
            }
        }
    }

    private static void RewriteRefProperty(JsonObject schema, string keyword, string prefix)
    {
        if (schema[keyword] is JsonValue refNode && refNode.TryGetValue<string>(out string? reference))
        {
            if (reference is "" or "#")
            {
                schema[keyword] = "#" + prefix;
            }
            else if (reference?.StartsWith("#", StringComparison.Ordinal) is true &&
                Uri.UnescapeDataString(reference.Substring(1)).StartsWith("/", StringComparison.Ordinal))
            {
                schema[keyword] = "#" + prefix + reference.Substring(1);
            }
        }
    }

    private static Type? GetStructuredReturnType(Type? returnType)
    {
        if (returnType is null)
        {
            return null;
        }

        if (returnType.IsGenericType &&
            (returnType.GetGenericTypeDefinition() == typeof(Task<>) ||
             returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            returnType = returnType.GetGenericArguments()[0];
        }

        return returnType;
    }

    private static JsonElement RestoreRequiredNullableProperties(
        JsonElement serializedResult,
        object originalResult,
        Type returnType,
        JsonElement outputSchema,
        JsonSerializerOptions serializerOptions)
    {
        List<(List<string> ParentPath, string Name)> missingProperties = [];
        HashSet<string> seenPaths = new(StringComparer.Ordinal);
        CollectMissingRequiredNulls(
            serializedResult,
            originalResult,
            returnType,
            outputSchema,
            outputSchema,
            serializerOptions,
            [],
            string.Empty,
            missingProperties,
            seenPaths,
            0);

        if (missingProperties.Count is 0 || JsonNode.Parse(serializedResult.GetRawText()) is not JsonObject resultNode)
        {
            return serializedResult;
        }

        foreach ((List<string> parentPath, string name) in missingProperties)
        {
            JsonObject? parent = resultNode;
            foreach (string segment in parentPath)
            {
                if (parent is null || !parent.TryGetPropertyValue(segment, out JsonNode? child) || child is not JsonObject childObject)
                {
                    parent = null;
                    break;
                }

                parent = childObject;
            }

            if (parent is not null && !parent.ContainsKey(name))
            {
                parent[name] = null;
            }
        }

        return JsonSerializer.SerializeToElement(resultNode, McpJsonUtilities.JsonContext.Default.JsonNode);
    }

    private static void CollectMissingRequiredNulls(
        JsonElement serializedObject,
        object sourceObject,
        Type sourceType,
        JsonElement schema,
        JsonElement schemaRoot,
        JsonSerializerOptions serializerOptions,
        List<string> instancePath,
        string instancePointer,
        List<(List<string> ParentPath, string Name)> missingProperties,
        HashSet<string> seenPaths,
        int depth)
    {
        if (depth >= 64 || serializedObject.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        JsonTypeInfo typeInfo;
        try
        {
            typeInfo = serializerOptions.GetTypeInfo(sourceType);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return;
        }

        if (typeInfo.Kind is not JsonTypeInfoKind.Object)
        {
            return;
        }

        OutputSchemaObjectShape shape = new();
        CollectObjectSchemaShape(schema, schemaRoot, shape, new(StringComparer.Ordinal), 0);
        if (shape.Properties.Count is 0)
        {
            return;
        }

        Dictionary<string, JsonPropertyInfo> properties = new(StringComparer.Ordinal);
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (!properties.ContainsKey(property.Name))
            {
                properties.Add(property.Name, property);
            }
        }

        foreach (string requiredName in shape.Required)
        {
            if (serializedObject.TryGetProperty(requiredName, out _) ||
                !properties.TryGetValue(requiredName, out JsonPropertyInfo? property) ||
                property.Get is null ||
                property.IsExtensionData ||
                property.CustomConverter is not null ||
                property.ShouldSerialize is not null ||
                IsExplicitlyIgnored(property) ||
                !shape.Properties.TryGetValue(requiredName, out List<JsonElement>? propertySchemas) ||
                propertySchemas.Count is 0 ||
                propertySchemas.Any(propertySchema => !SchemaAllowsNull(propertySchema, schemaRoot, new(StringComparer.Ordinal), 0)))
            {
                continue;
            }

            if (!TryGetPropertyValue(property, sourceObject, out object? value) || value is not null)
            {
                continue;
            }

            string propertyPointer = AppendJsonPointer(instancePointer, requiredName);
            if (seenPaths.Add(propertyPointer))
            {
                missingProperties.Add((new List<string>(instancePath), requiredName));
            }
        }

        foreach (KeyValuePair<string, List<JsonElement>> propertySchema in shape.Properties)
        {
            if (!serializedObject.TryGetProperty(propertySchema.Key, out JsonElement serializedValue) ||
                serializedValue.ValueKind is not JsonValueKind.Object ||
                !properties.TryGetValue(propertySchema.Key, out JsonPropertyInfo? property) ||
                property.Get is null ||
                property.IsExtensionData ||
                property.CustomConverter is not null ||
                property.ShouldSerialize is not null ||
                IsExplicitlyIgnored(property) ||
                !TryGetPropertyValue(property, sourceObject, out object? value) ||
                value is null)
            {
                continue;
            }

            instancePath.Add(propertySchema.Key);
            string childPointer = AppendJsonPointer(instancePointer, propertySchema.Key);
            foreach (JsonElement childSchema in propertySchema.Value)
            {
                CollectMissingRequiredNulls(
                    serializedValue,
                    value,
                    property.PropertyType,
                    childSchema,
                    schemaRoot,
                    serializerOptions,
                    instancePath,
                    childPointer,
                    missingProperties,
                    seenPaths,
                    depth + 1);
            }

            instancePath.RemoveAt(instancePath.Count - 1);
        }
    }

    private static bool TryGetPropertyValue(JsonPropertyInfo property, object source, out object? value)
    {
        try
        {
            value = property.Get!(source);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            value = null;
            return false;
        }
    }

    private static bool IsExplicitlyIgnored(JsonPropertyInfo property) =>
        property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
            .OfType<JsonIgnoreAttribute>()
            .Any(attribute => attribute.Condition is not JsonIgnoreCondition.Never) is true;

    private static string AppendJsonPointer(string parent, string name) =>
        string.Concat(parent, "/", name.Replace("~", "~0").Replace("/", "~1"));

    private static void CollectObjectSchemaShape(
        JsonElement schema,
        JsonElement schemaRoot,
        OutputSchemaObjectShape shape,
        HashSet<string> visitedReferences,
        int depth)
    {
        if (depth >= 64 || schema.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("$ref", out JsonElement referenceElement) &&
            referenceElement.ValueKind is JsonValueKind.String &&
            referenceElement.GetString() is { } reference &&
            visitedReferences.Add(reference) &&
            TryResolveSchemaReference(schemaRoot, reference, out JsonElement referencedSchema))
        {
            CollectObjectSchemaShape(referencedSchema, schemaRoot, shape, visitedReferences, depth + 1);
        }

        if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement name in required.EnumerateArray())
            {
                if (name.ValueKind is JsonValueKind.String && name.GetString() is { } requiredName)
                {
                    shape.Required.Add(requiredName);
                }
            }
        }

        if (schema.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind is JsonValueKind.Object)
        {
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                if (!shape.Properties.TryGetValue(property.Name, out List<JsonElement>? schemas))
                {
                    schemas = [];
                    shape.Properties.Add(property.Name, schemas);
                }

                schemas.Add(property.Value);
            }
        }

        if (schema.TryGetProperty("allOf", out JsonElement allOf) && allOf.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement child in allOf.EnumerateArray())
            {
                CollectObjectSchemaShape(child, schemaRoot, shape, visitedReferences, depth + 1);
            }
        }
    }

    private static bool SchemaAllowsNull(JsonElement schema, JsonElement schemaRoot, HashSet<string> visitedReferences, int depth)
    {
        if (depth >= 64 || schema.ValueKind is not JsonValueKind.Object)
        {
            return schema.ValueKind is not JsonValueKind.False;
        }

        bool allowsNull = true;
        if (schema.TryGetProperty("$ref", out JsonElement referenceElement) &&
            referenceElement.ValueKind is JsonValueKind.String &&
            referenceElement.GetString() is { } reference)
        {
            if (!visitedReferences.Add(reference) || !TryResolveSchemaReference(schemaRoot, reference, out JsonElement referencedSchema))
            {
                return false;
            }

            allowsNull &= SchemaAllowsNull(referencedSchema, schemaRoot, visitedReferences, depth + 1);
        }

        if (schema.TryGetProperty("type", out JsonElement type))
        {
            allowsNull &= type.ValueKind switch
            {
                JsonValueKind.String => type.GetString() is "null",
                JsonValueKind.Array => type.EnumerateArray().Any(item => item.ValueKind is JsonValueKind.String && item.GetString() is "null"),
                _ => false,
            };
        }

        if (schema.TryGetProperty("const", out JsonElement constant))
        {
            allowsNull &= constant.ValueKind is JsonValueKind.Null;
        }

        if (schema.TryGetProperty("enum", out JsonElement enumValues) && enumValues.ValueKind is JsonValueKind.Array)
        {
            allowsNull &= enumValues.EnumerateArray().Any(value => value.ValueKind is JsonValueKind.Null);
        }

        if (schema.TryGetProperty("allOf", out JsonElement allOf) && allOf.ValueKind is JsonValueKind.Array)
        {
            allowsNull &= allOf.EnumerateArray().All(child => SchemaAllowsNull(child, schemaRoot, new(visitedReferences, StringComparer.Ordinal), depth + 1));
        }

        if (schema.TryGetProperty("anyOf", out JsonElement anyOf) && anyOf.ValueKind is JsonValueKind.Array)
        {
            allowsNull &= anyOf.EnumerateArray().Any(child => SchemaAllowsNull(child, schemaRoot, new(visitedReferences, StringComparer.Ordinal), depth + 1));
        }

        if (schema.TryGetProperty("oneOf", out JsonElement oneOf) && oneOf.ValueKind is JsonValueKind.Array)
        {
            allowsNull &= oneOf.EnumerateArray().Count(child => SchemaAllowsNull(child, schemaRoot, new(visitedReferences, StringComparer.Ordinal), depth + 1)) is 1;
        }

        return allowsNull;
    }

    private static bool TryResolveSchemaReference(JsonElement schemaRoot, string reference, out JsonElement resolvedSchema)
    {
        resolvedSchema = default;
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return false;
        }

        string pointer = Uri.UnescapeDataString(reference.Substring(2));
        JsonElement current = schemaRoot;
        foreach (string rawSegment in pointer.Split('/'))
        {
            string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            if (current.ValueKind is JsonValueKind.Object && current.TryGetProperty(segment, out JsonElement property))
            {
                current = property;
            }
            else if (current.ValueKind is JsonValueKind.Array && int.TryParse(segment, out int index) && index >= 0 && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                return false;
            }
        }

        resolvedSchema = current;
        return true;
    }

    private sealed class OutputSchemaObjectShape
    {
        public HashSet<string> Required { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<JsonElement>> Properties { get; } = new(StringComparer.Ordinal);
    }

    private JsonElement? CreateStructuredResponse(object? aiFunctionResult, string? negotiatedProtocolVersion)
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

        if (_restoreRequiredNulls &&
            _returnType is not null &&
            AIFunction.ReturnJsonSchema is { } generatedSchema &&
            aiFunctionResult is not (JsonElement or JsonNode or CallToolResult) &&
            aiFunctionResult is not null &&
            elementResult is { ValueKind: JsonValueKind.Object } serializedObject)
        {
            elementResult = RestoreRequiredNullableProperties(
                serializedObject,
                aiFunctionResult,
                _returnType,
                generatedSchema,
                AIFunction.JsonSerializerOptions);
        }

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
