using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

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
    /// <summary>Cached naming information used for name/version when none is specified.</summary>
    internal static AssemblyName DefaultAssemblyName { get; } = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName();

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

    protected RequestHandlers RequestHandlers { get; } = [];

    protected NotificationHandlers NotificationHandlers { get; } = new();

    public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        => GetSessionOrThrow().SendRequestAsync(request, cancellationToken);

    public Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
        => GetSessionOrThrow().SendMessageAsync(message, cancellationToken);

    public IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, Task> handler) =>
        GetSessionOrThrow().RegisterNotificationHandler(method, handler);

    /// <summary>
    /// Gets the name of the endpoint for logging and debug purposes.
    /// </summary>
    public abstract string EndpointName { get; }

    /// <summary>
    /// Task that processes incoming messages from the transport.
    /// </summary>
    protected Task? MessageProcessingTask { get; private set; }

    protected void InitializeSession(ITransport sessionTransport)
    {
        _session = new McpSession(this is IMcpServer, sessionTransport, EndpointName, RequestHandlers, NotificationHandlers, _logger);
    }

    [MemberNotNull(nameof(MessageProcessingTask))]
    protected void StartSession(ITransport sessionTransport, CancellationToken fullSessionCancellationToken)
    {
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(fullSessionCancellationToken);
        MessageProcessingTask = GetSessionOrThrow().ProcessMessagesAsync(_sessionCts.Token);
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
        => _session ?? throw new InvalidOperationException($"This should be unreachable from public API! Call {nameof(InitializeSession)} before sending messages.");
}