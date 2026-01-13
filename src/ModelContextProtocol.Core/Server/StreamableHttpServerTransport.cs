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
public sealed class StreamableHttpServerTransport : ITransport
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
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _unsolicitedMessageLock = new(1, 1);

    private SseEventWriter? _sseResponseWriter;
    private ISseEventStreamWriter? _sseEventStreamWriter;
    private TaskCompletionSource<bool>? _responseTcs;
    private bool _getRequestStarted;
    private bool _disposed;

    /// <inheritdoc/>
    public string? SessionId { get; set; }

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
    /// Gets or sets a callback to be invoked before handling the initialize request.
    /// </summary>
    public Func<InitializeRequestParams?, ValueTask>? OnInitRequestReceived { get; set; }

    /// <summary>
    /// Gets or sets the event store for resumability support.
    /// When set, events are stored and can be replayed when clients reconnect with a Last-Event-ID header.
    /// </summary>
    public ISseEventStreamStore? EventStreamStore { get; set; }

    /// <summary>
    /// Gets or sets the retry interval to suggest to clients in SSE retry field.
    /// When <see cref="EventStreamStore"/> is set, the server will include a retry field in priming events.
    /// </summary>
    /// <remarks>
    /// The default value is 1 second.
    /// </remarks>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the negotiated protocol version for this session.
    /// </summary>
    internal string? NegotiatedProtocolVersion { get; private set; }

    /// <inheritdoc/>
    public ChannelReader<JsonRpcMessage> MessageReader => _incomingChannel.Reader;

    internal ChannelWriter<JsonRpcMessage> MessageWriter => _incomingChannel.Writer;

    /// <summary>
    /// Handles the initialize request by capturing the protocol version and invoking the user callback.
    /// </summary>
    internal async ValueTask HandleInitRequestAsync(InitializeRequestParams? initParams)
    {
        // Capture the negotiated protocol version for resumability checks
        NegotiatedProtocolVersion = initParams?.ProtocolVersion;

        // Invoke user-provided callback if specified
        if (OnInitRequestReceived is { } callback)
        {
            await callback(initParams).ConfigureAwait(false);
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

        using (await _unsolicitedMessageLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_getRequestStarted)
            {
                throw new InvalidOperationException("Session resumption is not yet supported. Please start a new session.");
            }

            _getRequestStarted = true;
            _sseResponseWriter = new SseEventWriter(sseResponseStream);
            _responseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _sseEventStreamWriter = await TryCreateEventStreamAsync(streamId: UnsolicitedMessageStreamId, cancellationToken).ConfigureAwait(false);
            if (_sseEventStreamWriter is not null)
            {
                var primingItem = await _sseEventStreamWriter.WriteEventAsync(SseItem.Prime<JsonRpcMessage>(RetryInterval), cancellationToken).ConfigureAwait(false);
                await _sseResponseWriter.WriteAsync(primingItem, cancellationToken).ConfigureAwait(false);
            }
        }

        // Wait for the response to be written before returning from the handler.
        // This keeps the HTTP response open until the final response message is sent.
        await _responseTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
    /// <see false="false"/> if nothing was written because the request body did not contain any <see cref="JsonRpcRequest"/> messages to respond to.
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

        using var postCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
        var postTransport = new StreamableHttpPostTransport(this, responseStream);
        await using (postTransport.ConfigureAwait(false))
        {
            return await postTransport.HandlePostAsync(message, postCts.Token).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (Stateless)
        {
            throw new InvalidOperationException("Unsolicited server to client messages are not supported in stateless mode.");
        }

        using var _ = await _unsolicitedMessageLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (!_getRequestStarted)
        {
            // Clients are not required to make a GET request for unsolicited messages.
            // If no GET request has been made, drop the message.
            return;
        }

        Debug.Assert(_sseResponseWriter is not null);
        Debug.Assert(_responseTcs is not null);

        var item = SseItem.Message(message);

        if (_sseEventStreamWriter is not null)
        {
            item = await _sseEventStreamWriter.WriteEventAsync(item, cancellationToken).ConfigureAwait(false);
        }

        if (!_disposed)
        {
            // Only write the message to the response if we're not disposed, since disposal is a sign
            // that the response has completed.

            try
            {
                await _sseResponseWriter!.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _responseTcs!.TrySetException(ex);
            }
        }
    }

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
            await _disposeCts.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _responseTcs?.TrySetResult(true);
                _sseResponseWriter?.Dispose();

                if (_sseEventStreamWriter is not null)
                {
                    await _sseEventStreamWriter.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _disposeCts.Dispose();
            }
        }
    }

    internal async ValueTask<ISseEventStreamWriter?> TryCreateEventStreamAsync(string streamId, CancellationToken cancellationToken)
    {
        if (EventStreamStore is null || !McpSessionHandler.SupportsPrimingEvent(NegotiatedProtocolVersion))
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
}
