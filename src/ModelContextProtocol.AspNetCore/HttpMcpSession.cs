using ModelContextProtocol.AspNetCore.Stateless;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;

namespace ModelContextProtocol.AspNetCore;

internal sealed class HttpMcpSession<TTransport>(
    string sessionId,
    TTransport transport,
    UserIdClaim? userId,
    TimeProvider timeProvider,
    SemaphoreSlim? idleSessionSemaphore = null) : IAsyncDisposable
    where TTransport : ITransport
{
    private int _referenceCount;
    private int _getRequestStarted;
    private bool _isDisposed;

    private readonly SemaphoreSlim? _idleSessionSemaphore = idleSessionSemaphore;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _referenceCountLock = new();

    public string Id { get; } = sessionId;
    public TTransport Transport { get; } = transport;
    public UserIdClaim? UserIdClaim { get; } = userId;

    public CancellationToken SessionClosed => _disposeCts.Token;

    public bool IsActive => !SessionClosed.IsCancellationRequested && _referenceCount > 0;
    public long LastActivityTicks { get; private set; } = timeProvider.GetTimestamp();

    private TimeProvider TimeProvider => timeProvider;

    public IMcpServer? Server { get; set; }
    public Task? ServerRunTask { get; set; }

    public IAsyncDisposable AcquireReference()
    {
        Debug.Assert(_idleSessionSemaphore is not null, "Only StreamableHttpHandler should call AcquireReference.");

        lock (_referenceCountLock)
        {
            if (!_isDisposed && ++_referenceCount == 1)
            {
                // Non-idle sessions should not prevent session creation.
                _idleSessionSemaphore.Release();
            }
        }

        return new UnreferenceDisposable(this);
    }

    public bool TryStartGetRequest() => Interlocked.Exchange(ref _getRequestStarted, 1) == 0;

    public async ValueTask DisposeAsync()
    {
        bool shouldReleaseIdleSessionSemaphore;

        lock (_referenceCountLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            shouldReleaseIdleSessionSemaphore = _referenceCount == 0;
        }

        try
        {
            await _disposeCts.CancelAsync();

            if (ServerRunTask is not null)
            {
                await ServerRunTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                if (Server is not null)
                {
                    await Server.DisposeAsync();
                }
            }
            finally
            {
                await Transport.DisposeAsync();
                _disposeCts.Dispose();

                // If the session was disposed while it was inactive, we need to release the semaphore
                // to allow new sessions to be created.
                if (_idleSessionSemaphore is not null && shouldReleaseIdleSessionSemaphore)
                {
                    _idleSessionSemaphore.Release();
                }
            }
        }
    }

    public bool HasSameUserId(ClaimsPrincipal user) => UserIdClaim == StreamableHttpHandler.GetUserIdClaim(user);

    private sealed class UnreferenceDisposable(HttpMcpSession<TTransport> session) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Debug.Assert(session._idleSessionSemaphore is not null, "Only StreamableHttpHandler should call AcquireReference.");

            bool shouldMarkSessionIdle;

            lock (session._referenceCountLock)
            {
                shouldMarkSessionIdle = !session._isDisposed && --session._referenceCount == 0;
            }

            if (shouldMarkSessionIdle)
            {
                session.LastActivityTicks = session.TimeProvider.GetTimestamp();

                // Acquire semaphore when session becomes inactive (reference count goes to 0) to slow
                // down session creation until idle sessions are disposed by the background service.
                await session._idleSessionSemaphore.WaitAsync();
            }
        }
    }
}
