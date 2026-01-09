using ModelContextProtocol.Protocol;
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
/// <param name="sseResponseStream">The response stream to write MCP JSON-RPC messages as SSE events to.</param>
/// <param name="messageEndpoint">
/// The relative or absolute URI the client should use to post MCP JSON-RPC messages for this session.
/// These messages should be passed to <see cref="OnMessageReceivedAsync(JsonRpcMessage, CancellationToken)"/>.
/// Defaults to "/message".
/// </param>
/// <param name="sessionId">The identifier corresponding to the current MCP session.</param>
public sealed class SseResponseStreamTransport(Stream sseResponseStream, string? messageEndpoint = "/message", string? sessionId = null) : ITransport
{
    private readonly Channel<JsonRpcMessage> _incomingChannel = Channel.CreateBounded<JsonRpcMessage>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TaskCompletionSource<bool> _completedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SseEventWriter _sseWriter = new(sseResponseStream);

    private bool _isConnected;
    private bool _disposed;

    /// <summary>
    /// Starts the transport and writes the JSON-RPC messages sent via <see cref="SendMessageAsync"/>
    /// to the SSE response stream until cancellation is requested or the transport is disposed.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the send loop that writes JSON-RPC messages to the SSE response stream.</returns>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = true;

        // Write the endpoint event first
        if (messageEndpoint is not null)
        {
            await _sseWriter.WriteAsync(SseItem.Endpoint(messageEndpoint), cancellationToken).ConfigureAwait(false);
        }

        await _completedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ChannelReader<JsonRpcMessage> MessageReader => _incomingChannel.Reader;

    /// <inheritdoc/>
    public string? SessionId { get; } = sessionId;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        using var _ = await _lock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isConnected = false;
        _incomingChannel.Writer.TryComplete();
        _completedTcs.TrySetResult(true);
        _sseWriter.Dispose();
    }

    /// <inheritdoc/>
    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        using var _ = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        // If disposed, just drop the message.
        if (_disposed)
        {
            return;
        }

        await _sseWriter.WriteAsync(SseItem.Message(message), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles incoming JSON-RPC messages received on the /message endpoint.
    /// </summary>
    /// <param name="message">The JSON-RPC message received from the client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation to buffer the JSON-RPC message for processing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">There is an attempt to process a message before calling <see cref="RunAsync(CancellationToken)"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method is the entry point for processing client-to-server communication in the SSE transport model.
    /// While the SSE protocol itself is unidirectional (server to client), this method allows bidirectional
    /// communication by handling HTTP POST requests sent to the message endpoint.
    /// </para>
    /// <para>
    /// When a client sends a JSON-RPC message to the /message endpoint, the server calls this method to
    /// process the message and make it available to the MCP server via the <see cref="MessageReader"/> channel.
    /// </para>
    /// <para>
    /// If an authenticated <see cref="ClaimsPrincipal"/> sent the message, that can be included in the <see cref="JsonRpcMessage.Context"/>.
    /// No other part of the context should be set.
    /// </para>
    /// </remarks>
    public async Task OnMessageReceivedAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (!_isConnected)
        {
            throw new InvalidOperationException($"Transport is not connected. Make sure to call {nameof(RunAsync)} first.");
        }

        await _incomingChannel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
