using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Utils.Json;

/// <summary>Provides a collection of utility methods for working with JSON data in the context of MCP.</summary>
public static partial class McpJsonUtilities
{
    /// <summary>
    /// Gets the <see cref="JsonSerializerOptions"/> singleton used as the default in JSON serialization operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For Native AOT or applications disabling <see cref="JsonSerializer.IsReflectionEnabledByDefault"/>, this instance 
    /// includes source generated contracts for all common exchange types contained in the ModelContextProtocol library.
    /// </para>
    /// <para>
    /// It additionally turns on the following settings:
    /// <list type="number">
    /// <item>Enables string-based enum serialization as implemented by <see cref="JsonStringEnumConverter"/>.</item>
    /// <item>Enables <see cref="JsonIgnoreCondition.WhenWritingNull"/> as the default ignore condition for properties.</item>
    /// <item>Enables <see cref="JsonNumberHandling.AllowReadingFromString"/> as the default number handling for number types.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    /// <summary>
    /// Creates default options to use for MCP-related serialization.
    /// </summary>
    /// <returns>The configured options.</returns>
    [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026", Justification = "DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled")]
    private static JsonSerializerOptions CreateDefaultOptions()
    {
        // If reflection-based serialization is enabled by default, use it, as it's the most permissive in terms of what it can serialize,
        // and we want to be flexible in terms of what can be put into the various collections in the object model.
        // Otherwise, use the source-generated options to enable trimming and Native AOT.
        JsonSerializerOptions options;

        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            // Keep in sync with the JsonSourceGenerationOptions attribute on JsonContext below.
            options = new(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                Converters = { new JsonStringEnumConverter() },
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
            };
        }
        else
        {
            // Keep in sync with any additional settings above beyond what's in JsonContext below.
            options = new(JsonContext.Default.Options)
            {
            };
        }

        // Include all types from AIJsonUtilities, so that anything default usable as part of an AIFunction
        // is also usable as part of an McpServerTool.
        options.TypeInfoResolverChain.Add(AIJsonUtilities.DefaultOptions.TypeInfoResolver!);

        options.MakeReadOnly();
        return options;
    }

    internal static JsonTypeInfo<T> GetTypeInfo<T>(this JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

    internal static JsonElement DefaultMcpToolSchema { get; } = ParseJsonElement("""{"type":"object"}"""u8);

    internal static bool IsValidMcpToolSchema(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.NameEquals("type"))
            {
                if (property.Value.ValueKind is not JsonValueKind.String ||
                    !property.Value.ValueEquals("object"))
                {
                    return false;
                }

                return true; // No need to check other properties
            }
        }

        return false; // No type keyword found.
    }

    // Keep in sync with CreateDefaultOptions above.
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
        UseStringEnumConverter = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString)]

    // JSON
    [JsonSerializable(typeof(JsonDocument))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(JsonNode))]
    
    // JSON-RPC
    [JsonSerializable(typeof(IJsonRpcMessage))]
    [JsonSerializable(typeof(JsonRpcRequest))]
    [JsonSerializable(typeof(JsonRpcNotification))]
    [JsonSerializable(typeof(JsonRpcResponse))]
    [JsonSerializable(typeof(JsonRpcError))]

    // MCP Request Params / Results
    [JsonSerializable(typeof(CallToolRequestParams))]
    [JsonSerializable(typeof(CallToolResponse))]
    [JsonSerializable(typeof(CompleteRequestParams))]
    [JsonSerializable(typeof(CompleteResult))]
    [JsonSerializable(typeof(CreateMessageRequestParams))]
    [JsonSerializable(typeof(CreateMessageResult))]
    [JsonSerializable(typeof(EmptyResult))]
    [JsonSerializable(typeof(GetPromptRequestParams))]
    [JsonSerializable(typeof(GetPromptResult))]
    [JsonSerializable(typeof(InitializeRequestParams))]
    [JsonSerializable(typeof(InitializeResult))]
    [JsonSerializable(typeof(ListPromptsRequestParams))]
    [JsonSerializable(typeof(ListPromptsResult))]
    [JsonSerializable(typeof(ListResourcesRequestParams))]
    [JsonSerializable(typeof(ListResourcesResult))]
    [JsonSerializable(typeof(ListResourceTemplatesRequestParams))]
    [JsonSerializable(typeof(ListResourceTemplatesResult))]
    [JsonSerializable(typeof(ListRootsRequestParams))]
    [JsonSerializable(typeof(ListRootsResult))]
    [JsonSerializable(typeof(ListToolsRequestParams))]
    [JsonSerializable(typeof(ListToolsResult))]
    [JsonSerializable(typeof(LoggingMessageNotificationParams))]
    [JsonSerializable(typeof(PingResult))]
    [JsonSerializable(typeof(ReadResourceRequestParams))]
    [JsonSerializable(typeof(ReadResourceResult))]
    [JsonSerializable(typeof(ResourceUpdatedNotificationParams))]
    [JsonSerializable(typeof(SetLevelRequestParams))]
    [JsonSerializable(typeof(SubscribeRequestParams))]
    [JsonSerializable(typeof(UnsubscribeFromResourceRequestParams))]
    [JsonSerializable(typeof(UnsubscribeRequestParams))]

    [ExcludeFromCodeCoverage]
    internal sealed partial class JsonContext : JsonSerializerContext;

    private static JsonElement ParseJsonElement(ReadOnlySpan<byte> utf8Json)
    {
        Utf8JsonReader reader = new(utf8Json);
        return JsonElement.ParseValue(ref reader);
    }
}
