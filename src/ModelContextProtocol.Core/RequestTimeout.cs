namespace ModelContextProtocol;

/// <summary>A request-local timer that can be suspended without suspending linked cancellation.</summary>
/// <remarks>
/// Owned by one awaited initialization or discovery operation, linked to its caller's cancellation.
/// Suspension scopes must be sequential and disposed before their owner.
/// Cancellation may race with suspension, but an expired timer cannot be restarted.
/// </remarks>
internal sealed class RequestTimeout : IDisposable
{
    private readonly CancellationTokenSource _source;
    private readonly ITimer _timer;
    private readonly TimeSpan _timeout;

    public RequestTimeout(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        _timeout = timeout;
        _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Token = _source.Token;
        try
        {
            _timer = timeProvider.CreateTimer(static state =>
            {
                try
                {
                    ((CancellationTokenSource)state!).Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // A timer callback already queued when Dispose ran can outlive the source.
                }
            }, _source, timeout, Timeout.InfiniteTimeSpan);
        }
        catch
        {
            _source.Dispose();
            throw;
        }
    }

    public CancellationToken Token { get; }

    public void Stop() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public Suspension Suspend()
    {
        Stop();
        return new Suspension(this);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _source.Dispose();
    }

    public readonly struct Suspension(RequestTimeout owner) : IDisposable
    {
        public void Dispose()
        {
            if (!owner.Token.IsCancellationRequested)
            {
                owner._timer.Change(owner._timeout, Timeout.InfiniteTimeSpan);
            }
        }
    }
}
