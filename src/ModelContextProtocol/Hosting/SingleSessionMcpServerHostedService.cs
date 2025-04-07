using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Hosting;

/// <summary>
/// Hosted service for a single-session (e.g. stdio) MCP server.
/// </summary>
internal sealed class SingleSessionMcpServerHostedService(IMcpServer session, IHostApplicationLifetime lifetime) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await session.RunAsync(stoppingToken);
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
