using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Shared;

/// <summary>
/// Base class for an MCP JSON-RPC endpoint. This covers both MCP clients and servers.
/// It is not supported, nor necessary, to implement both client and server functionality in the same class.
/// If an application needs to act as both a client and a server, it should use separate objects for each.
/// This is especially true as a client represents a connection to one and only one server, and vice versa.
/// Any multi-client or multi-server functionality should be implemented at a higher level of abstraction.
/// </summary>
internal abstract class McpEndpoint : IAsyncDisposable
{
    private readonly RequestHandlers _requestHandlers = [];
    private readonly NotificationHandlers _notificationHandlers = [];

    private McpSession? _session;
    private CancellationTokenSource? _sessionCts;

    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private bool _disposed;

    protected readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpEndpoint"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    protected McpEndpoint(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;
    }

    protected void SetRequestHandler<TRequest, TResponse>(
        string method,
        Func<TRequest?, CancellationToken, Task<TResponse>> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo)

        => _requestHandlers.Set(method, handler, requestTypeInfo, responseTypeInfo);

    public void AddNotificationHandler(string method, Func<JsonRpcNotification, Task> handler)
        => _notificationHandlers.Add(method, handler);

    public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        => GetSessionOrThrow().SendRequestAsync(request, cancellationToken);

    public Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
        => GetSessionOrThrow().SendMessageAsync(message, cancellationToken);

    /// <summary>
    /// Gets the name of the endpoint for logging and debug purposes.
    /// </summary>
    public abstract string EndpointName { get; }

    /// <summary>
    /// Task that processes incoming messages from the transport.
    /// </summary>
    protected Task? MessageProcessingTask { get; private set; }

    [MemberNotNull(nameof(MessageProcessingTask))]
    protected void StartSession(ITransport sessionTransport)
    {
        _sessionCts = new CancellationTokenSource();
        _session = new McpSession(this is IMcpServer, sessionTransport, EndpointName, _requestHandlers, _notificationHandlers, _logger);
        MessageProcessingTask = _session.ProcessMessagesAsync(_sessionCts.Token);
    }

    protected void CancelSession() => _sessionCts?.Cancel();

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

    protected McpSession GetSessionOrThrow()
        => _session ?? throw new InvalidOperationException($"This should be unreachable from public API! Call {nameof(StartSession)} before sending messages.");
}
