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

    [LoggerMessage(Level = LogLevel.Error, Message = "Request response type conversion error for {endpointName} with method {method}: expected {expectedType}")]
    internal static partial void RequestResponseTypeConversionError(this ILogger logger, string endpointName, string method, Type expectedType);

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport for {endpointName} error: {data}")]
    internal static partial void TransportError(this ILogger logger, string endpointName, string data);

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

    [LoggerMessage(
        EventId = 7012,
        Level = LogLevel.Warning,
        Message = "No handler found for request {method} for server {endpointName}"
    )]
    public static partial void NoHandlerFoundForRequest(
        this ILogger logger,
        string endpointName,
        string method);

    [LoggerMessage(
        EventId = 7013,
        Level = LogLevel.Trace,
        Message = "Response matched pending request for {endpointName} with ID {messageId}"
    )]
    public static partial void ResponseMatchedPendingRequest(
        this ILogger logger,
        string endpointName,
        string messageId);

    [LoggerMessage(
        EventId = 7014,
        Level = LogLevel.Warning,
        Message = "Endpoint handler received unexpected message type for {endpointName}: {messageType}"
    )]
    public static partial void EndpointHandlerUnexpectedMessageType(
        this ILogger logger,
        string endpointName,
        string messageType);

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

    [LoggerMessage(
        EventId = 7016,
        Level = LogLevel.Error,
        Message = "{endpointName} already initializing"
    )]
    public static partial void ServerAlreadyInitializing(
        this ILogger logger,
        string endpointName);

    [LoggerMessage(
        EventId = 7017,
        Level = LogLevel.Error,
        Message = "{endpointName} initialization error"
    )]
    public static partial void ServerInitializationError(
        this ILogger logger,
        string endpointName,
        Exception e);

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