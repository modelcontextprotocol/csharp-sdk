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

    /// <inheritdoc/>
    public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        => GetSessionOrThrow().SendRequestAsync(request, cancellationToken);

    /// <inheritdoc/>
    /// <summary>
    /// Sends a JSON-RPC message to the connected endpoint.
    /// </summary>
    /// <param name="message">The JSON-RPC message to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// This method delegates to the current session's <see cref="McpSession.SendMessageAsync"/> method.
    /// It will throw an <see cref="InvalidOperationException"/> if called before <see cref="InitializeSession"/> 
    /// is called to establish a session.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when no session has been initialized.</exception>

    public IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, Task> handler) =>
        GetSessionOrThrow().RegisterNotificationHandler(method, handler);

    /// <summary>
    /// Gets the name of the endpoint for logging and debug purposes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The endpoint name uniquely identifies a client or server connection in logs and diagnostics.
    /// It typically includes identifying information such as connection ID, name, and/or connection type.
    /// </para>
    /// <para>
    /// For clients, this is typically formatted as "Client ({ServerConfig.Id}: {ServerConfig.Name})".
    /// For servers, this is typically formatted as "Server ({ServerInfo.Name} {ServerInfo.Version})".
    /// </para>
    /// <para>
    /// This property is extensively used throughout the logging system to provide context
    /// for messages related to a specific endpoint's activities, errors, and lifecycle events.
    /// </para>
    /// </remarks>
    public abstract string EndpointName { get; }

    /// <summary>
    /// Task that processes incoming messages from the transport.
    /// </summary>
    protected Task? MessageProcessingTask { get; private set; }

    /// <summary>
    /// Initializes a new MCP session with the provided transport.
    /// </summary>
    /// <param name="sessionTransport">Transport to use for communicating with the remote endpoint.</param>
    /// <remarks>
    /// <para>
    /// This method must be called before sending any messages through this endpoint. It establishes
    /// the internal session state necessary for message processing, but does not start actively processing messages yet.
    /// </para>
    /// <para>
    /// After calling this method, you typically should follow up with a call to <see cref="StartSession"/> to begin
    /// message processing, and for clients, sending an initial <see cref="RequestMethods.Initialize"/> request.
    /// </para>
    /// <para>
    /// The session is not considered fully established until the initialization protocol exchange is complete:
    /// - Client sends Initialize request
    /// - Server responds with initialization result
    /// - Client sends Initialized notification
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Asynchronously releases all resources used by this endpoint in a thread-safe manner.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    /// <remarks>
    /// This method acquires a lock to ensure that disposal is thread-safe. It then delegates to 
    /// <see cref="DisposeUnsynchronizedAsync"/> to perform the actual resource cleanup.
    /// Multiple calls to this method will only result in resources being disposed once.
    /// </remarks>
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
    /// Asynchronously releases resources used by the endpoint without any synchronization.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    /// <remarks>
    /// This method is called by <see cref="DisposeAsync"/> after it has acquired a lock.
    /// It cancels any active session and waits for message processing to complete.
    /// Derived classes should override this method to perform additional cleanup specific to their implementation,
    /// and should call the base implementation.
    /// </remarks>
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
    /// Gets the current session or throws an exception if no session has been initialized.
    /// </summary>
    /// <returns>The active <see cref="McpSession"/> instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when attempting to access the session before calling <see cref="InitializeSession"/>.
    /// </exception>
    /// <remarks>
    /// This method is used internally by the endpoint to access the session for sending messages
    /// and registering notification handlers. It ensures that <see cref="InitializeSession"/> has
    /// been called before any operations that require an active session.
    /// </remarks>
    protected McpSession GetSessionOrThrow()
        => _session ?? throw new InvalidOperationException($"This should be unreachable from public API! Call {nameof(InitializeSession)} before sending messages.");
}
