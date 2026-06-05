using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// or <see cref="SseResponseStreamTransport.OnMessageReceivedAsync(JsonRpcMessage, CancellationToken)"/>.
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

            // Local variables for parsed message data
            bool hasJsonRpc = false;
            RequestId id = default;
            string? method = null;
            JsonNode? parameters = null;
            JsonRpcErrorDetail? error = null;
            JsonNode? result = null;
            bool hasResult = false;

            while (true)
            {
                bool success = reader.Read();
                Debug.Assert(success, "custom converters are guaranteed to be passed fully buffered objects");

                if (reader.TokenType is JsonTokenType.EndObject)
                {
                    break;
                }

                Debug.Assert(reader.TokenType is JsonTokenType.PropertyName);
                string propertyName = reader.GetString()!;

                success = reader.Read();
                Debug.Assert(success, "custom converters are guaranteed to be passed fully buffered objects");

                switch (propertyName)
                {
                    case "jsonrpc":
                        // Validate that the value is "2.0" without allocating a string
                        if (!reader.ValueTextEquals("2.0"u8))
                        {
                            throw new JsonException("Invalid jsonrpc version");
                        }
                        hasJsonRpc = true;
                        break;

                    case "id":
                        id = JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<RequestId>());
                        break;

                    case "method":
                        method = reader.GetString();
                        break;

                    case "params":
                        parameters = JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonNode>());
                        break;

                    case "error":
                        error = JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonRpcErrorDetail>());
                        break;

                    case "result":
                        result = JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonNode>());
                        hasResult = true;
                        break;

                    default:
                        // Skip unknown properties
                        reader.Skip();
                        break;
                }
            }

            // All JSON-RPC messages must have a jsonrpc property with value "2.0"
            if (!hasJsonRpc)
            {
                throw new JsonException("Missing jsonrpc version");
            }

            // Determine message type based on presence of id and method properties
            if (method is not null)
            {
                if (id.Id is not null)
                {
                    // Messages with both method and id are requests
                    return new JsonRpcRequest
                    {
                        Id = id,
                        Method = method,
                        Params = parameters
                    };
                }
                else
                {
                    // Messages with a method but no id are notifications
                    return new JsonRpcNotification
                    {
                        Method = method,
                        Params = parameters
                    };
                }
            }

            if (id.Id is not null)
            {
                if (error is not null)
                {
                    // Messages with an error and id are error responses
                    return new JsonRpcError
                    {
                        Id = id,
                        Error = error
                    };
                }

                if (hasResult)
                {
                    // Messages with a result and id are success responses
                    return new JsonRpcResponse
                    {
                        Id = id,
                        Result = result
                    };
                }

                // Error: Messages with an id but no method, error, or result are invalid
                throw new JsonException("Response must have either result or error");
            }

            // Error: Messages with neither id nor method are invalid
            throw new JsonException("Invalid JSON-RPC message format");
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
