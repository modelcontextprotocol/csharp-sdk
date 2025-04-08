using ModelContextProtocol.Protocol.Messages;
using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.Logging;

/// <summary>
/// Logging methods for the ModelContextProtocol library.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Server {endpointName} capabilities received: {capabilities}, server info: {serverInfo}")]
    internal static partial void ServerCapabilitiesReceived(this ILogger logger, string endpointName, string capabilities, string serverInfo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating client for {endpointName}")]
    internal static partial void CreatingClient(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client for {endpointName} created and connected")]
    internal static partial void ClientCreated(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Client server {endpointName} already initializing")]
    internal static partial void ClientAlreadyInitializing(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client server {endpointName} already initialized")]
    internal static partial void ClientAlreadyInitialized(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Client server {endpointName} initialization error")]
    internal static partial void ClientInitializationError(this ILogger logger, string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Server {endpointName} protocol version mismatch, expected {expected}, received {received}")]
    internal static partial void ServerProtocolVersionMismatch(this ILogger logger, string endpointName, string expected, string received);

    [LoggerMessage(Level = LogLevel.Error, Message = "Client {endpointName} initialization timeout")]
    internal static partial void ClientInitializationTimeout(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Endpoint message processing cancelled for {endpointName}")]
    internal static partial void EndpointMessageProcessingCancelled(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request handler called for {endpointName} with method {method}")]
    internal static partial void RequestHandlerCalled(this ILogger logger, string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request handler completed for {endpointName} with method {method}")]
    internal static partial void RequestHandlerCompleted(this ILogger logger, string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request handler error for {endpointName} with method {method}")]
    internal static partial void RequestHandlerError(this ILogger logger, string endpointName, string method, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No request found for message with ID {messageWithId} for {endpointName}")]
    internal static partial void NoRequestFoundForMessageWithId(this ILogger logger, string endpointName, string messageWithId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The request has not valid message ID for {endpointName}")]
    internal static partial void RequestHasInvalidId(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification handler error for {endpointName} with method {method}")]
    internal static partial void NotificationHandlerError(this ILogger logger, string endpointName, string method, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Client not connected for {endpointName}")]
    internal static partial void ClientNotConnected(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Endpoint not connected for {endpointName}")]
    internal static partial void EndpointNotConnected(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sending request payload for {endpointName}: {payload}")]
    internal static partial void SendingRequestPayload(this ILogger logger, string endpointName, string payload);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sending request for {endpointName} with method {method}")]
    internal static partial void SendingRequest(this ILogger logger, string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request failed for {endpointName} with method {method}: {message} ({code})")]
    internal static partial void RequestFailed(this ILogger logger, string endpointName, string method, string message, int code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request '{requestId}' canceled via client notification with reason '{Reason}'.")]
    internal static partial void RequestCanceled(this ILogger logger, RequestId requestId, string? reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request response received payload for {endpointName}: {payload}")]
    internal static partial void RequestResponseReceivedPayload(this ILogger logger, string endpointName, string payload);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request response received for {endpointName} with method {method}")]
    internal static partial void RequestResponseReceived(this ILogger logger, string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request invalid response type for {endpointName} with method {method}")]
    internal static partial void RequestInvalidResponseType(this ILogger logger, string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaning up endpoint {endpointName}")]
    internal static partial void CleaningUpEndpoint(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Endpoint cleaned up for {endpointName}")]
    internal static partial void EndpointCleanedUp(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport for {endpointName} already connected")]
    internal static partial void TransportAlreadyConnected(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport for {endpointName} connecting")]
    internal static partial void TransportConnecting(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating process for transport for {endpointName} with command {command}, arguments {arguments}, environment {environment}, working directory {workingDirectory}, shutdown timeout {shutdownTimeout}")]
    internal static partial void CreateProcessForTransport(this ILogger logger, string endpointName, string command, string? arguments, string environment, string workingDirectory, string shutdownTimeout);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport for {endpointName} received stderr log: {data}")]
    internal static partial void ReadStderr(this ILogger logger, string endpointName, string data);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport process start failed for {endpointName}")]
    internal static partial void TransportProcessStartFailed(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport process started for {endpointName} with PID {processId}")]
    internal static partial void TransportProcessStarted(this ILogger logger, string endpointName, int processId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport reading messages for {endpointName}")]
    internal static partial void TransportReadingMessages(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport connect failed for {endpointName}")]
    internal static partial void TransportConnectFailed(this ILogger logger, string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport not connected for {endpointName}")]
    internal static partial void TransportNotConnected(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport sending message for {endpointName} with ID {messageId}, JSON {json}")]
    internal static partial void TransportSendingMessage(this ILogger logger, string endpointName, string messageId, string? json = null);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport message sent for {endpointName} with ID {messageId}")]
    internal static partial void TransportSentMessage(this ILogger logger, string endpointName, string messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport send failed for {endpointName} with ID {messageId}")]
    internal static partial void TransportSendFailed(this ILogger logger, string endpointName, string messageId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport entering read messages loop for {endpointName}")]
    internal static partial void TransportEnteringReadMessagesLoop(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport waiting for message for {endpointName}")]
    internal static partial void TransportWaitingForMessage(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport end of stream for {endpointName}")]
    internal static partial void TransportEndOfStream(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport received message for {endpointName}: {line}")]
    internal static partial void TransportReceivedMessage(this ILogger logger, string endpointName, string line);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport received message parsed for {endpointName}: {messageId}")]
    internal static partial void TransportReceivedMessageParsed(this ILogger logger, string endpointName, string messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport message written for {endpointName} with ID {messageId}")]
    internal static partial void TransportMessageWritten(this ILogger logger, string endpointName, string messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport message parse failed due to unexpected message schema for {endpointName}: {line}")]
    internal static partial void TransportMessageParseUnexpectedType(this ILogger logger, string endpointName, string line);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport message parse failed for {endpointName}: {line}")]
    internal static partial void TransportMessageParseFailed(this ILogger logger, string endpointName, string line, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport exiting read messages loop for {endpointName}")]
    internal static partial void TransportExitingReadMessagesLoop(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport read messages cancelled for {endpointName}")]
    internal static partial void TransportReadMessagesCancelled(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport read messages failed for {endpointName}")]
    internal static partial void TransportReadMessagesFailed(this ILogger logger, string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport cleaning up for {endpointName}")]
    internal static partial void TransportCleaningUp(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Transport closing stdin for {endpointName}")]
    internal static partial void TransportClosingStdin(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Transport waiting for shutdown for {endpointName}")]
    internal static partial void TransportWaitingForShutdown(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport shutdown failed for {endpointName}")]
    internal static partial void TransportShutdownFailed(this ILogger logger, string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Transport waiting for read task for {endpointName}")]
    internal static partial void TransportWaitingForReadTask(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transport cleanup read task timeout for {endpointName}")]
    internal static partial void TransportCleanupReadTaskTimeout(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport cleanup read task cancelled for {endpointName}")]
    internal static partial void TransportCleanupReadTaskCancelled(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport cleanup read task failed for {endpointName}")]
    internal static partial void TransportCleanupReadTaskFailed(this ILogger logger, string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport read task cleaned up for {endpointName}")]
    internal static partial void TransportReadTaskCleanedUp(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport cleaned up for {endpointName}")]
    internal static partial void TransportCleanedUp(this ILogger logger, string endpointName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sending message to {endpointName}: {message}")]
    internal static partial void SendingMessage(this ILogger logger, string endpointName, string message);

    [LoggerMessage(
        EventId = 7000,
        Level = LogLevel.Error,
        Message = "Transport connection error for {endpointName}"
    )]
    public static partial void TransportConnectionError(
        this ILogger logger,
        string endpointName,
        Exception exception);

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Warning,
        Message = "Transport message received before connected for {endpointName}: {data}"
    )]
    public static partial void TransportMessageReceivedBeforeConnected(
        this ILogger logger,
        string endpointName,
        string data);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Error,
        Message = "Transport endpoint event received out of order for {endpointName}: {data}"
    )]
    public static partial void TransportEndpointEventInvalid(
        this ILogger logger,
        string endpointName,
        string data);

    [LoggerMessage(
        EventId = 7003,
        Level = LogLevel.Error,
        Message = "Transport event parse failed for {endpointName}: {data}"
    )]
    public static partial void TransportEndpointEventParseFailed(
        this ILogger logger,
        string endpointName,
        string data,
        Exception exception);

    /// <summary>
    /// Logs an error that occurred during JSON-RPC message handling.
    /// </summary>
    /// <param name="logger">The logger to write the error message to.</param>
    /// <param name="endpointName">The name of the endpoint where the error occurred.</param>
    /// <param name="messageType">The type name of the message that was being processed.</param>
    /// <param name="payload">The serialized JSON content of the message for debugging.</param>
    /// <param name="exception">The exception that was thrown during message handling.</param>
    /// <remarks>
    /// <para>
    /// This method logs at Error level (EventId 7008) and captures details about unexpected exceptions
    /// that occur during the processing of JSON-RPC messages that aren't handled by more specific
    /// error logging methods.
    /// </para>
    /// <para>
    /// Example usage:
    /// </para>
    /// <code>
    /// try 
    /// {
    ///     await HandleMessageAsync(message, cancellationToken);
    /// }
    /// catch (Exception ex) when (ex is not OperationCanceledException)
    /// {
    ///     var payload = JsonSerializer.Serialize(message, McpJsonUtilities.JsonContext.Default.IJsonRpcMessage);
    ///     _logger.MessageHandlerError(EndpointName, message.GetType().Name, payload, ex);
    /// }
    /// </code>
    /// </remarks>
    [LoggerMessage(
        EventId = 7008,
        Level = LogLevel.Error,
        Message = "Message handler error for {endpointName} with message type {messageType}, payload {payload}"
    )]
    public static partial void MessageHandlerError(
        this ILogger logger,
        string endpointName,
        string messageType,
        string payload,
        Exception exception);

    [LoggerMessage(
        EventId = 7009,
        Level = LogLevel.Trace,
        Message = "Writing message to channel: {message}"
    )]
    public static partial void TransportWritingMessageToChannel(
        this ILogger logger,
        IJsonRpcMessage message);

    /// <summary>
    /// Logs when a message has been successfully written to a transport channel.
    /// </summary>
    /// <param name="logger">The logger to write the message to.</param>
    /// <remarks>
    /// This method logs at Trace level and is primarily used for debugging transport-level
    /// message flow. The message contains no parameters and simply indicates that a message 
    /// was written to the channel.
    /// </remarks>
    [LoggerMessage(
        EventId = 7010,
        Level = LogLevel.Trace,
        Message = "Message written to channel"
    )]
    public static partial void TransportMessageWrittenToChannel(this ILogger logger);

    [LoggerMessage(
        EventId = 7011,
        Level = LogLevel.Trace,
        Message = "Message read from channel for {endpointName} with type {messageType}"
    )]
    public static partial void TransportMessageRead(
        this ILogger logger,
        string endpointName,
        string messageType);

    /// <summary>
    /// Logs a warning when no handler is found for a JSON-RPC request method.
    /// </summary>
    /// <param name="logger">The logger to write the warning message to.</param>
    /// <param name="endpointName">The name of the endpoint that received the request.</param>
    /// <param name="method">The method name in the request that has no registered handler.</param>
    /// <remarks>
    /// <para>
    /// This method logs at Warning level (EventId 7012) when a JSON-RPC request is received
    /// but no handler has been registered for the requested method. This typically indicates
    /// a client is requesting a capability that the server doesn't support.
    /// </para>
    /// <para>
    /// Example usage:
    /// </para>
    /// <code>
    /// if (!_requestHandlers.TryGetValue(request.Method, out var handler))
    /// {
    ///     _logger.NoHandlerFoundForRequest(EndpointName, request.Method);
    ///     throw new McpException("The method does not exist or is not available.", ErrorCodes.MethodNotFound);
    /// }
    /// </code>
    /// </remarks>
    [LoggerMessage(
        EventId = 7012,
        Level = LogLevel.Warning,
        Message = "No handler found for request {method} for server {endpointName}"
    )]
    public static partial void NoHandlerFoundForRequest(
        this ILogger logger,
        string endpointName,
        string method);

    /// <summary>
    /// Logs at trace level when a response message has been successfully matched to a pending request by its message ID.
    /// </summary>
    /// <param name="logger">The logger to write the trace message to.</param>
    /// <param name="endpointName">The name of the endpoint that received the response message.</param>
    /// <param name="messageId">The unique identifier of the message that was matched to a pending request.</param>
    /// <remarks>
    /// <para>
    /// This method logs at Trace level (EventId 7013) when the JSON-RPC message correlation system 
    /// successfully identifies a response message that corresponds to a previously sent request.
    /// This is part of the request/response tracking mechanism in the MCP framework.
    /// </para>
    /// <para>
    /// Example usage:
    /// </para>
    /// <code>
    /// if (_pendingRequests.TryRemove(messageWithId.Id, out var tcs))
    /// {
    ///     _logger.ResponseMatchedPendingRequest(EndpointName, messageWithId.Id.ToString());
    ///     tcs.TrySetResult(message);
    /// }
    /// </code>
    /// </remarks>
    [LoggerMessage(
        EventId = 7013,
        Level = LogLevel.Trace,
        Message = "Response matched pending request for {endpointName} with ID {messageId}"
    )]
    public static partial void ResponseMatchedPendingRequest(
        this ILogger logger,
        string endpointName,
        string messageId);

    /// <summary>
    /// Logs a warning when an endpoint handler receives a message with an unexpected type.
    /// This occurs when a message doesn't match any of the expected JSON-RPC message types
    /// (request, notification, or message with ID).
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="endpointName">The name of the endpoint that received the message.</param>
    /// <param name="messageType">The type name of the unexpected message.</param>
    [LoggerMessage(
        EventId = 7014,
        Level = LogLevel.Warning,
        Message = "Endpoint handler received unexpected message type for {endpointName}: {messageType}"
    )]
    public static partial void EndpointHandlerUnexpectedMessageType(
        this ILogger logger,
        string endpointName,
        string messageType);

    /// <summary>
    /// Logs a debug message when a request has been sent and the system is waiting for a response.
    /// </summary>
    /// <param name="logger">The logger to write the message to.</param>
    /// <param name="endpointName">The name of the endpoint the request was sent to.</param>
    /// <param name="method">The method name in the request.</param>
    /// <param name="id">The ID of the request being tracked.</param>
    /// <remarks>
    /// <para>
    /// This method logs at Debug level and tracks that a request has been successfully sent through
    /// the transport layer and the system is now awaiting a response.
    /// </para>
    /// <para>
    /// Example usage:
    /// </para>
    /// <code>
    /// await _transport.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);
    /// 
    /// _logger.RequestSentAwaitingResponse(EndpointName, request.Method, request.Id.ToString());
    /// var response = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    /// </code>
    /// </remarks>
    [LoggerMessage(
        EventId = 7015,
        Level = LogLevel.Debug,
        Message = "Request sent for {endpointName} with method {method}, ID {id}. Waiting for response."
    )]
    public static partial void RequestSentAwaitingResponse(
        this ILogger logger,
        string endpointName,
        string method,
        string id);

    /// <summary>
    /// Logs an error when a server initialization attempt is made while the server is already initializing.
    /// </summary>
    /// <param name="logger">The logger to write the error message to.</param>
    /// <param name="endpointName">The name of the endpoint that is already initializing.</param>
    /// <remarks>
    /// <para>
    /// This method logs at Error level (EventId 7016) when a second attempt is made to initialize
    /// a server that is already in the initialization process. This typically occurs when multiple
    /// initialization requests are sent to the same server endpoint concurrently.
    /// </para>
    /// <para>
    /// During the initialization sequence, a server can only process one initialization request at a time.
    /// Attempting to start initialization when it's already in progress indicates a potential race condition
    /// or improper sequencing of client-server communication.
    /// </para>
    /// <para>
    /// Example usage:
    /// </para>
    /// <code>
    /// if (_isInitializing)
    /// {
    ///     _logger.ServerAlreadyInitializing(EndpointName);
    ///     throw new McpException("Server is already initializing", ErrorCodes.ServerAlreadyInitialized);
    /// }
    /// 
    /// _isInitializing = true;
    /// </code>
    /// </remarks>
    [LoggerMessage(
        EventId = 7016,
        Level = LogLevel.Error,
        Message = "{endpointName} already initializing"
    )]
    public static partial void ServerAlreadyInitializing(
        this ILogger logger,
        string endpointName);

    /// <summary>
    /// Logs an error that occurs during server initialization.
    /// </summary>
    /// <param name="logger">The logger to write the error message to.</param>
    /// <param name="endpointName">The name of the endpoint where the initialization error occurred.</param>
    /// <param name="e">The exception that was thrown during server initialization.</param>
    /// <remarks>
    /// <para>
    /// This method logs at Error level (EventId 7017) when an error occurs during the initialization
    /// of a server endpoint. This typically indicates a problem with establishing the initial connection
    /// or handshake with the server component.
    /// </para>
    /// <para>
    /// Example usage:
    /// </para>
    /// <code>
    /// try
    /// {
    ///     await InitializeServerAsync(cancellationToken).ConfigureAwait(false);
    /// }
    /// catch (Exception ex) when (ex is not OperationCanceledException)
    /// {
    ///     _logger.ServerInitializationError(EndpointName, ex);
    ///     throw;
    /// }
    /// </code>
    /// </remarks>
    [LoggerMessage(
        EventId = 7017,
        Level = LogLevel.Error,
        Message = "{endpointName} initialization error"
    )]
    public static partial void ServerInitializationError(
        this ILogger logger,
        string endpointName,
        Exception e);

    /// <summary>
    /// Logs at debug level when a POST request to the SSE transport has been accepted by the server.
    /// </summary>
    /// <param name="logger">The logger to write the debug message to.</param>
    /// <param name="endpointName">The name of the endpoint that sent the POST request.</param>
    /// <param name="messageId">The unique identifier of the message that was accepted.</param>
    /// <remarks>
    /// <para>
    /// This method logs at Debug level (EventId 7018) when a JSON-RPC message sent via HTTP POST 
    /// to an SSE endpoint has been accepted by the server. The server responds with "accepted" 
    /// to indicate that it has received the message and will process it asynchronously.
    /// </para>
    /// <para>
    /// The actual response to the message will be delivered later via the Server-Sent Events (SSE) stream.
    /// This logging helps track the asynchronous message flow in the SSE transport implementation.
    /// </para>
    /// <para>
    /// Example usage:
    /// </para>
    /// <code>
    /// if (responseContent.Equals("accepted", StringComparison.OrdinalIgnoreCase))
    /// {
    ///     _logger.SSETransportPostAccepted(EndpointName, messageId);
    /// }
    /// </code>
    /// </remarks>
    [LoggerMessage(
        EventId = 7018,
        Level = LogLevel.Debug,
        Message = "SSE transport POST accepted for {endpointName} with message ID {messageId}"
    )]
    public static partial void SSETransportPostAccepted(
        this ILogger logger,
        string endpointName,
        string messageId);

    [LoggerMessage(
        EventId = 7019,
        Level = LogLevel.Error,
        Message = "SSE transport POST not accepted for {endpointName} with message ID {messageId} and server response {responseContent}"
    )]
    public static partial void SSETransportPostNotAccepted(
        this ILogger logger,
        string endpointName,
        string messageId,
        string responseContent);

    /// <summary>
    /// Logs the byte representation of a message in UTF-8 encoding.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="endpointName">The name of the endpoint.</param>
    /// <param name="byteRepresentation">The byte representation as a hex string.</param>
    [LoggerMessage(EventId = 39000, Level = LogLevel.Trace, Message = "Transport {EndpointName}: Message bytes (UTF-8): {ByteRepresentation}")]
    private static partial void TransportMessageBytes(this ILogger logger, string endpointName, string byteRepresentation);

    /// <summary>
    /// Logs the byte representation of a message for diagnostic purposes.
    /// This is useful for diagnosing encoding issues with non-ASCII characters.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="endpointName">The name of the endpoint.</param>
    /// <param name="message">The message to log bytes for.</param>
    internal static void TransportMessageBytesUtf8(this ILogger logger, string endpointName, string message)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(message);
            var byteRepresentation =
#if NET
                Convert.ToHexString(bytes);
#else
                BitConverter.ToString(bytes).Replace("-", " ");
#endif
            logger.TransportMessageBytes(endpointName, byteRepresentation);
        }
    }
}