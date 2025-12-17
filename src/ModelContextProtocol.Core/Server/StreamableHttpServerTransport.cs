using ModelContextProtocol.Protocol;
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

    // For JsonRpcMessages without a RelatedTransport, we don't want to block just because the client didn't make a GET request to handle unsolicited messages.
    private static readonly BoundedChannelOptions _sseWriterChannelOptions = new(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest,
    };
    private readonly Channel<JsonRpcMessage> _incomingChannel = Channel.CreateBounded<JsonRpcMessage>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private SseWriter _sseWriter = new(channelOptions: _sseWriterChannelOptions);
    private ISseEventStreamWriter? _eventStreamWriter;
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
    public Task HandleGetRequestAsync(Stream sseResponseStream, CancellationToken cancellationToken = default)
        => HandleGetRequestAsync(sseResponseStream, eventStreamReader: null, cancellationToken);

    /// <summary>
    /// Handles an optional SSE GET request a client using the Streamable HTTP transport might make by
    /// writing any unsolicited JSON-RPC messages sent via <see cref="SendMessageAsync"/>
    /// to the SSE response stream until cancellation is requested or the transport is disposed.
    /// </summary>
    /// <param name="sseResponseStream">The response stream to write MCP JSON-RPC messages as SSE events to.</param>
    /// <param name="eventStreamReader">The <see cref="ISseEventStreamReader"/> to replay events from before writing this transport's messages to the response stream.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the send loop that writes JSON-RPC messages to the SSE response stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sseResponseStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Stateless"/> is <see langword="true"/> and GET requests are not supported in stateless mode.
    /// </exception>
    public async Task HandleGetRequestAsync(Stream sseResponseStream, ISseEventStreamReader? eventStreamReader, CancellationToken cancellationToken = default)
    {
        var writeTask = await StartGetRequestAsync(sseResponseStream, eventStreamReader, cancellationToken).ConfigureAwait(false);
        await writeTask.ConfigureAwait(false);
    }

    private async Task<Task> StartGetRequestAsync(Stream sseResponseStream, ISseEventStreamReader? eventStreamReader, CancellationToken cancellationToken)
    {
        Throw.IfNull(sseResponseStream);

        if (Stateless)
        {
            throw new InvalidOperationException("GET requests are not supported in stateless mode.");
        }

        using var _ = await _sendLock.LockAsync(cancellationToken);

        ThrowIfDisposed();

        if (_getRequestStarted)
        {
            await _sseWriter.DisposeAsync().ConfigureAwait(false);
            _sseWriter = new();
        }

        _getRequestStarted = true;

        if (eventStreamReader is not null)
        {
            if (eventStreamReader.SessionId != SessionId)
            {
                throw new InvalidOperationException("The provided SSE event stream reader relates to a different session.");
            }

            if (eventStreamReader.StreamId != UnsolicitedMessageStreamId)
            {
                throw new InvalidOperationException("The event stream reader does not relate to the unsolicited message stream.");
            }

            await eventStreamReader.CopyToAsync(sseResponseStream, cancellationToken);
        }

        var eventStreamWriter = await GetOrCreateEventStreamAsync(cancellationToken).ConfigureAwait(false);
        if (eventStreamWriter is not null)
        {
            await _sseWriter.SendPrimingEventAsync(RetryInterval, eventStreamWriter, cancellationToken).ConfigureAwait(false);
        }

        // We do not need to reference _disposeCts like in HandlePostRequest, because the session ending completes the _sseWriter gracefully.
        return _sseWriter.WriteAllAsync(sseResponseStream, cancellationToken);
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
        await using var postTransport = new StreamableHttpPostTransport(this, responseStream);
        return await postTransport.HandlePostAsync(message, postCts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (Stateless)
        {
            throw new InvalidOperationException("Unsolicited server to client messages are not supported in stateless mode.");
        }

        using var _ = await _sendLock.LockAsync(cancellationToken);

        ThrowIfDisposed();

        // If the underlying writer has been disposed, rely on the event stream writer, if present.
        // Otherwise, just drop the message.
        var eventStreamWriter = await GetOrCreateEventStreamAsync(cancellationToken).ConfigureAwait(false);
        await _sseWriter.SendMessageAsync(message, eventStreamWriter, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        using var _ = await _sendLock.LockAsync();

        if (_disposed)
        {
            return;
        }

        try
        {
            _incomingChannel.Writer.TryComplete();
            await _disposeCts.CancelAsync();
        }
        finally
        {
            try
            {
                await _sseWriter.DisposeAsync().ConfigureAwait(false);

                if (_eventStreamWriter is not null)
                {
                    await _eventStreamWriter.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _disposeCts.Dispose();
                _disposed = true;
            }
        }
    }

    private async ValueTask<ISseEventStreamWriter?> GetOrCreateEventStreamAsync(CancellationToken cancellationToken)
    {
        if (_eventStreamWriter is not null)
        {
            return _eventStreamWriter;
        }

        if (EventStreamStore is null || !McpSessionHandler.SupportsPrimingEvent(NegotiatedProtocolVersion))
        {
            return null;
        }

        // We set the mode to 'Polling' so that the transport can take over writing to the response stream after
        // messages have been replayed.
        const SseEventStreamMode Mode = SseEventStreamMode.Polling;

        _eventStreamWriter = await EventStreamStore.CreateStreamAsync(options: new()
        {
            SessionId = SessionId ?? Guid.NewGuid().ToString("N"),
            StreamId = UnsolicitedMessageStreamId,
            Mode = Mode,
        }, cancellationToken).ConfigureAwait(false);

        return _eventStreamWriter;
    }

    private void ThrowIfDisposed()
    {
#if NET
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
#endif
    }
}
