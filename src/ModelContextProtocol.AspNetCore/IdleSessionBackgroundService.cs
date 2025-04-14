using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ModelContextProtocol.AspNetCore;

internal sealed class IdleSessionBackgroundService(StreamableHttpHandler handler, IOptions<HttpServerTransportOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeProvider = options.Value.TimeProvider;
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);

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
                    await removedSession.Transport.DisposeAsync();
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var (sessionKey, _) in handler.Sessions)
            {
                if (handler.Sessions.TryRemove(sessionKey, out var session))
                {
                    await session.Transport.DisposeAsync();
                }
            }
        }
        finally
        {
            await base.StopAsync(cancellationToken);
        }
    }
}
