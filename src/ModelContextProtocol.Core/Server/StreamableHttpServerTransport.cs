using ModelContextProtocol.Protocol;
using System.IO.Pipelines;
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
    /// Configures whether the transport should be in stateless mode that does not require all requests for a given session
    /// to arrive to the same ASP.NET Core application process. Unsolicited server-to-client messages are not supported in this mode,
    /// so calling <see cref="HandleGetRequest(Stream, CancellationToken)"/> results in an <see cref="InvalidOperationException"/>.
    /// Server-to-client requests are also unsupported, because the responses may arrive at another ASP.NET Core application process.
    /// Client sampling and roots capabilities are also disabled in stateless mode, because the server cannot make requests.
    /// </summary>
    public bool Stateless { get; init; }

    /// <summary>
    /// Gets a value indicating whether the execution context should flow from the calls to <see cref="HandlePostRequest(IDuplexPipe, CancellationToken)"/>
    /// to the corresponding <see cref="JsonRpcMessage.ExecutionContext"/> emitted by the <see cref="MessageReader"/>.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/>.
    /// </remarks>
    public bool FlowExecutionContextFromRequests { get; init; }

    /// <summary>
    /// Gets or sets a callback to be invoked before handling the initialize request.
    /// </summary>
    public Func<InitializeRequestParams?, ValueTask>? OnInitRequestReceived { get; set; }

    /// <inheritdoc/>
    public ChannelReader<JsonRpcMessage> MessageReader => _incomingChannel.Reader;

    internal ChannelWriter<JsonRpcMessage> MessageWriter => _incomingChannel.Writer;

    /// <summary>
    /// Handles an optional SSE GET request a client using the Streamable HTTP transport might make by
    /// writing any unsolicited JSON-RPC messages sent via <see cref="SendMessageAsync"/>
    /// to the SSE response stream until cancellation is requested or the transport is disposed.
    /// </summary>
    /// <param name="sseResponseStream">The response stream to write MCP JSON-RPC messages as SSE events to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the send loop that writes JSON-RPC messages to the SSE response stream.</returns>
    public async Task HandleGetRequest(Stream sseResponseStream, CancellationToken cancellationToken)
    {
        if (Stateless)
        {
            throw new InvalidOperationException("GET requests are not supported in stateless mode.");
        }

        if (Interlocked.Exchange(ref _getRequestStarted, 1) == 1)
        {
            throw new InvalidOperationException("Session resumption is not yet supported. Please start a new session.");
        }

        // We do not need to reference _disposeCts like in HandlePostRequest, because the session ending completes the _sseWriter gracefully.
        await _sseWriter.WriteAllAsync(sseResponseStream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a Streamable HTTP POST request processing both the request body and response body ensuring that
    /// <see cref="JsonRpcResponse"/> and other correlated messages are sent back to the client directly in response
    /// to the <see cref="JsonRpcRequest"/> that initiated the message.
    /// </summary>
    /// <param name="httpBodies">The duplex pipe facilitates the reading and writing of HTTP request and response data.</param>
    /// <param name="cancellationToken">This token allows for the operation to be canceled if needed.</param>
    /// <returns>
    /// True, if data was written to the response body.
    /// False, if nothing was written because the request body did not contain any <see cref="JsonRpcRequest"/> messages to respond to.
    /// The HTTP application should typically respond with an empty "202 Accepted" response in this scenario.
    /// </returns>
    public async Task<bool> HandlePostRequest(IDuplexPipe httpBodies, CancellationToken cancellationToken)
    {
        using var postCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
        await using var postTransport = new StreamableHttpPostTransport(this, httpBodies);
        return await postTransport.RunAsync(postCts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (Stateless)
        {
            throw new InvalidOperationException("Unsolicited server to client messages are not supported in stateless mode.");
        }

        await _sseWriter.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
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
