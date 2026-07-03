using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore;

internal sealed class StreamableHttpSession(
    string sessionId,
    StreamableHttpServerTransport transport,
    McpServer server,
    UserIdClaim? userId,
    StatefulSessionManager sessionManager) : IAsyncDisposable
{
    private int _referenceCount;
    private SessionState _state;
    private readonly object _stateLock = new();

    private int _getRequestStarted;
    private readonly CancellationTokenSource _disposeCts = new();

    public string Id => sessionId;
    public StreamableHttpServerTransport Transport => transport;
    public McpServer Server => server;
    private StatefulSessionManager SessionManager => sessionManager;

    public CancellationToken SessionClosed => _disposeCts.Token;
    public bool IsActive => !SessionClosed.IsCancellationRequested && _referenceCount > 0;
    public long LastActivityTicks { get; private set; } = sessionManager.TimeProvider.GetTimestamp();

    public Task ServerRunTask { get; set; } = Task.CompletedTask;

    public async ValueTask<IAsyncDisposable> AcquireReferenceAsync(CancellationToken cancellationToken)
    {
        // The StreamableHttpSession is not stored between requests in stateless mode. Instead, the session is recreated from the MCP-Session-Id.
        // Stateless sessions are 1:1 with HTTP requests and are outlived by the MCP session tracked by the Mcp-Session-Id.
        // Non-stateless sessions are 1:1 with the Mcp-Session-Id and outlive the POST request.
        // Non-stateless sessions get disposed by a DELETE request or the IdleTrackingBackgroundService.
        if (transport.Stateless)
        {
            return this;
        }

        SessionState startingState;

        lock (_stateLock)
        {
            startingState = _state;
            _referenceCount++;

            switch (startingState)
            {
                case SessionState.Uninitialized:
                    Debug.Assert(_referenceCount == 1, "The _referenceCount should start at 1 when the StreamableHttpSession is uninitialized.");
                    _state = SessionState.Started;
                    break;
                case SessionState.Started:
                    if (_referenceCount == 1)
                    {
                        sessionManager.DecrementIdleSessionCount();
                    }
                    // Update LastActivityTicks when acquiring reference in Started state to prevent timeout during active usage
                    LastActivityTicks = sessionManager.TimeProvider.GetTimestamp();
                    break;
                case SessionState.Disposed:
                    throw new ObjectDisposedException(nameof(StreamableHttpSession));
            }
        }

        if (startingState == SessionState.Uninitialized)
        {
            await sessionManager.StartNewSessionAsync(this, cancellationToken);
        }

        return new UnreferenceDisposable(this);
    }

    /// <summary>
    /// Ensures the session is registered with the session manager without acquiring a reference.
    /// No-ops if the session is already started.
    /// </summary>
    public async ValueTask EnsureStartedAsync(CancellationToken cancellationToken)
    {
        bool needsStart;
        lock (_stateLock)
        {
            needsStart = _state == SessionState.Uninitialized;
            if (needsStart)
            {
                _state = SessionState.Started;
            }
        }

        if (needsStart)
        {
            await sessionManager.StartNewSessionAsync(this, cancellationToken);

            // Session is registered with 0 references (idle), so reflect that in the idle count.
            sessionManager.IncrementIdleSessionCount();
        }
    }

    public bool TryStartGetRequest() => Interlocked.Exchange(ref _getRequestStarted, 1) == 0;
    public bool HasSameUserId(ClaimsPrincipal user) => userId == StreamableHttpHandler.GetUserIdClaim(user);

    public async ValueTask DisposeAsync()
    {
        var wasIdle = false;

        lock (_stateLock)
        {
            switch (_state)
            {
                case SessionState.Uninitialized:
                    break;
                case SessionState.Started:
                    if (_referenceCount == 0)
                    {
                        wasIdle = true;
                    }
                    break;
                case SessionState.Disposed:
                    return;
            }

            _state = SessionState.Disposed;
        }

        try
        {
            try
            {
                // Dispose transport first to complete the incoming MessageReader gracefully and avoid a potentially unnecessary OCE.
                await transport.DisposeAsync();
                await _disposeCts.CancelAsync();

                await ServerRunTask;
            }
            finally
            {
                await server.DisposeAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (wasIdle)
            {
                sessionManager.DecrementIdleSessionCount();
            }
            _disposeCts.Dispose();
        }
    }

    private sealed class UnreferenceDisposable(StreamableHttpSession session) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lock (session._stateLock)
            {
                Debug.Assert(session._state != SessionState.Uninitialized, "The session should have been initialized.");
                if (session._state != SessionState.Disposed && --session._referenceCount == 0)
                {
                    var sessionManager = session.SessionManager;
                    session.LastActivityTicks = sessionManager.TimeProvider.GetTimestamp();
                    sessionManager.IncrementIdleSessionCount();
                }
            }

            return default;
        }
    }

    private enum SessionState
    {
        Uninitialized,
        Started,
        Disposed
    }
}
