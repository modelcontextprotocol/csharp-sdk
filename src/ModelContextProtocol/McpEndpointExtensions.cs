using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol;

/// <summary>Provides extension methods for interacting with an <see cref="IMcpEndpoint"/>.</summary>
public static class McpEndpointExtensions
{
    /// <summary>
    /// Sends a JSON-RPC request and attempts to deserialize the result to <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TParameters">The type of the request parameters to serialize from.</typeparam>
    /// <typeparam name="TResult">The type of the result to deserialize to.</typeparam>
    /// <param name="endpoint">The MCP client or server instance.</param>
    /// <param name="method">The JSON-RPC method name to invoke.</param>
    /// <param name="parameters">Object representing the request parameters.</param>
    /// <param name="requestId">The request id for the request.</param>
    /// <param name="serializerOptions">The options governing request serialization.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized result.</returns>
    public static Task<TResult> SendRequestAsync<TParameters, TResult>(
        this IMcpEndpoint endpoint,
        string method,
        TParameters parameters,
        JsonSerializerOptions? serializerOptions = null,
        RequestId? requestId = null,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        JsonTypeInfo<TParameters> paramsTypeInfo = serializerOptions.GetTypeInfo<TParameters>();
        JsonTypeInfo<TResult> resultTypeInfo = serializerOptions.GetTypeInfo<TResult>();
        return SendRequestAsync(endpoint, method, parameters, paramsTypeInfo, resultTypeInfo, requestId, cancellationToken);
    }

    /// <summary>
    /// Sends a JSON-RPC request and attempts to deserialize the result to <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TParameters">The type of the request parameters to serialize from.</typeparam>
    /// <typeparam name="TResult">The type of the result to deserialize to.</typeparam>
    /// <param name="endpoint">The MCP client or server instance.</param>
    /// <param name="method">The JSON-RPC method name to invoke.</param>
    /// <param name="parameters">Object representing the request parameters.</param>
    /// <param name="parametersTypeInfo">The type information for request parameter serialization.</param>
    /// <param name="resultTypeInfo">The type information for request parameter deserialization.</param>
    /// <param name="requestId">The request id for the request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized result.</returns>
    internal static async Task<TResult> SendRequestAsync<TParameters, TResult>(
        this IMcpEndpoint endpoint,
        string method,
        TParameters parameters,
        JsonTypeInfo<TParameters> parametersTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        RequestId? requestId = null,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        Throw.IfNull(endpoint);
        Throw.IfNullOrWhiteSpace(method);
        Throw.IfNull(parametersTypeInfo);
        Throw.IfNull(resultTypeInfo);

        JsonRpcRequest jsonRpcRequest = new()
        {
            Method = method,
            Params = JsonSerializer.SerializeToNode(parameters, parametersTypeInfo),
        };

        if (requestId is { } id)
        {
            jsonRpcRequest.Id = id;
        }

        JsonRpcResponse response = await endpoint.SendRequestAsync(jsonRpcRequest, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(response.Result, resultTypeInfo) ?? throw new JsonException("Unexpected JSON result in response.");
    }

    /// <summary>
    /// Sends a notification to the server with parameters.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="method">The notification method name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task SendNotificationAsync(this IMcpEndpoint client, string method, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);
        Throw.IfNullOrWhiteSpace(method);
        return client.SendMessageAsync(new JsonRpcNotification { Method = method }, cancellationToken);
    }

    /// <summary>
    /// Sends a notification to the server with parameters.
    /// </summary>
    /// <param name="endpoint">The MCP client or server instance.</param>
    /// <param name="method">The JSON-RPC method name to invoke.</param>
    /// <param name="parameters">Object representing the request parameters.</param>
    /// <param name="serializerOptions">The options governing request serialization.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task SendNotificationAsync<TParameters>(
        this IMcpEndpoint endpoint,
        string method,
        TParameters parameters,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        JsonTypeInfo<TParameters> parametersTypeInfo = serializerOptions.GetTypeInfo<TParameters>();
        return SendNotificationAsync(endpoint, method, parameters, parametersTypeInfo, cancellationToken);
    }

    /// <summary>
    /// Sends a notification to the server with parameters.
    /// </summary>
    /// <param name="endpoint">The MCP client or server instance.</param>
    /// <param name="method">The JSON-RPC method name to invoke.</param>
    /// <param name="parameters">Object representing the request parameters.</param>
    /// <param name="parametersTypeInfo">The type information for request parameter serialization.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    internal static Task SendNotificationAsync<TParameters>(
        this IMcpEndpoint endpoint,
        string method,
        TParameters parameters,
        JsonTypeInfo<TParameters> parametersTypeInfo,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(endpoint);
        Throw.IfNullOrWhiteSpace(method);
        Throw.IfNull(parametersTypeInfo);

        JsonNode? parametersJson = JsonSerializer.SerializeToNode(parameters, parametersTypeInfo);
        return endpoint.SendMessageAsync(new JsonRpcNotification { Method = method, Params = parametersJson }, cancellationToken);
    }

    /// <summary>Notifies the connected endpoint of progress.</summary>
    /// <param name="endpoint">The endpoint issuing the notification.</param>
    /// <param name="progressToken">The <see cref="ProgressToken"/> identifying the operation.</param>
    /// <param name="progress">The progress update to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the completion of the operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    public static Task NotifyProgressAsync(
        this IMcpEndpoint endpoint,
        ProgressToken progressToken,
        ProgressNotificationValue progress, 
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(endpoint);

        return endpoint.SendMessageAsync(new JsonRpcNotification()
        {
            Method = NotificationMethods.ProgressNotification,
            Params = JsonSerializer.SerializeToNode(new ProgressNotification
            {
                ProgressToken = progressToken,
                Progress = progress,
            }, McpJsonUtilities.JsonContext.Default.ProgressNotification),
        }, cancellationToken);
    }
}
