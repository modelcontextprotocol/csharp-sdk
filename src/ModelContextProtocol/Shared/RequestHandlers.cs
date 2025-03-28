using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;

namespace ModelContextProtocol.Shared;

internal sealed class RequestHandlers : Dictionary<string, Func<JsonRpcRequest, CancellationToken, Task<object?>>>
{
    /// <summary>
    /// Registers a handler for incoming requests of a specific method.
    /// </summary>
    /// <typeparam name="TRequest">Type of request payload</typeparam>
    /// <typeparam name="TResponse">Type of response payload (not full RPC response</typeparam>
    /// <param name="method">Method identifier to register for</param>
    /// <param name="handler">Handler to be called when a request with specified method identifier is received</param>
    public void Set<TRequest, TResponse>(string method, Func<TRequest?, CancellationToken, Task<TResponse>> handler)
    {
        Throw.IfNull(method);
        Throw.IfNull(handler);

        this[method] = async (request, cancellationToken) =>
        {
            // Convert the params JsonElement to our type using the same options
            var jsonString = JsonSerializer.Serialize(request.Params, McpJsonUtilities.DefaultOptions.GetTypeInfo<object?>());
            var typedRequest = JsonSerializer.Deserialize(jsonString, McpJsonUtilities.DefaultOptions.GetTypeInfo<TRequest>());

            return await handler(typedRequest, cancellationToken).ConfigureAwait(false);
        };
    }
}
