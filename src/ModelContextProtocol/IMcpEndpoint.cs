using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol;

/// <summary>
/// Represents a client or server Model Context Protocol (MCP) endpoint for JSON-RPC communication.
/// </summary>
/// <remarks>
/// <para>
/// The MCP endpoint provides the core communication functionality used by both clients and servers:
/// <list type="bullet">
///   <item>Sending JSON-RPC requests and receiving responses</item>
///   <item>Sending notifications to the connected endpoint</item>
///   <item>Registering handlers for receiving notifications</item>
/// </list>
/// </para>
/// <para>
/// <see cref="IMcpEndpoint"/> serves as the base interface for both <see cref="Client.IMcpClient"/> and 
/// <see cref="Server.IMcpServer"/> interfaces, providing the common functionality needed for MCP protocol 
/// communication. Most applications will use these more specific interfaces rather than working with 
/// <see cref="IMcpEndpoint"/> directly.
/// </para>
/// <para>
/// All MCP endpoints should be properly disposed after use as they implement <see cref="IAsyncDisposable"/>.
/// </para>
/// <example>
/// <code>
/// // Example of using endpoint extension methods:
/// // Send a notification without parameters
/// await endpoint.SendNotificationAsync("progress/update", cancellationToken);
/// 
/// // Send a notification with parameters
/// await endpoint.SendNotificationAsync("notifications/message", new
/// {
///     Level = "info",
///     Data = "Operation completed successfully"
/// }, cancellationToken);
/// 
/// // Example of registering a notification handler:
/// await using var handler = endpoint.RegisterNotificationHandler("progress/update", 
///     (notification, token) => {
///         Console.WriteLine($"Received progress update: {notification.Params}");
///         return Task.CompletedTask;
///     });
/// </code>
/// </example>
/// </remarks>
public interface IMcpEndpoint : IAsyncDisposable
{
    /// <summary>
    /// Sends a JSON-RPC request to the connected endpoint and waits for a response.
    /// </summary>
    /// <param name="request">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the endpoint's response.</returns>
    /// <exception cref="McpException">Thrown when the transport is not connected, or another error occurs during request processing.</exception>
    /// <remarks>
    /// <para>
    /// This method provides low-level access to send raw JSON-RPC requests. For most use cases,
    /// consider using the strongly-typed extension methods that provide a more convenient API, such as:
    /// <code>
    /// // Using typed extension methods instead of raw request:
    /// var result = await endpoint.SendRequestAsync&lt;MyParams, MyResult&gt;("method.name", parameters);
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Creating and sending a raw JSON-RPC request
    /// var request = new JsonRpcRequest
    /// {
    ///     Method = "weather.getTemperature",
    ///     Params = JsonNode.Parse(@"{""location"": ""Seattle""}"),
    ///     Id = new RequestId(1)
    /// };
    /// 
    /// JsonRpcResponse response = await endpoint.SendRequestAsync(request);
    /// 
    /// // Check if we got a success or error response
    /// if (response.Error != null)
    /// {
    ///     Console.WriteLine($"Error: {response.Error.Message}");
    /// }
    /// else
    /// {
    ///     // Process the result
    ///     double temperature = response.Result.Deserialize&lt;double&gt;();
    ///     Console.WriteLine($"The temperature is {temperature}°C");
    /// }
    /// </code>
    /// </example>
    Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a JSON-RPC message to the connected endpoint.
    /// </summary>
    /// <param name="message">The JSON-RPC message to send. This can be any type that implements IJsonRpcMessage, such as JsonRpcRequest, JsonRpcResponse, JsonRpcNotification, or JsonRpcError.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <exception cref="McpException">Thrown when the transport is not connected.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the message is null.</exception>
    /// <remarks>
    /// <para>
    /// This method provides low-level access to send any JSON-RPC message. For specific message types,
    /// consider using the higher-level methods such as <see cref="SendRequestAsync"/> or extension methods
    /// like SendNotificationAsync() which provide a simpler API.
    /// </para>
    /// <para>
    /// The method will serialize the message and transmit it using the underlying transport mechanism.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Sending a notification
    /// await endpoint.SendMessageAsync(new JsonRpcNotification { 
    ///     Method = "progress/update", 
    ///     Params = JsonContent.Create(new { progress = 0.5 })
    /// }, cancellationToken);
    /// 
    /// // Sending an error response
    /// await endpoint.SendMessageAsync(new JsonRpcError {
    ///     Id = requestMessage.Id,
    ///     Error = new JsonRpcErrorDetail {
    ///         Code = ErrorCodes.InvalidParams,
    ///         Message = "Invalid parameters provided"
    ///     }
    /// }, cancellationToken);
    /// </code>
    /// </example>
    Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <summary>Registers a handler to be invoked when a notification for the specified method is received.</summary>
    /// <param name="method">The notification method.</param>
    /// <param name="handler">The handler to be invoked.</param>
    /// <returns>An <see cref="IDisposable"/> that will remove the registered handler when disposed.</returns>
    IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, Task> handler);
}
