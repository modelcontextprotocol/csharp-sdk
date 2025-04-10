using ModelContextProtocol.Protocol.Messages;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Utils.Json;

/// <summary>
/// Provides a <see cref="JsonConverter"/> for <see cref="IJsonRpcMessage"/> messages,
/// handling polymorphic deserialization of different message types.
/// </summary>
/// <remarks>
/// <para>
/// This converter is responsible for correctly deserializing JSON-RPC messages into their appropriate
/// concrete types based on the message structure. It analyzes the JSON payload and determines if it
/// represents a request, notification, successful response, or error response.
/// </para>
/// <para>
/// The type determination rules follow the JSON-RPC 2.0 specification:
/// <list type="bullet">
/// <item><description>Messages with "method" and "id" properties are deserialized as <see cref="JsonRpcRequest"/>.</description></item>
/// <item><description>Messages with "method" but no "id" property are deserialized as <see cref="JsonRpcNotification"/>.</description></item>
/// <item><description>Messages with "id" and "result" properties are deserialized as <see cref="JsonRpcResponse"/>.</description></item>
/// <item><description>Messages with "id" and "error" properties are deserialized as <see cref="JsonRpcError"/>.</description></item>
/// </list>
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class JsonRpcMessageConverter : JsonConverter<IJsonRpcMessage>
{
    /// <inheritdoc/>
    public override IJsonRpcMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // All JSON-RPC messages must have a jsonrpc property with value "2.0"
        if (!root.TryGetProperty("jsonrpc", out var versionProperty) ||
            versionProperty.GetString() != "2.0")
        {
            throw new JsonException("Invalid or missing jsonrpc version");
        }

        // Determine the message type based on the presence of id, method, and error properties
        bool hasId = root.TryGetProperty("id", out _);
        bool hasMethod = root.TryGetProperty("method", out _);
        bool hasError = root.TryGetProperty("error", out _);

        var rawText = root.GetRawText();

        // Messages with an id but no method are responses
        if (hasId && !hasMethod)
        {
            // Messages with an error property are error responses
            if (hasError)
            {
                return JsonSerializer.Deserialize(rawText, options.GetTypeInfo<JsonRpcError>());
            }

            // Messages with a result property are success responses
            if (root.TryGetProperty("result", out _))
            {
                return JsonSerializer.Deserialize(rawText, options.GetTypeInfo<JsonRpcResponse>());
            }

            throw new JsonException("Response must have either result or error");
        }

        // Messages with a method but no id are notifications
        if (hasMethod && !hasId)
        {
            return JsonSerializer.Deserialize(rawText, options.GetTypeInfo<JsonRpcNotification>());
        }

        // Messages with both method and id are requests
        if (hasMethod && hasId)
        {
            return JsonSerializer.Deserialize(rawText, options.GetTypeInfo<JsonRpcRequest>());
        }

        throw new JsonException("Invalid JSON-RPC message format");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, IJsonRpcMessage value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case JsonRpcRequest request:
                JsonSerializer.Serialize(writer, request, options.GetTypeInfo<JsonRpcRequest>());
                break;
            case JsonRpcNotification notification:
                JsonSerializer.Serialize(writer, notification, options.GetTypeInfo<JsonRpcNotification>());
                break;
            case JsonRpcResponse response:
                JsonSerializer.Serialize(writer, response, options.GetTypeInfo<JsonRpcResponse>());
                break;
            case JsonRpcError error:
                JsonSerializer.Serialize(writer, error, options.GetTypeInfo<JsonRpcError>());
                break;
            default:
                throw new JsonException($"Unknown JSON-RPC message type: {value.GetType()}");
        }
    }
}