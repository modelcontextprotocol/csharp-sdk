using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents any JSON-RPC message used in the Model Context Protocol (MCP).
/// </summary>
/// <remarks>
/// This interface serves as the foundation for all message types in the JSON-RPC 2.0 protocol
/// used by MCP, including requests, responses, notifications, and errors. JSON-RPC is a stateless,
/// lightweight remote procedure call (RPC) protocol that uses JSON as its data format.
/// </remarks>
[JsonConverter(typeof(Converter))]
public abstract class JsonRpcMessage
{
    /// <summary>Prevent external derivations.</summary>
    private protected JsonRpcMessage()
    {
    }

    /// <summary>
    /// Gets or sets the JSON-RPC protocol version used.
    /// </summary>
    /// <inheritdoc />
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>
    /// Gets or sets the contextual information for this JSON-RPC message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property contains transport-specific and runtime context information that accompanies
    /// JSON-RPC messages but is not serialized as part of the JSON-RPC payload. This includes
    /// transport references, execution context, and authenticated user information.
    /// </para>
    /// <para>
    /// This property should only be set when implementing a custom <see cref="ITransport"/>
    /// that needs to pass additional per-message context or to pass a <see cref="JsonRpcMessageContext.User"/>
    /// to <see cref="StreamableHttpServerTransport.HandlePostRequestAsync(JsonRpcMessage, Stream, CancellationToken)"/>
    /// or <see cref="SseResponseStreamTransport.OnMessageReceivedAsync(JsonRpcMessage, CancellationToken)"/> .
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public JsonRpcMessageContext? Context { get; set; }

    /// <summary>
    /// Provides a <see cref="JsonConverter"/> for <see cref="JsonRpcMessage"/> messages,
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
    public sealed class Converter : JsonConverter<JsonRpcMessage>
    {
        /// <inheritdoc/>
        public override JsonRpcMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token");
            }

            // We need to determine the concrete message type without round-tripping the payload
            // through a UTF-16 string (e.g. JsonElement.GetRawText()).
            var lookahead = reader;

            bool hasId = false;
            bool hasMethod = false;
            bool hasError = false;
            bool hasResult = false;
            bool foundJsonRpc = false;

            // Scan the top-level object using a copy of the reader.
            while (lookahead.Read())
            {
                if (lookahead.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (lookahead.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token");
                }

                bool isJsonRpc = lookahead.ValueTextEquals("jsonrpc"u8);
                bool isId = lookahead.ValueTextEquals("id"u8);
                bool isMethod = lookahead.ValueTextEquals("method"u8);
                bool isError = lookahead.ValueTextEquals("error"u8);
                bool isResult = lookahead.ValueTextEquals("result"u8);

                if (!lookahead.Read())
                {
                    throw new JsonException("Unexpected end of JSON");
                }

                if (isJsonRpc)
                {
                    foundJsonRpc = lookahead.TokenType == JsonTokenType.String && lookahead.ValueTextEquals("2.0"u8);
                }
                else if (isId)
                {
                    hasId = true;
                }
                else if (isMethod)
                {
                    hasMethod = true;
                }
                else if (isError)
                {
                    hasError = true;
                }
                else if (isResult)
                {
                    hasResult = true;
                }

                SkipValue(ref lookahead);
            }

            // All JSON-RPC messages must have a jsonrpc property with value "2.0"
            if (!foundJsonRpc)
            {
                throw new JsonException("Invalid or missing jsonrpc version");
            }

            // Messages with an id but no method are responses
            if (hasId && !hasMethod)
            {
                // Messages with an error property are error responses
                if (hasError)
                {
                    return JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonRpcError>());
                }

                // Messages with a result property are success responses
                if (hasResult)
                {
                    return JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonRpcResponse>());
                }

                throw new JsonException("Response must have either result or error");
            }

            // Messages with a method but no id are notifications
            if (hasMethod && !hasId)
            {
                return JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonRpcNotification>());
            }

            // Messages with both method and id are requests
            if (hasMethod && hasId)
            {
                return JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonRpcRequest>());
            }

            throw new JsonException("Invalid JSON-RPC message format");
        }

        private static void SkipValue(ref Utf8JsonReader reader)
        {
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                int depth = 0;
                do
                {
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    {
                        depth++;
                    }
                    else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                    {
                        depth--;
                    }
                }
                while (depth > 0 && reader.Read());
            }
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, JsonRpcMessage value, JsonSerializerOptions options)
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
}
