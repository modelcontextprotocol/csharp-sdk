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
    private ILogger _ = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeProvider = options.Value.TimeProvider;
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);

        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                var idleActivityCutoff = timeProvider.GetUtcNow().Ticks - options.Value.IdleTimeout.Ticks;

                foreach (var (_, session) in handler.Sessions)
                {
                    if (session.IsActive || session.LastActivityTicks > idleActivityCutoff)
                    {
                        continue;
                    }

                    if (handler.Sessions.TryRemove(session.Id, out var removedSession))
                    {
                        LogSessionIdle(removedSession.Id);
                        await DisposeSessionAsync(removedSession);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Error disposing the IMcpServer for session {sessionId}.")]
    private partial void LogSessionDisposeError(string sessionId, Exception ex);
}
