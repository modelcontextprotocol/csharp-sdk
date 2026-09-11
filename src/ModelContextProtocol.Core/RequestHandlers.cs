using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol;

internal readonly record struct RequestHandlerResult(JsonNode? Json, bool IsProtocolResult = false, bool IsCacheable = false);

internal sealed class RequestHandlers : Dictionary<string, Func<JsonRpcRequest, CancellationToken, Task<RequestHandlerResult>>>
{
    private readonly Func<JsonRpcRequest, RequestHandlerResult, JsonNode?>? _prepareResponseForEmission;

    public RequestHandlers(Func<JsonRpcRequest, RequestHandlerResult, JsonNode?>? prepareResponseForEmission = null) =>
        _prepareResponseForEmission = prepareResponseForEmission;

    public JsonNode? PrepareForEmission(JsonRpcRequest request, RequestHandlerResult result) =>
        _prepareResponseForEmission?.Invoke(request, result) ?? result.Json;

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
            TParams typedRequest = JsonSerializer.Deserialize(request.Params, requestTypeInfo)!;
            TResult result = await handler(typedRequest, request, cancellationToken).ConfigureAwait(false);
            JsonNode? resultNode = JsonSerializer.SerializeToNode(result, responseTypeInfo);
            return new(resultNode, result is Result, result is ICacheableResult);
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
            TParams typedRequest = JsonSerializer.Deserialize(request.Params, requestTypeInfo)!;
            var augmented = await handler(typedRequest, request, cancellationToken).ConfigureAwait(false);

            if (augmented.IsAlternate)
            {
                var result = augmented.Alternate!;
                JsonNode? resultNode = JsonSerializer.SerializeToNode(result, augmented.AlternateTypeInfo!);
                return new(resultNode, IsProtocolResult: true, IsCacheable: result is ICacheableResult);
            }

            var immediateResult = augmented.Result!;
            JsonNode? immediateResultNode = JsonSerializer.SerializeToNode(immediateResult, responseTypeInfo);
            return new(immediateResultNode, IsProtocolResult: true, IsCacheable: immediateResult is ICacheableResult);
        };
    }
#pragma warning restore MCPEXP002
}
