namespace AspNetCoreMcpClient;

public sealed class McpClientCleanupService(
    SessionClientRegistry<McpClientConnection> registry,
    IConfiguration configuration,
    ILogger<McpClientCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(configuration.GetValue("McpServer:CleanupIntervalMinutes", 1));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var removed = await registry.RemoveIdleAsync().ConfigureAwait(false);
            if (removed > 0)
            {
                logger.LogInformation("Disposed {Count} idle MCP client sessions.", removed);
            }
        }
    }
}
