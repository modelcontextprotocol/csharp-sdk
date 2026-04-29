using Microsoft.Extensions.AI;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol;

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
    /// <item>Enables <see cref="JsonSerializerDefaults.Web"/> defaults.</item>
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
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL3050:RequiresDynamicCode", Justification = "Converter is guarded by IsReflectionEnabledByDefault check.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access", Justification = "Converter is guarded by IsReflectionEnabledByDefault check.")]
    private static JsonSerializerOptions CreateDefaultOptions()
    {
        // Copy the configuration from the source generated context.
        JsonSerializerOptions options = new(JsonContext.Default.Options);

        // Chain with all supported types from MEAI.
        options.TypeInfoResolverChain.Add(AIJsonUtilities.DefaultOptions.TypeInfoResolver!);

        // Add a converter for user-defined enums, if reflection is enabled by default.
        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            options.Converters.Add(new JsonStringEnumConverter());
        }

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
    [JsonSerializable(typeof(JsonRpcMessage))]
    [JsonSerializable(typeof(JsonRpcMessage[]))]
    [JsonSerializable(typeof(JsonRpcRequest))]
    [JsonSerializable(typeof(JsonRpcNotification))]
    [JsonSerializable(typeof(JsonRpcResponse))]
    [JsonSerializable(typeof(JsonRpcError))]

    // MCP Notification Params
    [JsonSerializable(typeof(CancelledNotificationParams))]
    [JsonSerializable(typeof(InitializedNotificationParams))]
    [JsonSerializable(typeof(LoggingMessageNotificationParams))]
    [JsonSerializable(typeof(ElicitationCompleteNotificationParams))]
    [JsonSerializable(typeof(ProgressNotificationParams))]
    [JsonSerializable(typeof(PromptListChangedNotificationParams))]
    [JsonSerializable(typeof(ResourceListChangedNotificationParams))]
    [JsonSerializable(typeof(ResourceUpdatedNotificationParams))]
    [JsonSerializable(typeof(RootsListChangedNotificationParams))]
    [JsonSerializable(typeof(ToolListChangedNotificationParams))]
    [JsonSerializable(typeof(McpTaskStatusNotificationParams))]

    // MCP Request Params / Results
    [JsonSerializable(typeof(CallToolRequestParams))]
    [JsonSerializable(typeof(CallToolResult))]
    [JsonSerializable(typeof(CreateTaskResult))]
    [JsonSerializable(typeof(CompleteRequestParams))]
    [JsonSerializable(typeof(CompleteResult))]
    [JsonSerializable(typeof(CreateMessageRequestParams))]
    [JsonSerializable(typeof(CreateMessageResult))]
    [JsonSerializable(typeof(ElicitRequestParams))]
    [JsonSerializable(typeof(ElicitResult))]
    [JsonSerializable(typeof(UrlElicitationRequiredErrorData))]
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
    [JsonSerializable(typeof(PingRequestParams))]
    [JsonSerializable(typeof(PingResult))]
    [JsonSerializable(typeof(ReadResourceRequestParams))]
    [JsonSerializable(typeof(ReadResourceResult))]
    [JsonSerializable(typeof(SetLevelRequestParams))]
    [JsonSerializable(typeof(SubscribeRequestParams))]
    [JsonSerializable(typeof(UnsubscribeRequestParams))]

    // MCP Task Request Params / Results
    [JsonSerializable(typeof(McpTask))]
    [JsonSerializable(typeof(McpTaskStatus))]
    [JsonSerializable(typeof(McpTaskMetadata))]
    [JsonSerializable(typeof(GetTaskRequestParams))]
    [JsonSerializable(typeof(GetTaskResult))]
    [JsonSerializable(typeof(GetTaskPayloadRequestParams))]
    [JsonSerializable(typeof(ListTasksRequestParams))]
    [JsonSerializable(typeof(ListTasksResult))]
    [JsonSerializable(typeof(CancelMcpTaskRequestParams))]
    [JsonSerializable(typeof(CancelMcpTaskResult))]
    [JsonSerializable(typeof(McpTasksCapability))]
    [JsonSerializable(typeof(RequestMcpTasksCapability))]
    [JsonSerializable(typeof(ToolExecution))]
    [JsonSerializable(typeof(ToolTaskSupport))]

    // MCP Content
    [JsonSerializable(typeof(ContentBlock))]
    [JsonSerializable(typeof(TextContentBlock))]
    [JsonSerializable(typeof(ImageContentBlock))]
    [JsonSerializable(typeof(AudioContentBlock))]
    [JsonSerializable(typeof(EmbeddedResourceBlock))]
    [JsonSerializable(typeof(ResourceLinkBlock))]
    [JsonSerializable(typeof(ContentBlock[]))]
    [JsonSerializable(typeof(IEnumerable<ContentBlock>))]
    [JsonSerializable(typeof(PromptMessage))]
    [JsonSerializable(typeof(IEnumerable<PromptMessage>))]
    [JsonSerializable(typeof(PromptReference))]
    [JsonSerializable(typeof(ResourceTemplateReference))]
    [JsonSerializable(typeof(BlobResourceContents))]
    [JsonSerializable(typeof(TextResourceContents))]

    // Other MCP Types
    [JsonSerializable(typeof(IReadOnlyDictionary<string, object>))]
    [JsonSerializable(typeof(ProgressToken))]
    [JsonSerializable(typeof(JsonElement))]

    [JsonSerializable(typeof(ProtectedResourceMetadata))]
    [JsonSerializable(typeof(AuthorizationServerMetadata))]
    [JsonSerializable(typeof(TokenResponse))]
    [JsonSerializable(typeof(DynamicClientRegistrationRequest))]
    [JsonSerializable(typeof(DynamicClientRegistrationResponse))]

    // Primitive types for use in consuming AIFunctions
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(byte))]
    [JsonSerializable(typeof(byte?))]
    [JsonSerializable(typeof(sbyte))]
    [JsonSerializable(typeof(sbyte?))]
    [JsonSerializable(typeof(ushort))]
    [JsonSerializable(typeof(ushort?))]
    [JsonSerializable(typeof(short))]
    [JsonSerializable(typeof(short?))]
    [JsonSerializable(typeof(uint))]
    [JsonSerializable(typeof(uint?))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(int?))]
    [JsonSerializable(typeof(ulong))]
    [JsonSerializable(typeof(ulong?))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(long?))]
    [JsonSerializable(typeof(nuint))]
    [JsonSerializable(typeof(nuint?))]
    [JsonSerializable(typeof(nint))]
    [JsonSerializable(typeof(nint?))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(bool?))]
    [JsonSerializable(typeof(char))]
    [JsonSerializable(typeof(char?))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(float?))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(double?))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(decimal?))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(Guid?))]
    [JsonSerializable(typeof(Uri))]
    [JsonSerializable(typeof(Version))]
    [JsonSerializable(typeof(TimeSpan))]
    [JsonSerializable(typeof(TimeSpan?))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTime?))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(DateTimeOffset?))]
#if NET
    [JsonSerializable(typeof(DateOnly))]
    [JsonSerializable(typeof(DateOnly?))]
    [JsonSerializable(typeof(TimeOnly))]
    [JsonSerializable(typeof(TimeOnly?))]
    [JsonSerializable(typeof(Half))]
    [JsonSerializable(typeof(Half?))]
    [JsonSerializable(typeof(Int128))]
    [JsonSerializable(typeof(Int128?))]
    [JsonSerializable(typeof(UInt128))]
    [JsonSerializable(typeof(UInt128?))]
#endif

    [ExcludeFromCodeCoverage]
    internal sealed partial class JsonContext : JsonSerializerContext;

    private static JsonElement ParseJsonElement(ReadOnlySpan<byte> utf8Json)
    {
        Utf8JsonReader reader = new(utf8Json);
        return JsonElement.ParseValue(ref reader);
    }
}
