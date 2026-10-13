using System.Diagnostics;

namespace ModelContextProtocol.Client;

/// <summary>Tracks server-wait time without charging OAuth work to the discovery probe.</summary>
internal sealed class DiscoverProbe : IDisposable
{
    private static readonly TimeSpan s_maxTimerInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly TimeSpan _timeout;
    private readonly Timer _timer;
    private int _suspensions;
    private bool _disposed;

    public DiscoverProbe(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _timeout = timeout;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new Timer(_ => OnTimeout(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        ScheduleTimer();
    }

    public CancellationToken Token => _cancellation.Token;

    public IDisposable Suspend()
    {
        lock (_sync)
        {
            Token.ThrowIfCancellationRequested();
            if (_suspensions++ == 0)
            {
                _elapsed.Stop();
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
        return new Suspension(this);
    }

    private void Resume()
    {
        lock (_sync)
        {
            if (_disposed ||
                --_suspensions != 0)
            {
                return;
            }

            _elapsed.Start();
            ScheduleTimer();
        }
    }

    private void ScheduleTimer()
    {
        if (_timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        var remaining = _timeout - _elapsed.Elapsed;
        var interval = remaining > s_maxTimerInterval ? s_maxTimerInterval : remaining;
        _timer.Change(interval > TimeSpan.Zero ? interval : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private void OnTimeout()
    {
        // Serialize expiry with suspension and disposal, including a timer callback that was
        // already queued when OAuth suspended the budget.
        lock (_sync)
        {
            if (_disposed ||
                _suspensions != 0 ||
                _cancellation.IsCancellationRequested)
            {
                return;
            }

            var remaining = _timeout - _elapsed.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                ScheduleTimer();
                return;
            }

            _cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _timer.Dispose();
            _cancellation.Dispose();
        }
    }

    private sealed class Suspension(DiscoverProbe probe) : IDisposable
    {
        private DiscoverProbe? _probe = probe;

        public void Dispose() => Interlocked.Exchange(ref _probe, null)?.Resume();
    }
}
