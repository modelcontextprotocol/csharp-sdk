using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Shared;

/// <summary>
/// Base class for an MCP JSON-RPC endpoint. This covers both MCP clients and servers.
/// It is not supported, nor necessary, to implement both client and server functionality in the same class.
/// If an application needs to act as both a client and a server, it should use separate objects for each.
/// This is especially true as a client represents a connection to one and only one server, and vice versa.
/// Any multi-client or multi-server functionality should be implemented at a higher level of abstraction.
/// </summary>
public abstract class McpJsonRpcEndpoint : IMcpEndpoint, IAsyncDisposable
{
    private readonly RequestHandlers _requestHandlers = [];
    private readonly NotificationHandlers _notificationHandlers = [];

    private McpSession? _session;
    private CancellationTokenSource? _sessionCts;
    private int _started;

    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// The logger for this endpoint.
    /// </summary>
    protected readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpJsonRpcEndpoint"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    protected McpJsonRpcEndpoint(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;
    }

    /// <summary>
    /// Sets the request handler for a specific method.
    /// </summary>
    /// <typeparam name="TRequest">The MCP Request type</typeparam>
    /// <typeparam name="TResponse">The MCP Response type</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="handler">The handler function.</param>
    protected void SetRequestHandler<TRequest, TResponse>(string method, Func<TRequest?, CancellationToken, Task<TResponse>> handler)
        => _requestHandlers.Set(method, handler);

    /// <summary>
    /// Sets the request handler for a specific method.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="handler">The handler function.</param>
    public void AddNotificationHandler(string method, Func<JsonRpcNotification, Task> handler)
        => _notificationHandlers.Add(method, handler);

    /// <summary>
    /// Sends a request over the protocol
    /// </summary>
    /// <typeparam name="TResult">The MCP Response type.</typeparam>
    /// <param name="request">The request instance</param>
    /// <param name="cancellationToken">The token for cancellation.</param>
    /// <returns>The MCP response.</returns>
    public async Task<TResult> SendRequestAsync<TResult>(JsonRpcRequest request, CancellationToken cancellationToken = default) where TResult : class
    {
        using var registration = cancellationToken.Register(async () =>
        {
            try
            {
                await this.NotifyCancelAsync(request.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while notifying cancellation for request {RequestId}.", request.Id);
            }
        });
        return await GetSessionOrThrow().SendRequestAsync<TResult>(request, cancellationToken);
    }

    /// <summary>
    /// Sends a notification over the protocol.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">The token for cancellation.</param>
    /// <returns>A task representing the completion of the operation.</returns>
    public Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
        => GetSessionOrThrow().SendMessageAsync(message, cancellationToken);

    /// <summary>
    /// Gets the name of the endpoint for logging and debug purposes.
    /// </summary>
    public abstract string EndpointName { get; }

    /// <summary>
    /// Task that processes incoming messages from the transport.
    /// </summary>
    protected Task? MessageProcessingTask { get; set; }

    /// <summary>
    /// Starts the session with the given transport.
    /// </summary>
    /// <param name="sessionTransport">The transport to use for the session.</param>
    /// <param name="fullSessionCancellationToken">A cancellation token for the full session.</param>
    /// <exception cref="InvalidOperationException">Thrown if the session has already started.</exception>
    [MemberNotNull(nameof(MessageProcessingTask))]
    protected void StartSession(ITransport sessionTransport, CancellationToken fullSessionCancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The MCP session has already stared.");
        }

        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(fullSessionCancellationToken);
        _session = new McpSession(sessionTransport, EndpointName, _requestHandlers, _notificationHandlers, _logger);
        MessageProcessingTask = _session.ProcessMessagesAsync(_sessionCts.Token);
    }

    /// <summary>
    /// Disposes the endpoint and releases resources.
    /// </summary>
    /// <returns>A task representing the completion of the operation.</returns>
    public async ValueTask DisposeAsync()
    {
        using var _ = await _disposeLock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }
        _disposed = true;

        await DisposeUnsynchronizedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Cleans up the endpoint and releases resources.
    /// </summary>
    /// <returns></returns>
    public virtual async ValueTask DisposeUnsynchronizedAsync()
    {
        _logger.CleaningUpEndpoint(EndpointName);

        try
        {
            if (_sessionCts is not null)
            {
                await _sessionCts.CancelAsync().ConfigureAwait(false);
            }

            if (MessageProcessingTask is not null)
            {
                try
                {
                    await MessageProcessingTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation
                }
            }
        }
        finally
        {
            _session?.Dispose();
            _sessionCts?.Dispose();
        }

        _logger.EndpointCleanedUp(EndpointName);
    }

    /// <summary>
    /// Gets the current session.
    /// </summary>
    /// <returns>The current session.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the session is not started.</exception>
    protected IMcpSession GetSessionOrThrow()
        => _session ?? throw new InvalidOperationException($"This should be unreachable from public API! Call {nameof(StartSession)} before sending messages.");
}
