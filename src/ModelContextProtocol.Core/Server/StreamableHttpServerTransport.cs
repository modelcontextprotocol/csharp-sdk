using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.ServerSentEvents;
using System.Security.Claims;
using System.Threading.Channels;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides an <see cref="ITransport"/> implementation using Server-Sent Events (SSE) for server-to-client communication.
/// </summary>
/// <remarks>
/// <para>
/// This transport provides one-way communication from server to client using the SSE protocol over HTTP,
/// while receiving client messages through a separate mechanism. It writes messages as
/// SSE events to a response stream, typically associated with an HTTP response.
/// </para>
/// <para>
/// This transport is used in scenarios where the server needs to push messages to the client in real-time,
/// such as when streaming completion results or providing progress updates during long-running operations.
/// </para>
/// </remarks>
public sealed partial class StreamableHttpServerTransport : ITransport
{
    /// <summary>
    /// The stream ID used for unsolicited messages sent via the standalone GET SSE stream.
    /// </summary>
    public static readonly string UnsolicitedMessageStreamId = "__get__";

    private readonly Channel<JsonRpcMessage> _incomingChannel = Channel.CreateBounded<JsonRpcMessage>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly CancellationTokenSource _transportDisposedCts = new();
    private readonly SemaphoreSlim _unsolicitedMessageLock = new(1, 1);
    private readonly ILogger _logger;

