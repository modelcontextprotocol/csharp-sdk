using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore;

internal sealed class HttpMcpSession<TTransport>(string sessionId, TTransport transport, ClaimsPrincipal user, TimeProvider timeProvider) : IAsyncDisposable
    where TTransport : ITransport
{
    private int _referenceCount;
    private int _getRequestStarted;
    private CancellationTokenSource _disposeCts = new();

    public string Id { get; } = sessionId;
    public TTransport Transport { get; } = transport;
    public (string Type, string Value, string Issuer)? UserIdClaim { get; } = GetUserIdClaim(user);

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
        => UserIdClaim == GetUserIdClaim(user);

    // SignalR only checks for ClaimTypes.NameIdentifier in HttpConnectionDispatcher, but AspNetCore.Antiforgery checks that plus the sub and UPN claims.
    // However, we short-circuit unlike antiforgery since we expect to call this to verify MCP messages a lot more frequently than
    // verifying antiforgery tokens from <form> posts.
    private static (string Type, string Value, string Issuer)? GetUserIdClaim(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.Upn);

        if (claim is { } idClaim)
        {
            return (idClaim.Type, idClaim.Value, idClaim.Issuer);
        }

        return null;
    }

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
