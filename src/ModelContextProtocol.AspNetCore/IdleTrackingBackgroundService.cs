using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ModelContextProtocol.AspNetCore;

internal sealed partial class IdleTrackingBackgroundService(
    StatefulSessionManager sessions,
    IOptions<HttpServerTransportOptions> options,
    IHostApplicationLifetime appLifetime,
    ILogger<IdleTrackingBackgroundService> logger) : BackgroundService
{
    // Workaround for https://github.com/dotnet/runtime/issues/91121. This is fixed in .NET 9 and later.
    private readonly ILogger _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Still run loop given infinite IdleTimeout to enforce the MaxIdleSessionCount and assist graceful shutdown.
        if (options.Value.IdleTimeout != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(options.Value.IdleTimeout, TimeSpan.Zero);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.Value.MaxIdleSessionCount, 0);

        try
        {
            var timeProvider = options.Value.TimeProvider;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await sessions.PruneIdleSessionsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                await sessions.DisposeAllSessionsAsync();
            }
            finally
            {
                if (!stoppingToken.IsCancellationRequested)
                {
                    // Something went terribly wrong. A very unexpected exception must be bubbling up, but let's ensure we also stop the application,
                    // so that it hopefully gets looked at and restarted. This shouldn't really be reachable.
                    appLifetime.StopApplication();
                    IdleTrackingBackgroundServiceStoppedUnexpectedly();
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "The IdleTrackingBackgroundService has stopped unexpectedly.")]
    private partial void IdleTrackingBackgroundServiceStoppedUnexpectedly();
}