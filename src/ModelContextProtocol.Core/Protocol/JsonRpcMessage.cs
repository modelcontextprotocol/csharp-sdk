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
        private const string JsonRpcVersion = "2.0";

        /// <inheritdoc/>
        public override JsonRpcMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var union = ParseUnion(ref reader, options);

            // All JSON-RPC messages must have a jsonrpc property with value "2.0"
            if (union.JsonRpc != JsonRpcVersion)
            {
                throw new JsonException("Invalid or missing jsonrpc version");
            }

            // Determine message type based on presence of id and method properties
            switch ((union.HasId, union.HasMethod))
            {
                case (true, true):
                    // Messages with both method and id are requests
                    Debug.Assert(union.Method is not null, "HasMethod should only be true when Method is non-null");
                    return new JsonRpcRequest
                    {
                        JsonRpc = union.JsonRpc,
                        Id = union.Id,
                        Method = union.Method!,
                        Params = union.Params
                    };

                case (true, false):
                    // Messages with an id but no method are responses
                    if (union.HasError)
                    {
                        Debug.Assert(union.Error is not null, "HasError should only be true when Error is non-null");
                        return new JsonRpcError
                        {
                            JsonRpc = union.JsonRpc,
                            Id = union.Id,
                            Error = union.Error!
                        };
                    }

                    if (union.HasResult)
                    {
                        return new JsonRpcResponse
                        {
                            JsonRpc = union.JsonRpc,
                            Id = union.Id,
                            Result = union.Result
                        };
                    }

                    throw new JsonException("Response must have either result or error");

                case (false, true):
                    // Messages with a method but no id are notifications
                    Debug.Assert(union.Method is not null, "HasMethod should only be true when Method is non-null");
                    return new JsonRpcNotification
                    {
                        JsonRpc = union.JsonRpc,
                        Method = union.Method!,
                        Params = union.Params
                    };

                default:
                    throw new JsonException("Invalid JSON-RPC message format");
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

        /// <summary>
        /// Manually parses a JSON-RPC message from the reader into the Union struct.
        /// </summary>
        private static Union ParseUnion(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            var union = new Union
            {
                JsonRpc = string.Empty // Initialize to avoid null reference warnings
            };

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token");
            }

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
                        union.JsonRpc = reader.GetString() ?? string.Empty;
                        break;

                    case "id":
                        union.Id = JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<RequestId>());
                        union.HasId = true;
                        break;

                    case "method":
                        union.Method = reader.GetString();
                        union.HasMethod = union.Method is not null;
                        break;

                    case "params":
                        union.Params = JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonNode>());
                        break;

                    case "error":
                        union.Error = JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonRpcErrorDetail>());
                        union.HasError = union.Error is not null;
                        break;

                    case "result":
                        union.Result = JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonNode>());
                        union.HasResult = true;
                        break;

                    default:
                        // Skip unknown properties
                        reader.Skip();
                        break;
                }
            }

            return union;
        }

        /// <summary>
        /// Private struct to hold parsed JSON-RPC message data during deserialization.
        /// </summary>
        private struct Union
        {
            /// <summary>The JSON-RPC protocol version (must be "2.0").</summary>
            public string JsonRpc;
            /// <summary>The message identifier for requests and responses.</summary>
            public RequestId Id;
            /// <summary>The method name for requests and notifications.</summary>
            public string? Method;
            /// <summary>The parameters for requests and notifications.</summary>
            public JsonNode? Params;
            /// <summary>The error details for error responses.</summary>
            public JsonRpcErrorDetail? Error;
            /// <summary>The result for successful responses.</summary>
            public JsonNode? Result;
            /// <summary>Indicates whether an 'id' property was present.</summary>
            public bool HasId;
            /// <summary>Indicates whether a 'method' property was present.</summary>
            public bool HasMethod;
            /// <summary>Indicates whether an 'error' property was present.</summary>
            public bool HasError;
            /// <summary>Indicates whether a 'result' property was present.</summary>
            public bool HasResult;
        }
    }
}