    private SseEventWriter? _httpSseWriter;
#pragma warning disable MCP9006 // Stateful Streamable HTTP resumability types are obsolete but still wired up internally.
    private ISseEventStreamWriter? _storeSseWriter;
#pragma warning restore MCP9006
    private TaskCompletionSource<bool>? _httpResponseTcs;
    private string? _negotiatedProtocolVersion;
    private bool _getHttpRequestStarted;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamableHttpServerTransport"/> class.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    public StreamableHttpServerTransport(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger<StreamableHttpServerTransport>() ?? NullLogger<StreamableHttpServerTransport>.Instance;
    }

    /// <inheritdoc/>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets or initializes a value that indicates whether the transport should be in stateless mode that does not require all requests for a given session
    /// to arrive to the same ASP.NET Core application process. Unsolicited server-to-client messages are not supported in this mode,
    /// so calling <see cref="HandleGetRequestAsync(Stream, CancellationToken)"/> results in an <see cref="InvalidOperationException"/>.
    /// Server-to-client requests are also unsupported, because the responses might arrive at another ASP.NET Core application process.
    /// Client sampling and roots capabilities are also disabled in stateless mode, because the server cannot make requests.
    /// </summary>
    public bool Stateless { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the execution context should flow from the calls to <see cref="HandlePostRequestAsync(JsonRpcMessage, Stream, CancellationToken)"/>
    /// to the corresponding <see cref="JsonRpcMessageContext.ExecutionContext"/> property contained in the <see cref="JsonRpcMessage"/> instances returned by the <see cref="MessageReader"/>.
    /// </summary>
    /// <value>
    /// The default is <see langword="false"/>.
    /// </value>
    public bool FlowExecutionContextFromRequests { get; init; }

    /// <summary>
    /// Gets or sets the event store for resumability support.
    /// When set, events are stored and can be replayed when clients reconnect with a Last-Event-ID header.
    /// </summary>
    [Obsolete(Obsoletions.LegacyStatefulHttp_Message, DiagnosticId = Obsoletions.LegacyStatefulHttp_DiagnosticId, UrlFormat = Obsoletions.LegacyStatefulHttp_Url)]
    public ISseEventStreamStore? EventStreamStore { get; init; }

    /// <summary>
    /// Gets or sets an optional callback invoked after the initialization handshake completes.
    /// </summary>
    /// <remarks>
    /// When set, this callback is invoked with the <see cref="InitializeRequestParams"/> after a successful
    /// initialization handshake. This can be used to persist session data for cross-instance migration.
    /// </remarks>
    public Func<InitializeRequestParams, CancellationToken, ValueTask>? OnSessionInitialized { get; init; }

    /// <inheritdoc/>
    public ChannelReader<JsonRpcMessage> MessageReader => _incomingChannel.Reader;

    internal ChannelWriter<JsonRpcMessage> MessageWriter => _incomingChannel.Writer;

    /// <summary>
    /// Handles initialization by capturing the negotiated protocol version and optionally invoking
    /// <see cref="OnSessionInitialized"/> so session data can be persisted.
    /// </summary>
    /// <remarks>
    /// This is called automatically when an <c>initialize</c> request is processed via
    /// <see cref="HandlePostRequestAsync(JsonRpcMessage, Stream, CancellationToken)"/>. It can also be called
    /// directly when restoring a migrated session with known <see cref="InitializeRequestParams"/>.
    /// </remarks>
    /// <param name="initParams">The initialization parameters from the client, or <see langword="null"/> if unavailable.</param>
    public async ValueTask HandleInitializeRequestAsync(InitializeRequestParams? initParams)
    {
        _negotiatedProtocolVersion = initParams?.ProtocolVersion;

        if (initParams is not null && OnSessionInitialized is { } callback)
        {
            await callback(initParams, _transportDisposedCts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles an optional SSE GET request a client using the Streamable HTTP transport might make by
    /// writing any unsolicited JSON-RPC messages sent via <see cref="SendMessageAsync"/>
    /// to the SSE response stream until cancellation is requested or the transport is disposed.
    /// </summary>
    /// <param name="sseResponseStream">The response stream to write MCP JSON-RPC messages as SSE events to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the send loop that writes JSON-RPC messages to the SSE response stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sseResponseStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Stateless"/> is <see langword="true"/> and GET requests are not supported in stateless mode.
    /// </exception>
    public async Task HandleGetRequestAsync(Stream sseResponseStream, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(sseResponseStream);

        if (Stateless)
        {
            throw new InvalidOperationException("GET requests are not supported in stateless mode.");
        }

        try
        {
            using (await _unsolicitedMessageLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_getHttpRequestStarted)
                {
                    throw new InvalidOperationException("Session resumption is not yet supported. Please start a new session.");
                }

                _getHttpRequestStarted = true;
                _httpSseWriter = new SseEventWriter(sseResponseStream);
                _httpResponseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _storeSseWriter = await TryCreateEventStreamAsync(streamId: UnsolicitedMessageStreamId, cancellationToken).ConfigureAwait(false);
                if (_storeSseWriter is not null)
                {
                    var primingItem = await _storeSseWriter.WriteEventAsync(SseItem.Prime<JsonRpcMessage>(), cancellationToken).ConfigureAwait(false);
                    await _httpSseWriter.WriteAsync(primingItem, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // If there's no priming write, flush the stream to ensure HTTP response headers are
                    // sent to the client now that the transport is ready to accept messages via SendMessageAsync.
                    await sseResponseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            // Wait for the response to be written before returning from the handler.
            // This keeps the HTTP response open until the final response message is sent.
            await _httpResponseTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Release the SseEventWriter's reference to the response stream promptly when the GET
            // request ends, regardless of how it exits. Otherwise the response stream (and the
            // underlying Kestrel connection and associated memory pool buffers) remains pinned
            // in memory until the session itself is disposed (via explicit DELETE or idle timeout).
            // Clients that disconnect without sending DELETE — common with long-lived SSE — would
            // otherwise accumulate significant unmanaged memory per session during that interval.
            using (await _unsolicitedMessageLock.LockAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (_httpSseWriter is { } writer)
                {
                    _httpSseWriter = null;
                    writer.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Handles a Streamable HTTP POST request processing both the request body and response body ensuring that
    /// <see cref="JsonRpcResponse"/> and other correlated messages are sent back to the client directly in response
    /// to the <see cref="JsonRpcRequest"/> that initiated the message.
    /// </summary>
    /// <param name="message">The JSON-RPC message received from the client via the POST request body.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <param name="responseStream">The POST response body to write MCP JSON-RPC messages to.</param>
    /// <returns>
    /// <see langword="true"/> if data was written to the response body.
    /// <see langword="false"/> if nothing was written because the request body did not contain any <see cref="JsonRpcRequest"/> messages to respond to.
    /// The HTTP application should typically respond with an empty "202 Accepted" response in this scenario.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="responseStream"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// If an authenticated <see cref="ClaimsPrincipal"/> sent the message, that can be included in the <see cref="JsonRpcMessage.Context"/>.
    /// No other part of the context should be set.
    /// </remarks>
    public async Task<bool> HandlePostRequestAsync(JsonRpcMessage message, Stream responseStream, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);
        Throw.IfNull(responseStream);

        var postTransport = new StreamableHttpPostTransport(this, responseStream, _transportDisposedCts.Token, _logger);
        using var postCts = CancellationTokenSource.CreateLinkedTokenSource(_transportDisposedCts.Token, cancellationToken);
        await using (postTransport.ConfigureAwait(false))
        {
            return await postTransport.HandlePostAsync(
                message,
                cancellationToken: postCts.Token)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// This method sends server-to-client messages via the standalone SSE stream opened by an
    /// optional HTTP GET request (see <see cref="HandleGetRequestAsync(Stream, CancellationToken)"/>).
    /// </para>
    /// <para>
    /// <strong>This is generally the wrong channel for server-to-client requests.</strong> Requests
    /// sent via the GET stream depend on the client keeping a long-lived GET open, have no per-request
    /// correlation to a caller, and race with GET startup and teardown. When called from inside a
    /// tool, prompt, or resource handler, use the <see cref="McpServer"/> instance available via
    /// <c>RequestContext</c> instead — it routes through the originating POST response stream via
    /// <see cref="JsonRpcMessageContext.RelatedTransport"/>, which is always open for the duration of
    /// the request. A <see cref="LogLevel.Warning"/> diagnostic is emitted whenever a
    /// <see cref="JsonRpcRequest"/> is sent through this method.
    /// </para>
    /// <para>
    /// If no GET SSE stream has yet been opened on this session, behavior depends on the message kind:
    /// <see cref="JsonRpcRequest"/> messages throw <see cref="InvalidOperationException"/> because the
    /// awaiting caller has no way to receive a response; <see cref="JsonRpcNotification"/> messages are
    /// dropped (notifications are best-effort and the spec does not require clients to issue a GET)
    /// and a <see cref="LogLevel.Debug"/> diagnostic is logged; other messages are dropped and a
    /// <see cref="LogLevel.Warning"/> diagnostic is logged.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Stateless"/> is <see langword="true"/>, or <paramref name="message"/> is a
    /// <see cref="JsonRpcRequest"/> and no GET SSE stream has been opened on this session.
    /// </exception>
    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (Stateless)
        {
            throw new InvalidOperationException("Unsolicited server to client messages are not supported in stateless mode.");
        }

        using var _ = await _unsolicitedMessageLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (!_getHttpRequestStarted)
        {
            switch (message)
            {
                case JsonRpcRequest request:
                    throw new InvalidOperationException(
                        $"Cannot send server-to-client JSON-RPC request '{request.Method}' because no GET SSE stream has been opened on this session " +
                        $"(SessionId: '{SessionId}'). " +
                        "Inside a tool, prompt, or resource handler, use the IMcpServer instance from RequestContext (or any IMcpServer obtained via DI from a request-scoped service provider) so the request is routed through the originating POST response stream via JsonRpcMessageContext.RelatedTransport. " +
                        "The standalone GET SSE stream is optional for clients and is not a reliable channel for server-to-client requests.");

                case JsonRpcNotification notification:
                    // Clients are not required to make a GET request for unsolicited messages.
                    // If no GET request has been made, drop the notification (best-effort).
                    LogNotificationDroppedNoGetStream(notification.Method, SessionId ?? string.Empty);
                    return;

                default:
                    // JsonRpcResponse / JsonRpcError generally flow through the originating POST response
                    // stream, so receiving one here without a GET is unexpected. Log loudly and drop.
                    LogMessageDroppedNoGetStream(message.GetType().Name, GetMessageId(message), SessionId ?? string.Empty);
                    return;
            }
        }

        if (message is JsonRpcRequest openRequest)
        {
            LogServerRequestOverGetStream(openRequest.Method, SessionId ?? string.Empty);
        }

        Debug.Assert(_httpResponseTcs is not null);

        var item = SseItem.Message(message);

        if (_storeSseWriter is not null)
        {
            // Always record the message in the event store (if configured) — even when the GET
            // response stream is gone — so a reconnecting client can replay it via Last-Event-ID.
            item = await _storeSseWriter.WriteEventAsync(item, cancellationToken).ConfigureAwait(false);
        }

        if (_httpSseWriter is { } writer)
        {
            try
            {
                await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _httpResponseTcs!.TrySetException(ex);
            }
        }
    }

    private static string GetMessageId(JsonRpcMessage message) =>
        message is JsonRpcMessageWithId withId ? withId.Id.ToString() : string.Empty;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        using var _ = await _unsolicitedMessageLock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _incomingChannel.Writer.TryComplete();
            await _transportDisposedCts.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _httpResponseTcs?.TrySetResult(true);
                if (_httpSseWriter is { } writer)
                {
                    _httpSseWriter = null;
                    writer.Dispose();
                }

                if (_storeSseWriter is not null)
                {
                    await _storeSseWriter.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _transportDisposedCts.Dispose();
            }
        }
    }

#pragma warning disable MCP9006 // Stateful Streamable HTTP resumability types are obsolete but still wired up internally.
    internal async ValueTask<ISseEventStreamWriter?> TryCreateEventStreamAsync(string streamId, CancellationToken cancellationToken)
    {
        if (EventStreamStore is null || !McpSessionHandler.SupportsPrimingEvent(_negotiatedProtocolVersion))
        {
            return null;
        }

        // We use the 'Streaming' stream mode so that in the case of an unexpected network disconnection,
        // the client can continue reading the remaining messages in a single, streamed response.
        const SseEventStreamMode Mode = SseEventStreamMode.Streaming;

        var sseEventStreamWriter = await EventStreamStore.CreateStreamAsync(new SseEventStreamOptions
        {
            SessionId = SessionId ?? Guid.NewGuid().ToString("N"),
            StreamId = streamId,
            Mode = Mode,
        }, cancellationToken).ConfigureAwait(false);

        return sseEventStreamWriter;
    }
#pragma warning restore MCP9006

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sending server-to-client JSON-RPC request '{Method}' over the standalone GET SSE stream (SessionId: '{SessionId}'). " +
            "Consider using the IMcpServer instance from RequestContext inside a tool, prompt, or resource handler so the request is routed through the originating POST response stream via JsonRpcMessageContext.RelatedTransport, which is more reliable than the optional GET SSE stream.")]
    private partial void LogServerRequestOverGetStream(string method, string sessionId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Dropping server-to-client JSON-RPC notification '{Method}' because no GET SSE stream has been opened on this session (SessionId: '{SessionId}').")]
    private partial void LogNotificationDroppedNoGetStream(string method, string sessionId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Dropping unexpected server-to-client {MessageType} (Id: '{MessageId}') because no GET SSE stream has been opened on this session (SessionId: '{SessionId}'). " +
            "Responses normally flow through the originating POST response stream via JsonRpcMessageContext.RelatedTransport.")]
    private partial void LogMessageDroppedNoGetStream(string messageType, string messageId, string sessionId);
}
