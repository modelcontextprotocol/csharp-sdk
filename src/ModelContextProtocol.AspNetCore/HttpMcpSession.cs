using ModelContextProtocol.AspNetCore.Stateless;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore;

internal sealed class HttpMcpSession<TTransport>(
    string sessionId,
    TTransport transport,
    UserIdClaim? userId,
    TimeProvider timeProvider) : IAsyncDisposable
    where TTransport : ITransport
{
    private int _referenceCount;
    private int _getRequestStarted;
    private CancellationTokenSource _disposeCts = new();

    public string Id { get; } = sessionId;
    public TTransport Transport { get; } = transport;
    public UserIdClaim? UserIdClaim { get; } = userId;

    public CancellationToken SessionClosed => _disposeCts.Token;

    public bool IsActive => !SessionClosed.IsCancellationRequested && _referenceCount > 0;
    public long LastActivityTicks { get; private set; } = timeProvider.GetTimestamp();

    public IMcpServer? Server { get; set; }
    public Task? ServerRunTask { get; set; }

    public IDisposable AcquireReference()
    {
        Interlocked.Increment(ref _referenceCount);
        return new UnreferenceDisposable(this, timeProvider);
    }

    public bool TryStartGetRequest() => Interlocked.Exchange(ref _getRequestStarted, 1) == 0;

    public async ValueTask DisposeAsync()
    {
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
            }
        }
    }

    public bool HasSameUserId(ClaimsPrincipal user)
        => UserIdClaim == StreamableHttpHandler.GetUserIdClaim(user);

    private sealed class UnreferenceDisposable(HttpMcpSession<TTransport> session, TimeProvider timeProvider) : IDisposable
    {
        public void Dispose()
        {
            if (Interlocked.Decrement(ref session._referenceCount) == 0)
            {
                session.LastActivityTicks = timeProvider.GetTimestamp();
            }
        }
    }
}
