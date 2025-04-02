using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Shared;

internal sealed class RequestHandlers : Dictionary<string, Func<JsonRpcRequest, CancellationToken, Task<JsonNode?>>>
{
    /// <summary>
    /// Registers a handler for incoming requests of a specific method.
    /// </summary>
    /// <typeparam name="TRequest">Type of request payload</typeparam>
    /// <typeparam name="TResponse">Type of response payload (not full RPC response</typeparam>
    /// <param name="method">Method identifier to register for</param>
    /// <param name="handler">Handler to be called when a request with specified method identifier is received</param>
    /// <param name="requestTypeInfo">The JSON contract governing request serialization.</param>
    /// <param name="responseTypeInfo">The JSON contract governing response serialization.</param>
    public void Set<TRequest, TResponse>(
        string method,
        Func<TRequest?, CancellationToken, Task<TResponse>> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo)
    {
        Throw.IfNull(method);
        Throw.IfNull(handler);
        Throw.IfNull(requestTypeInfo);
        Throw.IfNull(responseTypeInfo);

        this[method] = async (request, cancellationToken) =>
        {
            TRequest? typedRequest = JsonSerializer.Deserialize(request.Params, requestTypeInfo);
            object? result = await handler(typedRequest, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(result, responseTypeInfo);
        };
    }
}
