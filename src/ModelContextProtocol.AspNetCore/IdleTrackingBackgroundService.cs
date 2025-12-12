using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ModelContextProtocol.AspNetCore;

internal sealed partial class IdleTrackingBackgroundService : BackgroundService
{
    private readonly StatefulSessionManager _sessions;
    private readonly IOptions<HttpServerTransportOptions> _options;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger _logger;

    public IdleTrackingBackgroundService(
        StatefulSessionManager sessions,
        IOptions<HttpServerTransportOptions> options,
        IHostApplicationLifetime appLifetime,
        ILogger<IdleTrackingBackgroundService> logger)
    {
        // Still run loop given infinite IdleTimeout to enforce the MaxIdleSessionCount and assist graceful shutdown.
        if (options.Value.IdleTimeout != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(options.Value.IdleTimeout, TimeSpan.Zero);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.Value.MaxIdleSessionCount, 0);

        _sessions = sessions;
        _options = options;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var timeProvider = _options.Value.TimeProvider;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _sessions.PruneIdleSessionsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                await _sessions.DisposeAllSessionsAsync();
            }
            finally
            {
                if (!stoppingToken.IsCancellationRequested)
                {
                    // Something went terribly wrong. A very unexpected exception must be bubbling up, but let's ensure we also stop the application,
                    // so that it hopefully gets looked at and restarted. This shouldn't really be reachable.
                    _appLifetime.StopApplication();
                    IdleTrackingBackgroundServiceStoppedUnexpectedly();
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "The IdleTrackingBackgroundService has stopped unexpectedly.")]
    private partial void IdleTrackingBackgroundServiceStoppedUnexpectedly();
}