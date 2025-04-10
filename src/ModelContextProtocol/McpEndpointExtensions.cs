using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol;

/// <summary>
/// Provides extension methods for interacting with an <see cref="IMcpEndpoint"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class provides strongly-typed methods for working with the Model Context Protocol (MCP) endpoints,
/// simplifying JSON-RPC communication by handling serialization and deserialization of parameters and results.
/// </para>
/// <para>
/// These extension methods are designed to be used with both client (<see cref="IMcpClient"/>) and
/// server (<see cref="IMcpServer"/>) implementations of the <see cref="IMcpEndpoint"/> interface.
/// </para>
/// </remarks>
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
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
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
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
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
    /// Sends a parameterless notification to the connected endpoint.
    /// </summary>
    /// <param name="client">The MCP client or server instance.</param>
    /// <param name="method">The notification method name.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// <para>
    /// This method sends a notification without any parameters. Notifications are one-way messages 
    /// that don't expect a response. They are commonly used for events, status updates, or to signal 
    /// changes in state.
    /// </para>
    /// </remarks>
    public static Task SendNotificationAsync(this IMcpEndpoint client, string method, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);
        Throw.IfNullOrWhiteSpace(method);
        return client.SendMessageAsync(new JsonRpcNotification { Method = method }, cancellationToken);
    }

    /// <summary>
    /// Sends a notification with parameters to the connected endpoint.
    /// </summary>
    /// <typeparam name="TParameters">The type of the notification parameters to serialize.</typeparam>
    /// <param name="endpoint">The MCP client or server instance.</param>
    /// <param name="method">The JSON-RPC method name for the notification.</param>
    /// <param name="parameters">Object representing the notification parameters.</param>
    /// <param name="serializerOptions">The options governing parameter serialization. If null, default options are used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// <para>
    /// This method sends a notification with parameters to the connected endpoint. Notifications are one-way 
    /// messages that don't expect a response, commonly used for events, status updates, or signaling changes.
    /// </para>
    /// <para>
    /// The parameters object is serialized to JSON according to the provided serializer options or the default 
    /// options if none are specified.
    /// </para>
    /// <para>
    /// The Model Context Protocol defines several standard notification methods in <see cref="NotificationMethods"/>,
    /// but custom methods can also be used for application-specific notifications.
    /// </para>
    /// </remarks>
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
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
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

    /// <summary>
    /// Notifies the connected endpoint of progress for a long-running operation.
    /// </summary>
    /// <param name="endpoint">The endpoint issuing the notification.</param>
    /// <param name="progressToken">The <see cref="ProgressToken"/> identifying the operation for which progress is being reported.</param>
    /// <param name="progress">The progress update to send, containing information such as percentage complete or status message.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the completion of the notification operation (not the operation being tracked).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method sends a progress notification to the connected endpoint using the Model Context Protocol's
    /// standardized progress notification format. Progress updates are identified by a <see cref="ProgressToken"/>
    /// that allows the recipient to correlate multiple updates with a specific long-running operation.
    /// </para>
    /// <para>
    /// Progress notifications are sent asynchronously and don't block the operation from continuing.
    /// </para>
    /// </remarks>
    public static Task NotifyProgressAsync(
        this IMcpEndpoint endpoint,
        ProgressToken progressToken,
        ProgressNotificationValue progress, 
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(endpoint);

        return endpoint.SendNotificationAsync(
            NotificationMethods.ProgressNotification,
            new ProgressNotification
            {
                ProgressToken = progressToken,
                Progress = progress,
            },
            McpJsonUtilities.JsonContext.Default.ProgressNotification,
            cancellationToken);
    }
}
