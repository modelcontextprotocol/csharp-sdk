using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
    private static JsonSerializerOptions CreateDefaultOptions()
    {
        // Copy the configuration from the source generated context.
        JsonSerializerOptions options = new(JsonContext.Default.Options);

        // Chain with all supported types from MEAI
        options.TypeInfoResolverChain.Add(AIJsonUtilities.DefaultOptions.TypeInfoResolver!);

        options.MakeReadOnly();
        return options;
    }

    internal static JsonTypeInfo<T> GetTypeInfo<T>(this JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

    internal static JsonElement DefaultMcpToolSchema { get; } = ParseJsonElement("""{"type":"object"}"""u8);
    internal static object? AsObject(this JsonElement element) => element.ValueKind is JsonValueKind.Null ? null : element;

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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString)]
    
    // JSON-RPC
    [JsonSerializable(typeof(IJsonRpcMessage))]
    [JsonSerializable(typeof(JsonRpcRequest))]
    [JsonSerializable(typeof(JsonRpcNotification))]
    [JsonSerializable(typeof(JsonRpcResponse))]
    [JsonSerializable(typeof(JsonRpcError))]

    // MCP Request Params / Results
    [JsonSerializable(typeof(CallToolRequestParams))]
    [JsonSerializable(typeof(CallToolResponse))]
    [JsonSerializable(typeof(CancelledNotification))]
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
    [JsonSerializable(typeof(ProgressNotification))]
    [JsonSerializable(typeof(ReadResourceRequestParams))]
    [JsonSerializable(typeof(ReadResourceResult))]
    [JsonSerializable(typeof(ResourceUpdatedNotificationParams))]
    [JsonSerializable(typeof(SetLevelRequestParams))]
    [JsonSerializable(typeof(SubscribeRequestParams))]
    [JsonSerializable(typeof(UnsubscribeRequestParams))]
    [JsonSerializable(typeof(IReadOnlyDictionary<string, object>))]

    [ExcludeFromCodeCoverage]
    internal sealed partial class JsonContext : JsonSerializerContext;

    private static JsonElement ParseJsonElement(ReadOnlySpan<byte> utf8Json)
    {
        Utf8JsonReader reader = new(utf8Json);
        return JsonElement.ParseValue(ref reader);
    }
}
