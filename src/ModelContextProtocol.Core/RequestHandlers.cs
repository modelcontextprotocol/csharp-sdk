using ModelContextProtocol.Protocol;
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
            TParams typedRequest = JsonSerializer.Deserialize(request.Params, requestTypeInfo)!;
            object? result = await handler(typedRequest, request, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(result, responseTypeInfo);
        };
    }

    /// <summary>
    /// Registers a handler that may return either a standard result or a <see cref="CreateTaskResult"/>
    /// for task-augmented execution.
    /// </summary>
    public void SetTaskAugmented<TParams, TResult>(
        string method,
        Func<TParams, JsonRpcRequest, CancellationToken, ValueTask<ResultOrCreatedTask<TResult>>> handler,
        JsonTypeInfo<TParams> requestTypeInfo,
        JsonTypeInfo<TResult> responseTypeInfo,
        JsonTypeInfo<CreateTaskResult> taskResultTypeInfo)
        where TResult : Result
    {
        Throw.IfNull(method);
        Throw.IfNull(handler);
        Throw.IfNull(requestTypeInfo);
        Throw.IfNull(responseTypeInfo);
        Throw.IfNull(taskResultTypeInfo);

        this[method] = async (request, cancellationToken) =>
        {
            TParams typedRequest = JsonSerializer.Deserialize(request.Params, requestTypeInfo)!;
            var augmented = await handler(typedRequest, request, cancellationToken).ConfigureAwait(false);

            if (augmented.IsTask)
            {
                // Guard against a misconfiguration where a handler opts into task-augmented
                // execution but the server has no task lifecycle handlers wired up. Without
                // tasks/get, a client that received a CreateTaskResult would have no way to
                // poll the task to completion. Configure McpServerOptions.TaskStore or set
                // the task handlers explicitly via McpServerOptions.Handlers.
                if (!ContainsKey(RequestMethods.TasksGet))
                {
                    throw new InvalidOperationException(
                        $"Handler for '{method}' returned a {nameof(CreateTaskResult)}, but the server has no " +
                        $"'{RequestMethods.TasksGet}' handler registered. Configure McpServerOptions.TaskStore " +
                        "or set the task handlers explicitly in McpServerOptions.Handlers before starting the server.");
                }

                return JsonSerializer.SerializeToNode(augmented.TaskCreated!, taskResultTypeInfo);
            }

            return JsonSerializer.SerializeToNode(augmented.Result!, responseTypeInfo);
        };
    }
}
