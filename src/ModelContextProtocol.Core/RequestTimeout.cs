namespace ModelContextProtocol;

/// <summary>A request-local timer that can be suspended without suspending linked cancellation.</summary>
/// <remarks>
/// Owned by one awaited discovery request, linked to the enclosing initialization scope.
/// Suspension scopes must be sequential and disposed before their owner.
/// Cancellation may race with suspension, but an expired timer cannot be restarted.
/// </remarks>
internal sealed class RequestTimeout : IDisposable
{
    private readonly CancellationTokenSource _source;
    private readonly TimeSpan _timeout;

    public RequestTimeout(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _timeout = timeout;
        _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Token = _source.Token;
        _source.CancelAfter(timeout);
    }

    public CancellationToken Token { get; }

    public void Stop() => _source.CancelAfter(Timeout.InfiniteTimeSpan);

    public Suspension Suspend()
    {
        Stop();
        return new Suspension(this);
    }

    public void Dispose() => _source.Dispose();

    public readonly struct Suspension(RequestTimeout owner) : IDisposable
    {
        public void Dispose()
        {
            if (!owner.Token.IsCancellationRequested)
            {
                owner._source.CancelAfter(owner._timeout);
            }
        }
    }
}
