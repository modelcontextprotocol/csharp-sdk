using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace ModelContextProtocol;

/// <summary>
/// Hosted service for a single-session (e.g. stdio) MCP server.
/// </summary>
/// <param name="session">The server representing the session being hosted.</param>
/// <param name="lifetime">
/// The host's application lifetime. If available, it will have termination requested when the session's run completes.
/// </param>
internal sealed class SingleSessionMcpServerHostedService(McpServer session, IHostApplicationLifetime? lifetime = null) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await session.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            lifetime?.StopApplication();
        }
    }
}
