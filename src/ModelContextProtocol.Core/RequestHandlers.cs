using ModelContextProtocol.Protocol;
using System.Net.ServerSentEvents;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol;

internal sealed class RequestHandlers : Dictionary<string, Func<JsonRpcRequest, CancellationToken, Task<JsonNode?>>>
{
    /// <summary>
    /// Registers a handler for incoming requests of a specific method in the MCP protocol.
    /// </summary>
    /// <typeparam name="TParams">Type of request payload that will be deserialized from incoming JSON</typeparam>
    /// <typeparam name="TResult">Type of response payload that will be serialized to JSON (not full RPC response)</typeparam>
    /// <param name="method">Method identifier to register for (e.g., "tools/list", "logging/setLevel")</param>
    /// <param name="handler">Handler function to be called when a request with the specified method identifier is received</param>
    /// <param name="requestTypeInfo">The JSON contract governing request parameter deserialization</param>
    /// <param name="responseTypeInfo">The JSON contract governing response serialization</param>
    /// <remarks>
    /// <para>
    /// This method is used internally by the MCP infrastructure to register handlers for various protocol methods.
    /// When an incoming request matches the specified method, the registered handler will be invoked with the
    /// deserialized request parameters.
    /// </para>
    /// <para>
    /// The handler function receives the deserialized request object, the full JSON-RPC request, and a cancellation token,
    /// and should return a response object that will be serialized back to the client.
    /// </para>
    /// </remarks>
    public void Set<TParams, TResult>(
        string method,
        Func<TParams, JsonRpcRequest, CancellationToken, ValueTask<TResult>> handler,
        JsonTypeInfo<TParams> requestTypeInfo,
        JsonTypeInfo<TResult> responseTypeInfo)
    {
        Throw.IfNull(method);
        Throw.IfNull(handler);
        Throw.IfNull(requestTypeInfo);
        Throw.IfNull(responseTypeInfo);

        this[method] = async (request, cancellationToken) =>
        {
            TParams typedRequest = DeserializeParams(request.Params, requestTypeInfo)!;
            object? result = await handler(typedRequest, request, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(result, responseTypeInfo);
        };
    }

#pragma warning disable MCPEXP002 // SetWithAlternate consumes the experimental ResultOrAlternate seam
    /// <summary>
    /// Registers a handler that may return either a standard result or an alternate <see cref="Result"/>
    /// subtype for scenarios like task-augmented execution.
    /// </summary>
    public void SetWithAlternate<TParams, TResult>(
        string method,
        Func<TParams, JsonRpcRequest, CancellationToken, ValueTask<ResultOrAlternate<TResult>>> handler,
        JsonTypeInfo<TParams> requestTypeInfo,
        JsonTypeInfo<TResult> responseTypeInfo)
        where TResult : Result
    {
        Throw.IfNull(method);
        Throw.IfNull(handler);
        Throw.IfNull(requestTypeInfo);
        Throw.IfNull(responseTypeInfo);

        this[method] = async (request, cancellationToken) =>
        {
            TParams typedRequest = DeserializeParams(request.Params, requestTypeInfo)!;
            var augmented = await handler(typedRequest, request, cancellationToken).ConfigureAwait(false);

            if (augmented.IsAlternate)
            {
                return JsonSerializer.SerializeToNode(augmented.Alternate!, augmented.AlternateTypeInfo!);
            }

            return JsonSerializer.SerializeToNode(augmented.Result!, responseTypeInfo);
        };
    }
#pragma warning restore MCPEXP002

    /// <summary>
    /// Deserializes request parameters into their typed representation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deserializing a <see cref="JsonNode"/> directly re-serializes it and then re-parses every property. For the
    /// <see cref="RequestParams.Meta"/> property this means System.Text.Json copies the entire <c>_meta</c> payload
    /// into a new, unpooled buffer, which for large metadata lands on the large object heap on every request.
    /// </para>
    /// <para>
    /// When <c>_meta</c> is an object, it is therefore excluded from the re-parse and assigned afterwards via
    /// <see cref="JsonNode.DeepClone"/>. The params node is backed by a non-disposable document, so cloning shares
    /// the already-parsed UTF-8 bytes instead of copying them while still producing an independent node.
    /// Any other shape uses the standard deserialization path so behavior, including errors, is unchanged.
    /// </para>
    /// </remarks>
    internal static TParams? DeserializeParams<TParams>(JsonNode? parameters, JsonTypeInfo<TParams> typeInfo)
    {
        if (parameters is not JsonObject paramsObject ||
            typeInfo.Kind is not JsonTypeInfoKind.Object ||
            !typeof(RequestParams).IsAssignableFrom(typeof(TParams)) ||
            !paramsObject.TryGetPropertyValue("_meta", out JsonNode? metaNode) ||
            metaNode is not JsonObject meta)
        {
            return JsonSerializer.Deserialize(parameters, typeInfo);
        }

        using var buffer = new PooledByteBufferWriter();
        try
        {
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (KeyValuePair<string, JsonNode?> property in paramsObject)
                {
                    if (property.Key == "_meta")
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Key);
                    if (property.Value is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        property.Value.WriteTo(writer, typeInfo.Options);
                    }
                }

                writer.WriteEndObject();
            }

            TParams? result = JsonSerializer.Deserialize(buffer.WrittenMemory.Span, typeInfo);
            if (result is RequestParams requestParams)
            {
                requestParams.Meta = (JsonObject)meta.DeepClone();
            }

            return result;
        }
        finally
        {
            // The buffer is returned to a shared pool; don't leave request content behind in it.
            MemoryMarshal.AsMemory(buffer.WrittenMemory).Span.Clear();
        }
    }
}
