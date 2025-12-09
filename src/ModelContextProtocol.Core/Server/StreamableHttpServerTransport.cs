using ModelContextProtocol.Protocol;
using System.IO.Pipelines;
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
    internal const string GetStreamId = "__get__";

    // For JsonRpcMessages without a RelatedTransport, we don't want to block just because the client didn't make a GET request to handle unsolicited messages.
    private readonly SseWriter _sseWriter = new(channelOptions: new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest,
    });
    private readonly Channel<JsonRpcMessage> _incomingChannel = Channel.CreateBounded<JsonRpcMessage>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly CancellationTokenSource _disposeCts = new();

    private int _getRequestStarted;

    /// <inheritdoc/>
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or initializes a value that indicates whether the transport should be in stateless mode that does not require all requests for a given session
    /// to arrive to the same ASP.NET Core application process. Unsolicited server-to-client messages are not supported in this mode,
    /// so calling <see cref="HandleGetRequestAsync(Stream, string?, CancellationToken)"/> results in an <see cref="InvalidOperationException"/>.
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
    public IEventStore? EventStore { get; set; }

    /// <summary>
    /// Gets or sets the retry interval to suggest to clients in SSE retry field.
    /// When set along with <see cref="EventStore"/>, the server will include a retry field in priming events.
    /// </summary>
    public TimeSpan? RetryInterval { get; set; }

    /// <summary>
    /// Gets or sets the negotiated protocol version for this session.
    /// </summary>
    public string? NegotiatedProtocolVersion { get; set; }

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
    /// <param name="lastEventId">
    /// The Last-Event-ID header value from the client request for resumability.
    /// When provided, the server will replay events that occurred after this ID before streaming new events.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the send loop that writes JSON-RPC messages to the SSE response stream.</returns>
    public async Task HandleGetRequestAsync(Stream sseResponseStream, string? lastEventId = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(sseResponseStream);

        if (Stateless)
        {
            throw new InvalidOperationException("GET requests are not supported in stateless mode.");
        }

        // Handle resumption: if Last-Event-ID is provided and we have an event store
        if (!string.IsNullOrEmpty(lastEventId) && EventStore is not null)
        {
            // Replay events and determine which stream they belonged to
            var streamId = await ReplayEventsToStreamAsync(sseResponseStream, lastEventId!, cancellationToken).ConfigureAwait(false);

            if (streamId is null)
            {
                // Event ID not found - client should start fresh
                throw new InvalidOperationException($"Event ID '{lastEventId}' not found in event store.");
            }

            // If this is a POST stream, we're done - the replay was the complete response
            if (streamId != GetStreamId)
            {
                return;
            }

            // For GET stream resumption, mark as started and fall through to continue receiving new events
            // (Don't need to check if already started - resumption is always allowed)
            Interlocked.Exchange(ref _getRequestStarted, 1);
        }
        else
        {
            // New GET stream (not resumption) - only allow one per session
            if (Interlocked.Exchange(ref _getRequestStarted, 1) == 1)
            {
                throw new InvalidOperationException("Only one GET SSE stream is allowed per session. Use Last-Event-ID header to resume.");
            }
        }

        // Configure the SSE writer for resumability if we have an event store and the client supports it
        if (EventStore is not null && McpSessionHandler.SupportsResumability(NegotiatedProtocolVersion))
        {
            _sseWriter.EventStore = EventStore;
            _sseWriter.StreamId = GetStreamId;
            _sseWriter.RetryInterval = RetryInterval;

            // Send a priming event to establish resumability
            await _sseWriter.SendPrimingEventAsync(cancellationToken).ConfigureAwait(false);
        }

        // We do not need to reference _disposeCts like in HandlePostRequest, because the session ending completes the _sseWriter gracefully.
        await _sseWriter.WriteAllAsync(sseResponseStream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replays events from the event store to the response stream.
    /// For POST streams, this writes the complete replay response.
    /// For GET streams, this writes replayed events before the caller continues with _sseWriter.
    /// </summary>
    /// <returns>The stream ID of the replayed events, or null if the event ID was not found.</returns>
    private async Task<string?> ReplayEventsToStreamAsync(Stream sseResponseStream, string lastEventId, CancellationToken cancellationToken)
    {
        if (EventStore is null)
        {
            return null;
        }

        // Create a writer for replaying events
        await using var replayWriter = new SseWriter();

        // Replay events from the store - this also tells us which stream the events belong to
        var streamId = await EventStore.ReplayEventsAfterAsync(
            lastEventId,
            async (message, eventId, ct) =>
            {
                await replayWriter.SendMessageAsync(message, eventId, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        if (streamId is null)
        {
            return null;
        }

        // For POST streams: configure resumability and write the replay as a complete response
        // For GET streams: just write the replayed events (caller will continue with _sseWriter for new events)
        if (streamId != GetStreamId)
        {
            // POST stream replay - include resumability info so client can resume again if needed
            replayWriter.EventStore = EventStore;
            replayWriter.StreamId = streamId;
            replayWriter.RetryInterval = RetryInterval;
            await replayWriter.SendPrimingEventAsync(cancellationToken).ConfigureAwait(false);
        }

        // Complete the replay writer and write all events to the stream
        replayWriter.Complete();
        await replayWriter.WriteAllAsync(sseResponseStream, cancellationToken).ConfigureAwait(false);

        return streamId;
    }

    /// <summary>
    /// Handles a Streamable HTTP POST request processing both the request body and response body ensuring that
    /// <see cref="JsonRpcResponse"/> and other correlated messages are sent back to the client directly in response
    /// to the <see cref="JsonRpcRequest"/> that initiated the message.
    /// </summary>
    /// <param name="message">The JSON-RPC message received from the client via the POST request body.</param>
    /// <param name="cancellationToken">This token allows for the operation to be canceled if needed. The default is <see cref="CancellationToken.None"/>.</param>
    /// <param name="responseStream">The POST response body to write MCP JSON-RPC messages to.</param>
    /// <returns>
    /// <see langword="true"/> if data was written to the response body.
    /// <see false="false"/> if nothing was written because the request body did not contain any <see cref="JsonRpcRequest"/> messages to respond to.
    /// The HTTP application should typically respond with an empty "202 Accepted" response in this scenario.
    /// </returns>
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

        // If the underlying writer has been disposed, just drop the message.
        await _sseWriter.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the standalone SSE stream (from GET requests) gracefully.
    /// </summary>
    /// <remarks>
    /// This implements part of the SSE polling pattern from SEP-1699: the server can close
    /// the standalone GET SSE stream at will. The client should reconnect with the Last-Event-ID
    /// header to resume receiving unsolicited server messages.
    /// </remarks>
    internal void CloseStandaloneSseStream()
    {
        _sseWriter.Complete();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
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
            }
            finally
            {
                _disposeCts.Dispose();
            }
        }
    }
}
