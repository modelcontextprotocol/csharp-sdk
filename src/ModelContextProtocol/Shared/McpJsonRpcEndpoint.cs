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
internal abstract class McpJsonRpcEndpoint : IAsyncDisposable
{
    private readonly RequestHandlers _requestHandlers = [];
    private readonly NotificationHandlers _notificationHandlers = [];

    private McpSession? _session;
    private CancellationTokenSource? _sessionCts;
    private int _started;

    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private bool _disposed;

    protected readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpJsonRpcEndpoint"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    protected McpJsonRpcEndpoint(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;
    }

    protected void SetRequestHandler<TRequest, TResponse>(string method, Func<TRequest?, CancellationToken, Task<TResponse>> handler)
        => _requestHandlers.Set(method, handler);

    public void AddNotificationHandler(string method, Func<JsonRpcNotification, Task> handler)
        => _notificationHandlers.Add(method, handler);

    public Task<TResult> SendRequestAsync<TResult>(JsonRpcRequest request, CancellationToken cancellationToken = default) where TResult : class
        => GetSessionOrThrow().SendRequestAsync<TResult>(request, cancellationToken);

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

    protected void InitializeSession(ITransport sessionTransport)
    {
        _session = new McpSession(sessionTransport, EndpointName, _requestHandlers, _notificationHandlers, _logger);
    }

    [MemberNotNull(nameof(MessageProcessingTask))]
    protected void StartSession(CancellationToken fullSessionCancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The MCP session has already stared.");
        }

        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(fullSessionCancellationToken);
        MessageProcessingTask = GetSessionOrThrow().ProcessMessagesAsync(_sessionCts.Token);
    }

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

        _session?.Dispose();
        _sessionCts?.Dispose();

        _logger.EndpointCleanedUp(EndpointName);
    }

    protected McpSession GetSessionOrThrow()
        => _session ?? throw new InvalidOperationException($"This should be unreachable from public API! Call {nameof(InitializeSession)} before sending messages.");
}
