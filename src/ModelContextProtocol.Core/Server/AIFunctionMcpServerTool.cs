using Microsoft.Extensions.AI;
using Json.Schema;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    private readonly bool? _returnsCallToolResult;
    private OutputSchemaCache? _outputSchema;
    private static readonly ConditionalWeakTable<JsonTypeInfo, SourceObjectContract> s_sourceContracts = new();
    private static readonly ConditionalWeakTable<JsonSchema, SchemaEvaluationCost> s_schemaEvaluationCosts = new();
    private static readonly TimeSpan s_recoveryRegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly JsonElement s_propertyNameInstance = JsonSerializer.SerializeToElement(string.Empty, McpJsonUtilities.JsonContext.Default.String);
    [ThreadStatic]
    private static SourceMemberSerializationState? t_serializingSourceMember;

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

        return new AIFunctionMcpServerTool(function, tool, options?.Services, options?.Metadata ?? [], returnsCallToolResult);
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
        AIFunction function, Tool tool, IServiceProvider? serviceProvider, IReadOnlyList<object> metadata, bool? returnsCallToolResult)
    {
        ValidateToolName(tool.Name);

        AIFunction = function;
        ProtocolTool = tool;
        _metadata = metadata;
        _returnsCallToolResult = returnsCallToolResult;
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

        JsonElement? recoverySchema = GetRecoverySchema(request.MatchedPrimitive as McpServerTool);
        JsonElement? structuredContent = CreateStructuredResponse(result, request.Server.NegotiatedProtocolVersion, recoverySchema);
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

    private JsonElement? CreateStructuredResponse(
        object? aiFunctionResult, string? negotiatedProtocolVersion, JsonElement? recoverySchema)
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

        elementResult = AddMissingRequiredNullableProperties(elementResult, aiFunctionResult, recoverySchema);

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
        object? originalResult,
        JsonElement? recoverySchema)
    {
        if (elementResult is not { } result ||
            recoverySchema is not { } outputSchema ||
            result.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return elementResult;
        }

        OutputSchemaCache cachedSchema = GetOutputSchema(outputSchema);
        JsonNode? schemaNode = cachedSchema.Node;
        JsonSchema? schema = cachedSchema.Schema;
        if (schemaNode is null || schema is null)
        {
            return elementResult;
        }

        RepairTraversalBudget budget = new();
        try
        {
            if (IsSchemaValid(schema, result, budget) || HasDuplicatePropertyNames(result))
            {
                return elementResult;
            }
            budget.SpendCopy(result);
            JsonNode? resultNode = JsonSerializer.SerializeToNode(result, McpJsonUtilities.JsonContext.Default.JsonElement);
            if (resultNode is null)
            {
                return elementResult;
            }

            JsonNode? repairedResult = RepairStructuredResult(
                resultNode,
                schemaNode,
                schema,
                originalResult is null ? null : new SourceRecoveryContext(
                    originalResult, AIFunction.JsonSerializerOptions.GetTypeInfo(typeof(object)),
                    fallbackType: GetDeclaredReturnType(originalResult)),
                AIFunction.JsonSerializerOptions,
                traversalBudget: budget);

            if (repairedResult is null)
            {
                return elementResult;
            }

            budget.SpendCopy(repairedResult);
            return JsonSerializer.SerializeToElement(repairedResult, McpJsonUtilities.JsonContext.Default.JsonNode);
        }
        catch (Exception exception) when (exception is RecoveryBudgetExceededException or RegexMatchTimeoutException)
        {
            return elementResult;
        }
    }

    private JsonElement? GetRecoverySchema(McpServerTool? matchedTool)
    {
        McpServerTool schemaTool = matchedTool ?? this;
        McpServerTool current = schemaTool;
        while (current is DelegatingMcpServerTool delegating)
        {
            if (delegating.HasCustomInvocation)
            {
                // A transforming wrapper's schema describes its result, not the inner CLR value.
                schemaTool = delegating.InnerTool;
            }
            current = delegating.InnerTool;
        }

        return ReferenceEquals(current, this) ? schemaTool.ProtocolTool.OutputSchema : ProtocolTool.OutputSchema;
    }

    private Type? GetDeclaredReturnType(object result)
    {
        Type? returnType = AIFunction.UnderlyingMethod?.ReturnType;
        if (returnType is { IsGenericType: true } &&
            (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            returnType = returnType.GetGenericArguments()[0];
        }

        if (returnType is null || returnType == typeof(object) || !returnType.IsInstanceOfType(result))
        {
            return null;
        }

        return returnType;
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
        List<RepairCondition>? inheritedConditions = null,
        bool includeValidAlternatives = false)
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
        traversalBudget ??= new();
        if (references is null)
        {
            traversalBudget.SpendCopy(schemaDocument, immutable: true);
            references = new(schemaDocument, schema.BaseUri);
        }
        // Cache predicates independently of their callers. Conditional expectations and
        // instance paths remain on each group and are checked on every tentative repair.
        Dictionary<(JsonNode Schema, string Scope), JsonSchema?> branchSchemas = traversalBudget.BranchSchemas;
        Dictionary<string, JsonNode> conjunctiveSchemas = new(StringComparer.Ordinal);
        List<RepairCandidateGroup> unevaluatedGroups = [];
        Dictionary<string, RepairCandidate> unevaluatedCandidates = new(StringComparer.Ordinal);
        SchemaReferenceResolver? evaluationReferences = null;
        bool skippedValidAlternatives = false;

        HashSet<(string Path, string Schema)> GetUnevaluatedApplications(JsonNode instance)
        {
            HashSet<(string, string)> applications = [];
            try
            {
                if (evaluationReferences is null)
                {
                    traversalBudget.SpendCopy(schema.Root.Source);
                    evaluationReferences = new(
                        JsonSerializer.SerializeToNode(schema.Root.Source, McpJsonUtilities.JsonContext.Default.JsonElement)!,
                        schema.BaseUri);
                }
                traversalBudget.SpendCopy(instance);
                JsonElement element = JsonSerializer.SerializeToElement(instance, McpJsonUtilities.JsonContext.Default.JsonNode);
                traversalBudget.SpendEvaluation(schema, element, detailed: true);
                Stack<EvaluationResults> results = new();
                results.Push(schema.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical }));
                while (results.Count > 0)
                {
                    traversalBudget.Spend(1);
                    EvaluationResults evaluation = results.Pop();
                    string path = evaluation.EvaluationPath.ToString();
                    // Evaluation paths retain caller provenance even when SchemaLocation is imprecise across references.
                    if ((path.EndsWith("/unevaluatedProperties", StringComparison.Ordinal) ||
                        path.EndsWith("/unevaluatedItems", StringComparison.Ordinal)) &&
                        evaluationReferences.TryResolveEvaluationPath(path, out Uri? application))
                    {
                        applications.Add((evaluation.InstanceLocation.ToString(), application!.AbsoluteUri));
                    }

                    if (evaluation.Details is { } details)
                    {
                        traversalBudget.Spend(details.Count);
                        foreach (EvaluationResults detail in details)
                        {
                            results.Push(detail);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or JsonSchemaException or RefResolutionException or InvalidOperationException or ArgumentException)
            {
                applications.Clear();
            }

            return applications;
        }

        JsonNode FinalizeRepair(JsonNode instance)
        {
            if (unevaluatedGroups.Count == 0)
            {
                return instance;
            }

            HashSet<(string Path, string Schema)> applications = GetUnevaluatedApplications(instance);
            HashSet<string> checkedKeys = new(StringComparer.Ordinal);
            foreach (RepairCandidateGroup group in unevaluatedGroups)
            {
                if (group.Unevaluated is not { } origin ||
                    applications.Contains((origin.InstancePath, references.GetSchemaUri(origin.Schema).AbsoluteUri)))
                {
                    continue;
                }

                foreach (string key in group.Keys)
                {
                    if (!checkedKeys.Add(key))
                    {
                        continue;
                    }
                    if (checkedKeys.Count > maxCandidateSetEvaluations)
                    {
                        return instance;
                    }
                    traversalBudget.Spend(1);

                    if (!unevaluatedCandidates.TryGetValue(key, out RepairCandidate? candidate))
                    {
                        continue;
                    }

                    string parentPath = candidate.ParentPath;
                    string name = candidate.PropertyName;
                    if ((TryGetNodeAtPath(resultNode, parentPath, out JsonNode? originalParent) &&
                        originalParent is JsonObject originalObject && originalObject.ContainsKey(name)) ||
                        !TryGetNodeAtPath(instance, parentPath, out JsonNode? parent) ||
                        parent is not JsonObject objectParent || !objectParent.TryGetPropertyValue(name, out JsonNode? value))
                    {
                        continue;
                    }

                    objectParent.Remove(name);
                    if (!IsRepairValid(instance))
                    {
                        objectParent[name] = value;
                    }
                }
            }

            return instance;
        }

        JsonSchema? GetBranchSchema(JsonNode branch, List<JsonNode> scope)
        {
            var schemaKey = (branch, references.GetScopeKey(scope));
            if (!branchSchemas.TryGetValue(schemaKey, out JsonSchema? branchSchema))
            {
                traversalBudget.SpendSchema(schemaDocument);
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
                if (references.SupplementalSchemas.Contains(branch))
                {
                    traversalBudget.SpendSchema(branch);
                    branchDocument["$defs"]!["scope"] = branch.DeepClone();
                }
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
                    IsCandidateValid(conditionSchema, ProjectConditionInstance(conditionInstance, condition.Schema, traversalBudget)) != condition.Expected)
                {
                    return false;
                }
            }

            return true;
        }

        bool IsCandidateValid(JsonSchema candidateSchema, JsonNode? instance)
        {
            traversalBudget.SpendCopy(instance);
            return IsSchemaValid(candidateSchema, instance, traversalBudget);
        }

        bool IsRepairValid(JsonNode instance) =>
            IsCandidateValid(schema, instance) &&
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
            HashSet<(string Path, string Schema)>? applications = null;
            SchemaTraversal traversal = new(traversalBudget, (subschema, path) =>
                    (applications ??= GetUnevaluatedApplications(instance)).Contains((path, references.GetSchemaUri(subschema).AbsoluteUri)),
                    (subschema, value, scope) => GetBranchSchema(subschema, scope) is { } compiled && IsCandidateValid(compiled, value),
                    includeValidAlternatives);
            CollectSchemaContexts(
                instance, schemaNode, references.GetResourceRoot(schemaNode), string.Empty, contexts, traversal,
                groups, references, dynamicScope);
            skippedValidAlternatives |= traversal.SkippedValidAlternatives;
            traversalBudget.Spend(groups.Count);
            unevaluatedGroups.AddRange(groups.Where(static group => group.Unevaluated is not null));
            if (inheritedConditions is { Count: > 0 })
            {
                foreach (RepairCandidateGroup group in groups)
                {
                    traversalBudget.Spend(inheritedConditions.Count + group.Conditions.Count);
                }
                groups = groups.Select(group => group with
                {
                    Conditions = [.. inheritedConditions, .. group.Conditions],
                }).ToList();
            }

            // Conjunctive child schemas must qualify together, not as independent alternatives.
            var envelopes = groups.Where(static group => group.EnclosesInstance)
                .GroupBy(group => (group.InstancePath, Scope: references.HasDynamicReferences ? references.GetScopeKey(group.DynamicScope) : string.Empty))
                .ToDictionary(static bucket => bucket.Key, static bucket => bucket.ToList());
            HashSet<RepairCandidateGroup> combinedEnvelopes = [];
            List<RepairCandidateGroup> qualifiedGroups = [];
            foreach (RepairCandidateGroup group in groups)
            {
                if (combinedEnvelopes.Contains(group))
                {
                    continue;
                }

                if (!group.EnclosesInstance)
                {
                    qualifiedGroups.Add(group);
                    continue;
                }

                var key = (group.InstancePath, references.HasDynamicReferences ? references.GetScopeKey(group.DynamicScope) : string.Empty);
                List<RepairCandidateGroup> conjuncts = envelopes[key].Where(other =>
                {
                    traversalBudget.Spend(1 + other.Choices.Count + other.Conditions.Count + (other.Alternatives?.Count ?? 0));
                    return other.Choices.SequenceEqual(group.Choices) && other.Conditions.SequenceEqual(group.Conditions) &&
                        (other.Alternatives ?? []).SequenceEqual(group.Alternatives ?? []);
                }).ToList();
                if (conjuncts.Count == 1)
                {
                    qualifiedGroups.Add(group);
                    continue;
                }

                string schemaKey = string.Join("\n", conjuncts.Select(item => references.GetSchemaUri(item.Schema).AbsoluteUri)
                    .Distinct(StringComparer.Ordinal).OrderBy(static uri => uri, StringComparer.Ordinal));
                if (!conjunctiveSchemas.TryGetValue(schemaKey, out JsonNode? conjunction))
                {
                    JsonArray clauses = [];
                    foreach (string uri in schemaKey.Split('\n'))
                    {
                        traversalBudget.SpendText(uri);
                        clauses.Add((JsonNode)new JsonObject { ["$ref"] = uri });
                    }
                    conjunction = new JsonObject { ["$id"] = "https://mcp.invalid/recovery/" + Guid.NewGuid().ToString("N"), ["allOf"] = clauses };
                    references.AddSupplementalSchema(conjunction);
                    conjunctiveSchemas.Add(schemaKey, conjunction);
                }

                qualifiedGroups.Add(group with
                {
                    Schema = conjunction,
                    Keys = CombineKeys(direct: false),
                    DirectKeys = CombineKeys(direct: true),
                });
                combinedEnvelopes.UnionWith(conjuncts);

                HashSet<string> CombineKeys(bool direct)
                {
                    HashSet<string> keys = new(StringComparer.Ordinal);
                    foreach (RepairCandidateGroup conjunct in conjuncts)
                    {
                        HashSet<string> source = direct ? conjunct.DirectKeys : conjunct.Keys;
                        traversalBudget.Spend(source.Count);
                        keys.UnionWith(source);
                    }
                    return keys;
                }
            }
            groups = qualifiedGroups;

            List<RepairCandidate> candidates = CreateRepairCandidates(
                instance, originalResult, serializerOptions, contexts, traversalBudget);
            if (unevaluatedGroups.Count > 0)
            {
                foreach (RepairCandidate candidate in candidates)
                {
                    unevaluatedCandidates[candidate.Key] = candidate;
                }
            }
            if (candidates.Count == 0)
            {
                return null;
            }

            foreach (RepairCandidateGroup group in groups)
            {
                traversalBudget.Spend(group.Keys.Count);
            }
            traversalBudget.Spend(candidates.Count);
            HashSet<string> grouped = new(groups.SelectMany(static group => group.Keys), StringComparer.Ordinal);
            Dictionary<string, RepairCandidate> candidatesByKey = candidates.ToDictionary(static candidate => candidate.Key, StringComparer.Ordinal);
            List<RepairCandidate> ungrouped = candidates.Where(candidate => !grouped.Contains(candidate.Key)).ToList();
            Dictionary<(JsonNode Applicator, string Path), JsonNode> exclusiveChoices = new();
            Dictionary<RepairCondition, bool?> stableConditions = new();
            RepairMutationPaths? mutations = null;
            traversalBudget.SpendCopy(instance);
            JsonNode combinedResult = instance.DeepClone();
            bool hasCombinedRepairs = false;
            HashSet<string> completedInstances = new(StringComparer.Ordinal);
            foreach (RepairCandidate candidate in ungrouped)
            {
                ApplyRepairCandidate(combinedResult, candidate, traversalBudget);
                hasCombinedRepairs = true;
            }

            foreach (RepairCandidateGroup group in groups)
            {
                // A guard untouched by every candidate cannot be rescued by searching subsets.
                bool rejected = false;
                foreach (RepairCondition condition in group.Conditions)
                {
                    if (!stableConditions.TryGetValue(condition, out bool? matches))
                    {
                        matches = IsConditionUnaffected(condition, mutations ??= new(candidates, traversalBudget), traversalBudget)
                            ? AreConditionsValid(instance, [condition])
                            : null;
                        stableConditions.Add(condition, matches);
                    }
                    if (matches == false)
                    {
                        rejected = true;
                        break;
                    }
                }
                if (rejected)
                {
                    continue;
                }

                string enclosingPath = group.InstancePath;
                while (enclosingPath.Length > 0 && !completedInstances.Contains(enclosingPath))
                {
                    enclosingPath = enclosingPath.Substring(0, enclosingPath.LastIndexOf('/'));
                }
                if (enclosingPath.Length > 0)
                {
                    continue;
                }

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
                    traversalBudget.Spend(1);
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
                    traversalBudget.SpendCopy(currentBranch);
                    JsonNode branchResult = currentBranch.DeepClone();
                    foreach (RepairCandidate candidate in branchCandidates)
                    {
                        ApplyRepairCandidate(branchResult, candidate with { ParentPath = candidate.ParentPath.Substring(group.InstancePath.Length) }, traversalBudget);
                    }

                    bool branchIsValid = IsCandidateValid(branchSchema, branchResult);
                    if (!branchIsValid && (branchCandidates.Count > 0 || !includeDescendants) && depth + 1 < remainingDepth &&
                        !(group.InstancePath.Length == 0 && ReferenceEquals(group.Schema, schemaNode) &&
                            references.GetScopeKey(group.DynamicScope) ==
                                references.GetScopeKey(dynamicScope ?? [references.GetResourceRoot(schemaNode)])))
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
                                }).ToList(), includeValidAlternatives) is JsonNode repairedBranch)
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
                                    IsCandidateValid(ancestorSchema, ancestorInstance);
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
                        if (group.EnclosesInstance)
                        {
                            completedInstances.Add(group.InstancePath);
                        }
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

                traversalBudget.Spend(ungrouped.Count);
                List<RepairCandidate> branchCandidates = [.. ungrouped];
                foreach (string key in group.Keys)
                {
                    traversalBudget.Spend(1);
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
                traversalBudget.Spend(1);
                fallback.Enqueue(new(instance, [candidates[i]], depth + 1));
            }

            for (int i = 0; i < candidates.Count && fallback.Count < maxCandidateSetEvaluations; i++)
            {
                for (int j = i + 1; j < candidates.Count && fallback.Count < maxCandidateSetEvaluations; j++)
                {
                    traversalBudget.Spend(2);
                    fallback.Enqueue(new(instance, [candidates[i], candidates[j]], depth + 1));
                }
            }

            return null;
        }

        if (EnqueueRepairs(resultNode, 0) is JsonNode composedResult)
        {
            return FinalizeRepair(composedResult);
        }
        for (int evaluations = 0; evaluations < maxCandidateSetEvaluations && (pending.Count > 0 || fallback.Count > 0);)
        {
            RepairAttempt attempt = pending.Count > 0 ? pending.Dequeue() : fallback.Dequeue();
            traversalBudget.SpendCopy(attempt.Result);
            JsonNode candidateResult = attempt.Result.DeepClone();
            foreach (RepairCandidate candidate in attempt.Candidates)
            {
                ApplyRepairCandidate(candidateResult, candidate, traversalBudget);
            }

            traversalBudget.SpendCopy(candidateResult);
            if (!visited.Add(candidateResult.ToJsonString()))
            {
                continue;
            }

            evaluations++;
            if (IsRepairValid(candidateResult))
            {
                return FinalizeRepair(candidateResult);
            }

            if (EnqueueRepairs(candidateResult, attempt.Depth) is JsonNode repairedResult)
            {
                return FinalizeRepair(repairedResult);
            }
        }

        // A valid composition can still need another branch's evaluated-property annotations.
        // Prefer ordinary recovery before considering changes to already-valid choices.
        return !includeValidAlternatives && skippedValidAlternatives &&
            s_schemaEvaluationCosts.GetValue(schema, static compiled => new(compiled.Root)).HasUnevaluated
            ? RepairStructuredResult(
                resultNode, schemaNode, schema, originalResult, serializerOptions, remainingDepth,
                schemaDocument, references, dynamicScope, traversalBudget, inheritedConditions,
                includeValidAlternatives: true)
            : null;
    }

    private static bool TrySetSchemaRegexTimeouts(JsonNode source, JsonSchemaNode root)
    {
        SchemaReferenceResolver references = new(source, root.BaseUri);
        HashSet<JsonSchemaNode> visited = [root];
        Stack<JsonSchemaNode> pending = new();
        pending.Push(root);
        List<JsonSchemaNode> nodes = [];
        while (pending.Count > 0)
        {
            JsonSchemaNode node = pending.Pop();
            nodes.Add(node);
            foreach (KeywordData keyword in node.Keywords)
            {
                // Do not borrow mutable nodes or unbounded regexes from a global registry.
                if (keyword.Handler.Name is "$ref" or "$dynamicRef" or "$recursiveRef" &&
                    !references.CanResolveReference(node.BaseUri, keyword.RawValue.GetString()!))
                {
                    return false;
                }
                foreach (JsonSchemaNode child in keyword.Subschemas)
                {
                    if (visited.Add(child))
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        foreach (JsonSchemaNode node in nodes)
        {
            foreach (KeywordData keyword in node.Keywords)
            {
                if (keyword.Handler.Name == "pattern" && keyword.Value is Regex pattern)
                {
                    keyword.Value = new Regex(pattern.ToString(), pattern.Options, s_recoveryRegexTimeout);
                }
                else if (keyword.Handler.Name == "patternProperties" && keyword.Value is Dictionary<string, Regex> patterns)
                {
                    Dictionary<string, Regex> bounded = new(patterns.Count, patterns.Comparer);
                    foreach (KeyValuePair<string, Regex> entry in patterns)
                    {
                        bounded.Add(entry.Key, new Regex(entry.Value.ToString(), entry.Value.Options, s_recoveryRegexTimeout));
                    }
                    keyword.Value = bounded;
                }
            }
        }
        return true;
    }

    private static bool TryBuildJsonSchema(JsonNode schemaNode, out JsonSchema? schema)
    {
        try
        {
            JsonElement schemaElement = JsonSerializer.SerializeToElement(schemaNode, McpJsonUtilities.JsonContext.Default.JsonNode);
            schema = JsonSchema.Build(schemaElement, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            if (!TrySetSchemaRegexTimeouts(schemaNode, schema.Root))
            {
                schema = null;
                return false;
            }
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

    private static bool IsSchemaValid(JsonSchema schema, JsonNode? instance, RepairTraversalBudget budget)
    {
        try
        {
            JsonElement instanceElement = JsonSerializer.SerializeToElement(instance, McpJsonUtilities.JsonContext.Default.JsonNode);
            return IsSchemaValid(schema, instanceElement, budget);
        }
        catch (Exception ex) when (ex is JsonException or JsonSchemaException or RefResolutionException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSchemaValid(JsonSchema schema, JsonElement instance, RepairTraversalBudget budget)
    {
        try
        {
            budget.SpendEvaluation(schema, instance);
            return schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.Flag }).IsValid;
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
        Dictionary<string, SchemaObjectContext> contexts,
        RepairTraversalBudget budget)
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
                budget.SpendText(contextEntry.Key);
                budget.SpendText(propertyName);
                if (parentObject.ContainsKey(propertyName))
                {
                    continue;
                }

                string candidateKey = AppendJsonPointer(contextEntry.Key, propertyName);
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

    private static void ApplyRepairCandidate(JsonNode resultNode, RepairCandidate candidate, RepairTraversalBudget budget)
    {
        if (TryGetNodeAtPath(resultNode, candidate.ParentPath, out JsonNode? parentNode) && parentNode is JsonObject parentObject)
        {
            budget.SpendCopy(candidate.Value);
            parentObject[candidate.PropertyName] = candidate.Value?.DeepClone();
        }
    }

    private sealed class SchemaObjectContext
    {
        internal HashSet<string> Required { get; } = new(StringComparer.Ordinal);
    }

    private sealed record RepairCandidate(string ParentPath, string PropertyName, JsonNode? Value)
    {
        internal string Key => AppendJsonPointer(ParentPath, PropertyName);
    }

    private sealed record RepairAttempt(JsonNode Result, List<RepairCandidate> Candidates, int Depth);

    private sealed record RepairExclusiveChoice(
        JsonNode Applicator, JsonNode Branch, string InstancePath, List<JsonNode> DynamicScope);

    private sealed record RepairCondition(
        JsonNode Schema, string InstancePath, List<JsonNode> DynamicScope, bool Expected);

    private static JsonNode ProjectConditionInstance(JsonNode instance, JsonNode schema, RepairTraversalBudget budget)
    {
        if (instance is not JsonObject instanceObject || schema is not JsonObject schemaObject ||
            schemaObject.Any(static property => property.Key is not ("properties" or "required" or "type" or "minProperties" or "maxProperties")))
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
                    budget.SpendCopy(value);
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

        if (schemaObject.ContainsKey("minProperties") || schemaObject.ContainsKey("maxProperties"))
        {
            foreach (KeyValuePair<string, JsonNode?> property in instanceObject)
            {
                budget.SpendText(property.Key);
                if (!projection.ContainsKey(property.Key))
                {
                    projection[property.Key] = null;
                }
            }
        }

        return projection;
    }

    private static bool IsConditionUnaffected(
        RepairCondition condition, RepairMutationPaths mutations, RepairTraversalBudget budget)
    {
        if (condition.Schema is not JsonObject schema ||
            schema.Any(static property => property.Key is not ("properties" or "required" or "type" or "minProperties" or "maxProperties")))
        {
            return false;
        }

        for (string path = condition.InstancePath; path.Length > 0; path = path.Substring(0, path.LastIndexOf('/')))
        {
            budget.SpendText(path);
            if (mutations.Additions.Contains(path))
            {
                return false;
            }
        }

        if (mutations.DirectAdditions.TryGetValue(condition.InstancePath, out HashSet<string>? added))
        {
            if (schema.ContainsKey("minProperties") || schema.ContainsKey("maxProperties"))
            {
                return false;
            }
            if (schema["required"] is JsonArray required)
            {
                foreach (JsonNode? item in required)
                {
                    budget.Spend(1);
                    if (item?.GetValue<string>() is string name && added.Contains(name))
                    {
                        return false;
                    }
                }
            }
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (KeyValuePair<string, JsonNode?> property in properties)
            {
                budget.SpendText(property.Key);
                if (mutations.AffectedAncestors.Contains(AppendJsonPointer(condition.InstancePath, property.Key)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private sealed class RepairMutationPaths
    {
        internal HashSet<string> Additions { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> AffectedAncestors { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, HashSet<string>> DirectAdditions { get; } = new(StringComparer.Ordinal);

        internal RepairMutationPaths(List<RepairCandidate> candidates, RepairTraversalBudget budget)
        {
            foreach (RepairCandidate candidate in candidates)
            {
                budget.SpendText(candidate.ParentPath);
                budget.SpendText(candidate.PropertyName);
                string path = candidate.Key;
                Additions.Add(path);
                if (!DirectAdditions.TryGetValue(candidate.ParentPath, out HashSet<string>? names))
                {
                    names = new(StringComparer.Ordinal);
                    DirectAdditions.Add(candidate.ParentPath, names);
                }
                names.Add(candidate.PropertyName);
                for (; path.Length > 0; path = path.Substring(0, path.LastIndexOf('/')))
                {
                    budget.SpendText(path);
                    AffectedAncestors.Add(path);
                }
            }
        }
    }

    private sealed record RepairCandidateGroup(
        HashSet<string> Keys, HashSet<string> DirectKeys, string InstancePath, JsonNode Schema,
        List<JsonNode> DynamicScope, List<RepairExclusiveChoice> Choices, List<RepairCondition> Conditions,
        RepairUnevaluatedApplication? Unevaluated = null, bool EnclosesInstance = false,
        List<RepairExclusiveChoice>? Alternatives = null);

    private sealed record RepairUnevaluatedApplication(JsonNode Schema, string InstancePath);

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
        internal const int MaxEvaluationWork = 65_536;
        // Shared by document attempts and recursive branch repairs, including cached merges.
        // Costs include copied entries, JSON nodes, and 16-character text units.
        private int _remainingWork = 2_000_000;
        private int _remainingCopyWork = 1_000_000;
        // Compiled branch schemas retain copies of the complete source document.
        private int _remainingSchemaWork = 65_536;
        private int _remainingEvaluationWork = MaxEvaluationWork;
        private readonly Dictionary<JsonNode, int> _immutableCosts = new();
        internal Dictionary<(JsonNode Schema, string Scope), JsonSchema?> BranchSchemas { get; } = new();

        internal void Spend(int work)
        {
            if (work < 0 || work > _remainingWork)
            {
                throw new RecoveryBudgetExceededException();
            }

            _remainingWork -= work;
        }

        internal void SpendText(string text) => Spend(1 + text.Length / 16);

        internal void SpendEvaluation(JsonSchema schema, JsonElement instance, bool detailed = false)
        {
            SchemaEvaluationCost evaluation = s_schemaEvaluationCosts.GetValue(schema, static compiled => new(compiled.Root));
            if (!detailed && !evaluation.HasUnevaluated)
            {
                return;
            }

            int instanceCost = 0;
            Measure(instance, ref instanceCost, 0);
            int cost = evaluation.GetCost(instance, instanceCost, _remainingEvaluationWork);
            if (cost > _remainingEvaluationWork)
            {
                throw new RecoveryBudgetExceededException();
            }
            _remainingEvaluationWork -= cost;
        }

        internal void SpendContexts(Dictionary<string, SchemaObjectContext> contexts)
        {
            Spend(contexts.Count);
            foreach (KeyValuePair<string, SchemaObjectContext> entry in contexts)
            {
                Spend(entry.Value.Required.Count);
            }
        }

        internal void SpendSchema(JsonNode schema)
        {
            int cost = GetCopyCost(schema, immutable: true);
            if (cost > _remainingSchemaWork)
            {
                throw new RecoveryBudgetExceededException();
            }

            SpendCopyCost(cost);
            _remainingSchemaWork -= cost;
        }

        internal void SpendCopy(JsonNode? node, bool immutable = false) => SpendCopyCost(GetCopyCost(node, immutable));

        internal void SpendCopy(JsonElement element)
        {
            int cost = 0;
            Measure(element, ref cost, 0);
            SpendCopyCost(cost);
        }

        private void SpendCopyCost(int cost)
        {
            if (cost > _remainingCopyWork)
            {
                throw new RecoveryBudgetExceededException();
            }
            _remainingCopyWork -= cost;
        }

        private int GetCopyCost(JsonNode? node, bool immutable)
        {
            if (immutable && node is not null && _immutableCosts.TryGetValue(node, out int cached))
            {
                return cached;
            }

            int cost = 0;
            Measure(node, ref cost, 0);
            if (immutable && node is not null)
            {
                _immutableCosts[node] = cost;
            }
            return cost;
        }

        private void AddCost(ref int cost, int work, int depth)
        {
            if (depth >= 256 || work > _remainingCopyWork - cost)
            {
                throw new RecoveryBudgetExceededException();
            }
            cost += work;
        }

        private void Measure(JsonNode? node, ref int cost, int depth)
        {
            AddCost(ref cost, 1, depth);
            if (node is JsonObject obj)
            {
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    AddCost(ref cost, 1 + property.Key.Length / 16, depth);
                    Measure(property.Value, ref cost, depth + 1);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    Measure(item, ref cost, depth + 1);
                }
            }
            else if (node is JsonValue value && value.TryGetValue<string>(out string? text) && text is not null)
            {
                AddCost(ref cost, text.Length / 16, depth);
            }
            else if (node is JsonValue number && number.GetValueKind() == JsonValueKind.Number)
            {
                AddCost(ref cost, number.ToJsonString().Length / 16, depth);
            }
        }

        private void Measure(JsonElement element, ref int cost, int depth)
        {
            AddCost(ref cost, 1, depth);
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        AddCost(ref cost, 1 + property.Name.Length / 16, depth);
                        Measure(property.Value, ref cost, depth + 1);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        Measure(item, ref cost, depth + 1);
                    }
                    break;
                case JsonValueKind.String:
                    AddCost(ref cost, element.GetString()!.Length / 16, depth);
                    break;
                case JsonValueKind.Number:
                    AddCost(ref cost, element.GetRawText().Length / 16, depth);
                    break;
            }
        }
    }

    private sealed class RecoveryBudgetExceededException : Exception;

    private sealed class SchemaEvaluationCost
    {
        internal bool HasUnevaluated { get; }
        private readonly JsonSchemaNode _root;
        private readonly int _expandedCost;
        private readonly Dictionary<(Uri Resource, string Anchor), JsonSchemaNode> _dynamicTargets = new();
        private bool _needsInstanceCost;

        internal SchemaEvaluationCost(JsonSchemaNode root)
        {
            _root = root;
            HashSet<JsonSchemaNode> visited = [root];
            Stack<JsonSchemaNode> pending = new();
            pending.Push(root);
            while (pending.Count > 0)
            {
                JsonSchemaNode node = pending.Pop();
                foreach (KeywordData keyword in node.Keywords)
                {
                    HasUnevaluated |= keyword.Handler.Name is "unevaluatedProperties" or "unevaluatedItems";
                    if (keyword.Handler.Name == "$dynamicAnchor")
                    {
                        _dynamicTargets[(node.BaseUri, keyword.RawValue.GetString()!)] = node;
                    }
                    foreach (JsonSchemaNode child in keyword.Subschemas)
                    {
                        if (visited.Add(child))
                        {
                            pending.Push(child);
                        }
                    }
                }
            }

            _expandedCost = Measure(root, new(), new(), 0);
        }

        internal int GetCost(JsonElement instance, int instanceCost, int limit)
        {
            if (!_needsInstanceCost)
            {
                return _expandedCost > limit / instanceCost ? limit + 1 : _expandedCost * instanceCost;
            }

            int remaining = limit;
            int cost = MeasureInstance(_root, instance, string.Empty, false, [], new(), new(), 0, ref remaining);
            return Math.Max(cost, limit - remaining);
        }

        private int Measure(
            JsonSchemaNode node, Dictionary<JsonSchemaNode, int> completed, HashSet<JsonSchemaNode> active, int depth)
        {
            const int limit = RepairTraversalBudget.MaxEvaluationWork + 1;
            if (completed.TryGetValue(node, out int cached))
            {
                return cached;
            }
            if (depth >= 128 || !active.Add(node))
            {
                _needsInstanceCost = true;
                return limit;
            }

            int cost = 1;
            foreach (KeywordData keyword in node.Keywords)
            {
                if (keyword.Handler.Name is "$defs" or "definitions")
                {
                    continue;
                }

                cost++;
                if (keyword.Handler.Name is "$dynamicRef" or "$recursiveRef" && keyword.Subschemas.Length == 0)
                {
                    _needsInstanceCost = true;
                    cost = limit;
                }
                foreach (JsonSchemaNode child in keyword.Subschemas)
                {
                    cost = Math.Min(limit, cost + Measure(child, completed, active, depth + 1));
                    if (cost >= limit)
                    {
                        break;
                    }
                }
                if (cost >= limit)
                {
                    break;
                }
            }

            active.Remove(node);
            completed[node] = cost;
            return cost;
        }

        private int MeasureInstance(
            JsonSchemaNode node, JsonElement instance, string path, bool propertyName, List<Uri> scope,
            Dictionary<(JsonSchemaNode, string, string, bool), int> completed,
            HashSet<(JsonSchemaNode, string, string, bool)> active, int depth, ref int remaining)
        {
            const int limit = RepairTraversalBudget.MaxEvaluationWork + 1;
            if (--remaining < 0 || depth >= 128)
            {
                return limit;
            }
            if (!scope.Contains(node.BaseUri))
            {
                scope = [.. scope, node.BaseUri];
            }
            foreach (Uri resource in scope)
            {
                remaining -= 1 + resource.AbsoluteUri.Length / 16;
                if (remaining < 0)
                {
                    return limit;
                }
            }
            var state = (node, path, string.Join("\n", scope), propertyName);
            if (completed.TryGetValue(state, out int cached))
            {
                return cached;
            }
            if (!active.Add(state))
            {
                return limit;
            }

            int cost = 1;
            foreach (KeywordData keyword in node.Keywords)
            {
                string name = keyword.Handler.Name;
                if (name is "$defs" or "definitions")
                {
                    continue;
                }
                cost++;
                if (name == "$recursiveRef" && keyword.Subschemas.Length == 0)
                {
                    // Legacy recursive scope can stop at an intervening non-recursive resource.
                    return limit;
                }
                if (name == "$dynamicRef" && keyword.Subschemas.Length == 0)
                {
                    JsonSchemaNode? target = null;
                    if (Uri.TryCreate(node.BaseUri, keyword.RawValue.GetString(), out Uri? reference))
                    {
                        string anchor = Uri.UnescapeDataString(reference.Fragment.TrimStart('#'));
                        foreach (Uri resource in scope)
                        {
                            if (_dynamicTargets.TryGetValue((resource, anchor), out target))
                            {
                                break;
                            }
                        }
                    }
                    cost = target is null ? limit :
                        Math.Min(limit, cost + MeasureInstance(target, instance, path, propertyName, scope, completed, active, depth + 1, ref remaining));
                }
                foreach (JsonSchemaNode child in keyword.Subschemas)
                {
                    if (name == "properties")
                    {
                        string property = child.RelativePath[child.RelativePath.SegmentCount - 1].ToString();
                        if (instance.ValueKind == JsonValueKind.Object && instance.TryGetProperty(property, out JsonElement value))
                        {
                            cost += MeasureInstance(child, value, AppendPath(path, property, ref remaining), false,
                                scope, completed, active, depth + 1, ref remaining);
                        }
                    }
                    else if (name is "patternProperties" or "additionalProperties" or "unevaluatedProperties" or "propertyNames")
                    {
                        if (instance.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty property in instance.EnumerateObject())
                            {
                                // Property-name schemas cannot observe the property's value.
                                JsonElement value = name == "propertyNames" ? s_propertyNameInstance : property.Value;
                                cost += MeasureInstance(child, value, AppendPath(path, property.Name, ref remaining), name == "propertyNames",
                                    scope, completed, active, depth + 1, ref remaining);
                                if (cost >= limit)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    else if (name is "items" or "prefixItems" or "contains" or "unevaluatedItems" or "additionalItems")
                    {
                        if (instance.ValueKind == JsonValueKind.Array)
                        {
                            int index = 0;
                            foreach (JsonElement value in instance.EnumerateArray())
                            {
                                cost += MeasureInstance(child, value, AppendPath(path, (index++).ToString(CultureInfo.InvariantCulture), ref remaining), false,
                                    scope, completed, active, depth + 1, ref remaining);
                                if (cost >= limit)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        cost += MeasureInstance(child, instance, path, propertyName, scope, completed, active, depth + 1, ref remaining);
                    }
                    if (cost >= limit)
                    {
                        break;
                    }
                }
                if (cost >= limit)
                {
                    cost = limit;
                    break;
                }
            }

            active.Remove(state);
            completed[state] = cost;
            return cost;
        }

        private static string AppendPath(string path, string segment, ref int remaining)
        {
            remaining -= 1 + path.Length / 16 + segment.Length / 16;
            if (remaining < 0)
            {
                throw new RecoveryBudgetExceededException();
            }
            return AppendJsonPointer(path, segment);
        }
    }

    private sealed class SchemaTraversal(
        RepairTraversalBudget budget, Func<JsonNode, string, bool> appliesUnevaluated,
        Func<JsonNode, JsonNode?, List<JsonNode>, bool> isBranchValid, bool includeValidAlternatives = false)
    {
        internal RepairTraversalBudget Budget { get; } = budget;
        internal Func<JsonNode, string, bool> AppliesUnevaluated { get; } = appliesUnevaluated;
        internal Func<JsonNode, JsonNode?, List<JsonNode>, bool> IsBranchValid { get; } = isBranchValid;
        internal bool IncludeValidAlternatives { get; } = includeValidAlternatives;
        internal bool SkippedValidAlternatives { get; set; }
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
        traversal.Budget.Spend(1);

        if (traversal.Completed.TryGetValue(state, out SchemaTraversalResult? completed))
        {
            traversal.Budget.SpendContexts(completed.Contexts);
            traversal.Budget.Spend(completed.Groups.Count);
            MergeSchemaContexts(contexts, completed.Contexts);
            groups.AddRange(completed.Groups);

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
                    traversal.Budget.SpendText(propertyName);
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

                CollectSchemaBranch(
                    propertyInstance,
                    property.Value,
                    schemaRoot,
                    AppendJsonPointer(instancePath, property.Key),
                    contexts,
                    traversal,
                    groups, references, dynamicScope, enclosesInstance: true);
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
                        traversal.Budget.SpendText(patternProperty.Key);
                        traversal.Budget.SpendText(property.Key);
                        if (patternProperty.Value is not null &&
                            Regex.IsMatch(property.Key, patternProperty.Key, RegexOptions.None, s_recoveryRegexTimeout))
                        {
                            matchedPattern = true;
                            CollectSchemaBranch(
                                property.Value,
                                patternProperty.Value,
                                schemaRoot,
                                AppendJsonPointer(instancePath, property.Key),
                                contexts,
                                traversal,
                                groups, references, dynamicScope, enclosesInstance: true);
                        }
                    }
                }

                if (!isExplicitProperty && !matchedPattern && additionalProperties is not null)
                {
                    CollectSchemaBranch(
                        property.Value,
                        additionalProperties,
                        schemaRoot,
                        AppendJsonPointer(instancePath, property.Key),
                        contexts,
                        traversal,
                        groups, references, dynamicScope, enclosesInstance: true);
                }

                if (schemaObject["unevaluatedProperties"] is JsonNode unevaluatedPropertySchema &&
                    traversal.AppliesUnevaluated(unevaluatedPropertySchema, AppendJsonPointer(instancePath, property.Key)))
                {
                    CollectSchemaBranch(
                        property.Value, unevaluatedPropertySchema, schemaRoot,
                        AppendJsonPointer(instancePath, property.Key), contexts, traversal, groups, references, dynamicScope,
                        unevaluated: new(unevaluatedPropertySchema, AppendJsonPointer(instancePath, property.Key)));
                }
            }
        }

        if (instanceNode is JsonArray arrayInstance)
        {
            JsonArray? prefixItems = schemaObject["prefixItems"] as JsonArray;
            JsonNode? items = schemaObject["items"];
            double minimumMatches = schemaObject["minContains"] is JsonValue minimum &&
                minimum.TryGetValue<double>(out double count) ? count : 1;
            bool repairContains = schemaObject["contains"] is JsonNode contains &&
                arrayInstance.Count(item => traversal.IsBranchValid(contains, item, dynamicScope)) < minimumMatches;
            for (int i = 0; i < arrayInstance.Count; i++)
            {
                JsonNode? itemSchema = prefixItems is not null && i < prefixItems.Count ? prefixItems[i] : items;
                if (itemSchema is not null)
                {
                    CollectSchemaBranch(
                        arrayInstance[i],
                        itemSchema,
                        schemaRoot,
                        AppendJsonPointer(instancePath, i.ToString(CultureInfo.InvariantCulture)),
                        contexts,
                        traversal,
                        groups, references, dynamicScope, enclosesInstance: true);
                }

                foreach (string keyword in new[] { "contains", "unevaluatedItems" })
                {
                    if (schemaObject[keyword] is JsonNode subschema &&
                        (keyword != "contains" || repairContains) &&
                        (keyword != "unevaluatedItems" ||
                            traversal.AppliesUnevaluated(subschema, AppendJsonPointer(instancePath, i.ToString(CultureInfo.InvariantCulture)))))
                    {
                        CollectSchemaBranch(
                            arrayInstance[i], subschema, schemaRoot,
                            AppendJsonPointer(instancePath, i.ToString(CultureInfo.InvariantCulture)),
                            contexts, traversal, groups, references, dynamicScope,
                            unevaluated: keyword == "unevaluatedItems"
                                ? new(subschema, AppendJsonPointer(instancePath, i.ToString(CultureInfo.InvariantCulture))) : null);
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
                            traversal.Budget.SpendText(propertyName);
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

            if (compositionKeyword != "allOf")
            {
                int validBranches = composition.Count(branch => branch is not null &&
                    traversal.IsBranchValid(branch, instanceNode, dynamicScope));
                if ((compositionKeyword == "anyOf" && validBranches > 0) ||
                    (compositionKeyword == "oneOf" && validBranches == 1))
                {
                    if (!traversal.IncludeValidAlternatives)
                    {
                        traversal.SkippedValidAlternatives = true;
                        continue;
                    }
                }
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
                            compositionKeyword == "oneOf" ? composition : null,
                            alternativeApplicator: compositionKeyword == "anyOf" ? composition : null);
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

        traversal.Budget.Spend(groups.Count);
        groups = groups.Distinct().ToList();
        traversal.Active.Remove(state);
        // A cycle or depth cutoff produces a partial summary that cannot be reused
        // when this schema is reached later through a different caller.
        if (incompleteVersion == traversal.IncompleteVersion)
        {
            traversal.Completed[state] = new(contexts, groups);
        }
        traversal.Budget.SpendContexts(contexts);
        traversal.Budget.Spend(groups.Count);
        MergeSchemaContexts(destinationContexts, contexts);
        destinationGroups.AddRange(groups);
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
        RepairCondition? condition = null,
        RepairUnevaluatedApplication? unevaluated = null,
        bool enclosesInstance = false,
        JsonNode? alternativeApplicator = null)
    {
        Dictionary<string, SchemaObjectContext> branchContexts = new(StringComparer.Ordinal);
        List<RepairCandidateGroup> branchGroups = [];
        CollectSchemaContexts(
            instanceNode, branch, schemaRoot, instancePath, branchContexts,
            traversal, branchGroups, references, dynamicScope);
        traversal.Budget.SpendContexts(branchContexts);
        List<JsonNode> branchScope = SchemaReferenceResolver.EnterResource(dynamicScope, references.GetResourceRoot(branch));
        List<RepairExclusiveChoice> choices = exclusiveApplicator is null
            ? [] : [new(exclusiveApplicator, branch, instancePath, branchScope)];
        List<RepairCondition> conditions = condition is null ? [] : [condition];
        List<RepairExclusiveChoice> alternatives = alternativeApplicator is null
            ? [] : [new(alternativeApplicator, branch, instancePath, branchScope)];
        HashSet<string> group = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, SchemaObjectContext> entry in branchContexts)
        {
            GetOrCreateSchemaContext(contexts, entry.Key).Required.UnionWith(entry.Value.Required);
            foreach (string property in entry.Value.Required)
            {
                traversal.Budget.SpendText(entry.Key);
                traversal.Budget.SpendText(property);
                group.Add(AppendJsonPointer(entry.Key, property));
            }
        }

        if (group.Count > 0)
        {
            traversal.Budget.Spend(group.Count);
            foreach (RepairCandidateGroup nested in branchGroups)
            {
                traversal.Budget.Spend(nested.Keys.Count);
            }
            HashSet<string> directKeys = new(group, StringComparer.Ordinal);
            directKeys.ExceptWith(branchGroups.SelectMany(static nested => nested.Keys));
            // Restore this branch's own discriminator before composing nested selectors.
            // Nested alternatives must not be added en masse before re-evaluating them.
            groups.Add(new(group, directKeys, instancePath, branch, branchScope, choices, conditions, unevaluated, enclosesInstance, alternatives));
        }

        foreach (RepairCandidateGroup branchGroup in branchGroups)
        {
            traversal.Budget.Spend(1 + choices.Count + branchGroup.Choices.Count +
                conditions.Count + branchGroup.Conditions.Count +
                alternatives.Count + (branchGroup.Alternatives?.Count ?? 0));
            groups.Add(branchGroup with
            {
                Choices = [.. choices, .. branchGroup.Choices],
                Conditions = [.. conditions, .. branchGroup.Conditions],
                Unevaluated = unevaluated ?? branchGroup.Unevaluated,
                Alternatives = [.. alternatives, .. branchGroup.Alternatives ?? []],
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
        object? owner = null, JsonTypeInfo? ownerContract = null, SourceRecoveryContext? parent = null,
        Type? fallbackType = null)
    {
        private readonly Dictionary<string, SourceRecoveryContext?> _children = new(StringComparer.Ordinal);
        private Dictionary<JsonTypeInfo<object>, JsonTypeInfo<object>>? _scopeContracts;
        private JsonTypeInfo? _activeContract;
        private bool _dictionaryRead;
        private bool _extensionDataRead;
        private object? _extensionData;

        internal object? Value { get; } = value;
        internal JsonTypeInfo Contract { get; } = contract;
        internal JsonPropertyInfo? Property { get; } = property;
        internal object? Owner { get; } = owner;
        internal JsonTypeInfo? OwnerContract { get; } = ownerContract;
        internal SourceRecoveryContext? Parent { get; } = parent;

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
                    return new(Value, typeInfo, parent: this);
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
                            : new(entryValue, options.GetTypeInfo(typeInfo.ElementType!), parent: this);
                    }
                }

                return _children.TryGetValue(name, out SourceRecoveryContext? entryContext) ? entryContext : null;
            }

            if (typeInfo.Kind == JsonTypeInfoKind.Enumerable && serializedParent is JsonArray serializedArray &&
                int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                index >= 0 && index < serializedArray.Count)
            {
                if (Value is IList list)
                {
                    return index < list.Count ? new(list[index], options.GetTypeInfo(typeInfo.ElementType!), parent: this) : null;
                }

                if (TryGetGenericListElement(Value, typeInfo.ElementType!, index, out object? element))
                {
                    return new(element, options.GetTypeInfo(typeInfo.ElementType!), parent: this);
                }
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

                if (serializedParent is JsonObject serializedObject && serializedObject.ContainsKey(name) &&
                    sourceContract.ExtensionData is { } extension &&
                    GetExtensionData(extension) is { } collisionData &&
                    (extension.CustomConverter is not null || collisionData switch
                    {
                        IDictionary<string, object?> data => data.ContainsKey(name),
                        IDictionary<string, JsonElement> data => data.ContainsKey(name),
                        JsonObject data => data.ContainsKey(name),
                        _ => true,
                    }))
                {
                    // The JSON path may belong to extension data instead of this CLR member.
                    return null;
                }

                object? memberValue = member.Get(Value);
                if (serializedParent is JsonObject parentObject && parentObject.ContainsKey(name) &&
                    member.ShouldSerialize?.Invoke(Value, memberValue) == false)
                {
                    return null;
                }

                JsonTypeInfo memberContract = options.GetTypeInfo(member.PropertyType);
                return new(memberValue, memberContract, member, Value, typeInfo, this);
            }

            JsonPropertyInfo? extensionData = sourceContract.ExtensionData;
            if (extensionData is null || extensionData.CustomConverter is not null)
            {
                return null;
            }

            object? extensionValue = GetExtensionData(extensionData);
            if (extensionValue is null)
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
                return new(extensionEntry, options.GetTypeInfo(extensionType.ElementType!), parent: this);
            }

            if (extensionValue is IDictionary<string, JsonElement> elementData && elementData.TryGetValue(name, out JsonElement elementEntry))
            {
                return new(elementEntry, options.GetTypeInfo(typeof(JsonElement)), parent: this);
            }

            return null;
        }

#if !NETSTANDARD2_0
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(IReadOnlyList<>))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(IList<>))]
#endif
        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The indexed interface properties are preserved by DynamicDependency.")]
        private static bool TryGetGenericListElement(object value, Type elementType, int index, out object? element)
        {
            element = null;
            foreach (Type contract in value.GetType().GetInterfaces())
            {
                if (!contract.IsGenericType || contract.GetGenericArguments()[0] != elementType)
                {
                    continue;
                }

                Type definition = contract.GetGenericTypeDefinition();
                if (definition == typeof(IList<>) || definition == typeof(IReadOnlyList<>))
                {
                    PropertyInfo? item = contract.GetProperty("Item");
                    if (item is not null)
                    {
                        try
                        {
                            element = item.GetValue(value, [index]);
                            return true;
                        }
                        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException or IndexOutOfRangeException)
                        {
                            return false;
                        }
                    }
                }
            }

            return false;
        }

        private object? GetExtensionData(JsonPropertyInfo property)
        {
            if (!_extensionDataRead)
            {
                _extensionDataRead = true;
                if (property.Get is { } getter && !IsExcludedReadOnlyMember(property, Contract.Options))
                {
                    object? data = getter(Value!);
                    if (property.ShouldSerialize?.Invoke(Value!, data) != false)
                    {
                        _extensionData = data;
                    }
                }
            }

            return _extensionData;
        }

        internal object GetSerializationOwner() =>
            GetActiveContract().Type.IsValueType ? RuntimeHelpers.GetObjectValue(Value!)! : Value!;

        internal JsonTypeInfo<object> GetScopeContract(JsonTypeInfo<object> childContract)
        {
            _scopeContracts ??= new();
            if (!_scopeContracts.TryGetValue(childContract, out JsonTypeInfo<object>? scope))
            {
                scope = CreateSerializationObjectContract(GetActiveContract());
                JsonPropertyInfo child = JsonMetadataServices.CreatePropertyInfo<object>(scope.Options, new()
                {
                    DeclaringType = typeof(object),
                    IsProperty = true,
                    IsPublic = true,
                    PropertyName = "value",
                    JsonPropertyName = "value",
                    PropertyTypeInfo = childContract,
                    Getter = static _ => t_serializingSourceMember!.GetNextOwner(),
                });
                child.ShouldSerialize = static (_, _) => true;
                scope.Properties.Add(child);
                scope.MakeReadOnly();
                _scopeContracts.Add(childContract, scope);
            }

            return scope;
        }

        private JsonTypeInfo GetActiveContract() => _activeContract ??= ResolveActiveContract();

        private JsonTypeInfo ResolveActiveContract()
        {
            JsonSerializerOptions options = Contract.Options;
            Type runtimeType = Value!.GetType();
            if (runtimeType == Contract.Type)
            {
                return Contract;
            }

            if (Contract.Type == typeof(object) && Contract.Converter.GetType().Assembly == typeof(JsonSerializer).Assembly)
            {
                try
                {
                    return options.GetTypeInfo(runtimeType);
                }
                catch (NotSupportedException) when (runtimeType.IsDefined(typeof(CompilerGeneratedAttribute), false) &&
                    typeof(IEnumerable).IsAssignableFrom(runtimeType) && fallbackType is not null)
                {
                    JsonTypeInfo fallbackContract = options.GetTypeInfo(fallbackType);
                    if (fallbackContract.Kind != JsonTypeInfoKind.Enumerable)
                    {
                        throw;
                    }

                    return fallbackContract;
                }
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
            JsonTypeInfo<object> contract = CreateSerializationObjectContract(_contract);
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

    private static JsonTypeInfo<object> CreateSerializationObjectContract(JsonTypeInfo original)
    {
        JsonConverter converter = original.Kind == JsonTypeInfoKind.Object
            ? original.Converter
            : JsonMetadataServices.CreateObjectInfo<IEnumerable>(original.Options, new()
            {
                PropertyMetadataInitializer = static _ => [],
            }).Converter;
        // Collections use a single-property object view to seed the same reference stack
        // without enumerating unrelated entries. These ancestor envelopes are never published.
        JsonTypeInfo<object> contract = JsonMetadataServices.CreateValueInfo<object>(original.Options, converter);
        contract.NumberHandling = original.NumberHandling;
        contract.PolymorphismOptions = null;
        contract.OnSerializing = null;
        contract.OnSerialized = null;
        return contract;
    }

    private sealed class SourceMemberSerializationState(object? value, Stack<SourceRecoveryContext>? owners)
    {
        internal object? Value { get; } = value;
        internal object GetNextOwner() => owners!.Pop().GetSerializationOwner();
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
            SourceRecoveryContext ownerContext = member.Parent!;
            Stack<SourceRecoveryContext>? owners = null;
            int scopeDepth = 0;
            if (ReferenceEquals(options.ReferenceHandler, ReferenceHandler.IgnoreCycles) && ownerContext.Parent is not null)
            {
                owners = new();
                owners.Push(ownerContext);
                while (ownerContext.Parent is { } ancestor)
                {
                    contract = ancestor.GetScopeContract(contract);
                    owners.Push(ancestor);
                    ownerContext = ancestor;
                    scopeDepth++;
                }
                owners.Pop();
            }

            JsonElement element;
            SourceMemberSerializationState? previous = t_serializingSourceMember;
            try
            {
                // Serialization is synchronous; restore the slot for nested invocations.
                t_serializingSourceMember = new(member.Value, owners);
                // Value-type serialization must not track the original box as a reference cycle.
                element = JsonSerializer.SerializeToElement(ownerContext.GetSerializationOwner(), contract);
                for (int i = 0; i < scopeDepth; i++)
                {
                    element = element.GetProperty("value");
                }
                element = element.GetProperty(property.Name);
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
        private readonly List<JsonNode> _dynamicSchemas = [];
        private readonly JsonNode _root;
        private readonly Uri? _rootUri;
        internal HashSet<JsonNode> SupplementalSchemas { get; } = [];
        internal bool HasDynamicReferences => _dynamicSchemas.Count != 0;

        internal bool CanResolveReference(Uri resourceUri, string reference) =>
            _resources.TryGetValue(resourceUri, out JsonNode? resource) &&
            TryResolve(resource, reference, out _, out _);

        internal SchemaReferenceResolver(JsonNode schema, Uri? rootUri = null)
        {
            _root = schema;
            _rootUri = rootUri;
            Register(schema, new Uri("https://mcp.invalid/schema"), isRoot: true);
        }

        internal void AddSupplementalSchema(JsonNode schema)
        {
            Register(schema, new Uri("https://mcp.invalid/recovery"));
            SupplementalSchemas.Add(schema);
        }

        internal bool TryResolveEvaluationPath(string path, out Uri? schemaUri)
        {
            schemaUri = null;
            JsonNode? node = _root;
            List<JsonNode> scope = [GetResourceRoot(_root)];
            foreach (string segment in path.Split('/').Skip(1).Select(DecodeJsonPointerSegment))
            {
                if (node is JsonObject obj)
                {
                    if (segment is "$ref" or "$dynamicRef" &&
                        obj[segment] is JsonValue value && value.TryGetValue<string>(out string? reference) &&
                        reference is not null)
                    {
                        if (!TryResolve(GetResourceRoot(node), reference, out JsonNode? target, out JsonNode resource))
                        {
                            return false;
                        }

                        if (segment == "$dynamicRef" && target is JsonObject targetObject &&
                            targetObject["$dynamicAnchor"]?.GetValue<string>() is string anchor &&
                            reference.IndexOf('#') is int fragment && fragment >= 0 &&
                            Uri.UnescapeDataString(reference.Substring(fragment + 1)) == anchor)
                        {
                            foreach (JsonNode caller in scope)
                            {
                                if (TryFindSchemaAnchor(caller, anchor, out JsonNode? bound, dynamicOnly: true))
                                {
                                    target = bound;
                                    resource = caller;
                                    break;
                                }
                            }
                        }

                        node = target;
                        scope = EnterResource(scope, resource);
                    }
                    else
                    {
                        node = obj[segment];
                    }
                }
                else if (node is JsonArray array &&
                    int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                    index >= 0 && index < array.Count)
                {
                    node = array[index];
                }
                else
                {
                    return false;
                }

                if (node is null)
                {
                    return false;
                }

                if (_resourceIds.ContainsKey(node))
                {
                    scope = EnterResource(scope, node);
                }
            }

            schemaUri = GetSchemaUri(node!);
            return true;
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
            foreach (JsonNode node in _dynamicSchemas)
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

            if (schemaObject.ContainsKey("$dynamicRef"))
            {
                _dynamicSchemas.Add(schema);
            }
            bool isResource = isRoot;
            if (isRoot && _rootUri is not null)
            {
                baseUri = _rootUri;
            }
            else if (schemaObject["$id"] is JsonValue idValue && idValue.TryGetValue<string>(out string? id) &&
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
