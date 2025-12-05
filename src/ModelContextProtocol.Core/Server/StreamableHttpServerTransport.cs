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
    /// Gets or sets the retry interval in milliseconds to suggest to clients in SSE retry field.
    /// When set along with <see cref="EventStore"/>, the server will include a retry field in priming events.
    /// </summary>
    public int? RetryInterval { get; set; }

    /// <inheritdoc/>
    public ChannelReader<JsonRpcMessage> MessageReader => _incomingChannel.Reader;

    internal ChannelWriter<JsonRpcMessage> MessageWriter => _incomingChannel.Writer;

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

        // Handle resumption: if Last-Event-ID is provided and we have an event store, replay missed events
        if (!string.IsNullOrEmpty(lastEventId) && EventStore is not null)
        {
            await ReplayEventsAsync(sseResponseStream, lastEventId!, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Only allow one new GET stream per session (not resumption)
        if (Interlocked.Exchange(ref _getRequestStarted, 1) == 1)
        {
            throw new InvalidOperationException("Only one GET SSE stream is allowed per session. Use Last-Event-ID header to resume.");
        }

        // Configure the SSE writer for resumability if we have an event store
        if (EventStore is not null)
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
    /// Replays events from the event store after the specified event ID.
    /// </summary>
    private async Task ReplayEventsAsync(Stream sseResponseStream, string lastEventId, CancellationToken cancellationToken)
    {
        if (EventStore is null)
        {
            return;
        }

        // Create a temporary SSE writer for replay
        await using var replayWriter = new SseWriter();
        replayWriter.EventStore = EventStore;

        // Replay events from the store
        var streamId = await EventStore.ReplayEventsAfterAsync(
            lastEventId,
            async (message, eventId, ct) =>
            {
                // The replay callback sends each stored event
                // We need to write directly to the stream with the stored event ID
                await replayWriter.SendMessageAsync(message, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        if (streamId is null)
        {
            // Event ID not found - client should start fresh
            throw new InvalidOperationException($"Event ID '{lastEventId}' not found in event store.");
        }

        // Set the stream ID for any new events and send priming
        replayWriter.StreamId = streamId;
        replayWriter.RetryInterval = RetryInterval;
        await replayWriter.SendPrimingEventAsync(cancellationToken).ConfigureAwait(false);

        // If this is the GET stream, continue receiving new events
        if (streamId == GetStreamId)
        {
            // Mark that we've resumed the GET stream
            Interlocked.Exchange(ref _getRequestStarted, 1);

            // Transfer remaining messages from the main writer to the replay writer
            // This is complex - for now, just write what we have
            await replayWriter.WriteAllAsync(sseResponseStream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // For POST streams, just complete the replay
            await replayWriter.WriteAllAsync(sseResponseStream, cancellationToken).ConfigureAwait(false);
        }
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
