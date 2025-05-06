using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol.Transport;

namespace ModelContextProtocol.AspNetCore;

internal sealed partial class IdleTrackingBackgroundService(
    StreamableHttpHandler handler,
    IOptions<HttpServerTransportOptions> options,
    IHostApplicationLifetime appLifetime,
    ILogger<IdleTrackingBackgroundService> logger) : BackgroundService
{
    // The compiler will complain about the parameter being unused otherwise despite the source generator.
    private ILogger _logger = logger;

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

            var idleTimeoutTicks = options.Value.IdleTimeout.Ticks;
            var maxIdleSessionCount = options.Value.MaxIdleSessionCount;

            // The default ValueTuple Comparer will check the first item then the second which preserves both order and uniqueness.
            var idleSessions = new SortedSet<(long Timestamp, string SessionId)>();

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                var idleActivityCutoff = idleTimeoutTicks switch
                {
                    < 0 => long.MinValue,
                    var ticks => timeProvider.GetTimestamp() - ticks,
                };

                foreach (var (_, session) in handler.Sessions)
                {
                    if (session.IsActive || session.SessionClosed.IsCancellationRequested)
                    {
                        // There's a request currently active or the session is already being closed.
                        continue;
                    }

                    if (session.LastActivityTicks < idleActivityCutoff)
                    {
                        RemoveAndCloseSession(session.Id);
                        continue;
                    }

                    idleSessions.Add((session.LastActivityTicks, session.Id));

                    // Emit critical log at most once every 5 seconds the idle count it exceeded,
                    // since the IdleTimeout will no longer be respected.
                    if (idleSessions.Count == maxIdleSessionCount + 1)
                    {
                        LogMaxSessionIdleCountExceeded(maxIdleSessionCount);
                    }
                }

                if (idleSessions.Count > maxIdleSessionCount)
                {
                    var sessionsToPrune = idleSessions.ToArray()[..^maxIdleSessionCount];
                    foreach (var (_, id) in sessionsToPrune)
                    {
                        RemoveAndCloseSession(id);
                    }
                }

                idleSessions.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                List<Task> disposeSessionTasks = [];

                foreach (var (sessionKey, _) in handler.Sessions)
                {
                    if (handler.Sessions.TryRemove(sessionKey, out var session))
                    {
                        disposeSessionTasks.Add(DisposeSessionAsync(session));
                    }
                }

                await Task.WhenAll(disposeSessionTasks);
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

    private void RemoveAndCloseSession(string sessionId)
    {
        if (!handler.Sessions.TryRemove(sessionId, out var session))
        {
            return;
        }

        LogSessionIdle(session.Id);
        // Don't slow down the idle tracking loop. DisposeSessionAsync logs. We only await during graceful shutdown.
        _ = DisposeSessionAsync(session);
    }

    private async Task DisposeSessionAsync(HttpMcpSession<StreamableHttpServerTransport> session)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception ex)
        {
            LogSessionDisposeError(session.Id, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Closing idle session {sessionId}.")]
    private partial void LogSessionIdle(string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error disposing session {sessionId}.")]
    private partial void LogSessionDisposeError(string sessionId, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Exceeded maximum of {maxIdleSessionCount} idle sessions. Now closing sessions active more recently than configured IdleTimeout.")]
    private partial void LogMaxSessionIdleCountExceeded(int maxIdleSessionCount);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The IdleTrackingBackgroundService has stopped unexpectedly.")]
    private partial void IdleTrackingBackgroundServiceStoppedUnexpectedly();
}
