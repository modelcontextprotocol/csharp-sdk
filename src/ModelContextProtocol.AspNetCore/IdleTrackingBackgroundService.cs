using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol.Transport;

namespace ModelContextProtocol.AspNetCore;

internal sealed partial class IdleTrackingBackgroundService(
    StreamableHttpHandler handler,
    IOptions<HttpServerTransportOptions> options,
    ILogger<IdleTrackingBackgroundService> logger) : BackgroundService
{
    // The compiler will complain about the parameter being unused otherwise despite the source generator.
    private ILogger _logger = logger;

    // We can make this configurable once we properly harden the MCP server. In the meantime, anyone running
    // this should be taking a cattle not pets approach to their servers and be able to launch more processes
    // to handle more than 10,000 idle sessions at a time.
    private const int MaxIdleSessionCount = 10_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeProvider = options.Value.TimeProvider;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);

        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                var idleActivityCutoff = timeProvider.GetUtcNow().Ticks - options.Value.IdleTimeout.Ticks;

                var idleCount = 0;
                foreach (var (_, session) in handler.Sessions)
                {
                    if (session.IsActive || session.SessionClosed.IsCancellationRequested)
                    {
                        // There's a request currently active or the session is already being closed.
                        continue;
                    }

                    idleCount++;
                    if (idleCount == MaxIdleSessionCount)
                    {
                        // By checking for exact equality in this log, we log it at most once every 5 seconds.
                        LogMaxSessionIdleCountExceeded();
                    }
                    else if (idleCount < MaxIdleSessionCount && session.LastActivityTicks > idleActivityCutoff)
                    {
                        continue;
                    }

                    if (handler.Sessions.TryRemove(session.Id, out var removedSession))
                    {
                        LogSessionIdle(removedSession.Id);

                        // Don't slow down the idle tracking loop. DisposeSessionAsync logs. We only await during graceful shutdown.
                        _ = DisposeSessionAsync(removedSession);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (stoppingToken.IsCancellationRequested)
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
        }
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

    [LoggerMessage(Level = LogLevel.Critical, Message = "Exceeded static maximum of 10,000 idle connections. Now clearing all inactive connections regardless of timeout.")]
    private partial void LogMaxSessionIdleCountExceeded();

    [LoggerMessage(Level = LogLevel.Error, Message = "Error disposing the IMcpServer for session {sessionId}.")]
    private partial void LogSessionDisposeError(string sessionId, Exception ex);
}
